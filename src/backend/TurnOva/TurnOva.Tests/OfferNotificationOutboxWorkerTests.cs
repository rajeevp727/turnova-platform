using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TurnOva.API;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class OfferNotificationOutboxWorkerTests
{
    [Fact]
    public async Task WorkerSendsCurrentPendingOfferAndMarksNotificationSent()
    {
        var store = new InMemoryRideOfferStore();
        var state = CreateState("ride-outbox-1", "driver-outbox-1");
        Assert.True(await store.TryCreateAsync(state));
        var hub = new RecordingHubContext(expectedMessages: 1);
        using var worker = CreateWorker(store, hub);

        await worker.StartAsync(CancellationToken.None);
        await hub.WaitForMessagesAsync();
        await worker.StopAsync(CancellationToken.None);

        var message = Assert.Single(hub.Messages);
        Assert.Equal("driver:driver-outbox-1", message.Group);
        Assert.Equal("RideOfferReceived", message.Method);
        Assert.Equal(state.RideId, message.Payload.GetProperty("rideId").GetString());
        Assert.Equal(state.CurrentOfferId, message.Payload.GetProperty("offerId").GetString());

        var saved = await store.GetByRideAsync(state.RideId);
        Assert.Equal("Sent", saved?.NotificationStatus);
        Assert.NotNull(saved?.NotificationSentAtUtc);
    }

    [Fact]
    public async Task WorkerSkipsNotificationWhenOfferIsNoLongerPending()
    {
        var pending = CreateState("ride-outbox-stale", "driver-outbox-stale") with { ETag = "old-etag" };
        var current = pending with { Status = RideOfferStatuses.Accepted, ETag = "new-etag" };
        var store = new StaleSnapshotStore(pending, current);
        var hub = new RecordingHubContext(expectedMessages: 1);
        using var worker = CreateWorker(store, hub);

        await worker.StartAsync(CancellationToken.None);
        await store.ReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(hub.Messages);
    }

    [Fact]
    public async Task WorkerRetriesNotificationAfterTransientSignalRFailure()
    {
        var store = new InMemoryRideOfferStore();
        var state = CreateState("ride-outbox-retry", "driver-outbox-retry");
        Assert.True(await store.TryCreateAsync(state));
        var attempts = 0;
        var hub = new RecordingHubContext(expectedMessages: 1, send: (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("simulated transient SignalR failure");
            return Task.CompletedTask;
        });
        using var worker = CreateWorker(store, hub);

        await worker.StartAsync(CancellationToken.None);
        await hub.WaitForMessagesAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(attempts >= 2);
        Assert.Single(hub.Messages);
        Assert.Equal("Sent", (await store.GetByRideAsync(state.RideId))?.NotificationStatus);
    }

    [Fact]
    public async Task ConcurrentWorkersMayRedeliverSameOfferButKeepStableOfferIdForClientDeduplication()
    {
        var inner = new InMemoryRideOfferStore();
        var state = CreateState("ride-outbox-concurrent", "driver-outbox-concurrent");
        Assert.True(await inner.TryCreateAsync(state));
        var store = new TwoWorkerBarrierStore(inner);
        var hub = new RecordingHubContext(expectedMessages: 2);
        using var first = CreateWorker(store, hub);
        using var second = CreateWorker(store, hub);

        await Task.WhenAll(first.StartAsync(CancellationToken.None), second.StartAsync(CancellationToken.None));
        await hub.WaitForMessagesAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () =>
            (await inner.GetByRideAsync(state.RideId))?.NotificationStatus == "Sent");
        await Task.WhenAll(first.StopAsync(CancellationToken.None), second.StopAsync(CancellationToken.None));

        var messages = hub.Messages.ToArray();
        Assert.Equal(2, messages.Length);
        Assert.All(messages, message => Assert.Equal("driver:driver-outbox-concurrent", message.Group));
        Assert.All(messages, message => Assert.Equal("RideOfferReceived", message.Method));
        Assert.All(messages, message => Assert.Equal(state.CurrentOfferId, message.Payload.GetProperty("offerId").GetString()));
    }

    private static OfferNotificationOutboxWorker CreateWorker(IRideOfferStore store, RecordingHubContext hub) =>
        new(store, hub, TimeProvider.System,
            Options.Create(new DispatchOfferOptions { NotificationPollIntervalSeconds = 1, NotificationBatchSize = 10 }),
            NullLogger<OfferNotificationOutboxWorker>.Instance);

    private static RideOfferState CreateState(string rideId, string driverId)
    {
        var now = DateTimeOffset.UtcNow;
        var offerId = $"offer-{rideId}";
        return new RideOfferState(
            rideId, "hyderabad-central", "auto", offerId, driverId,
            now, now.AddMinutes(1), RideOfferStatuses.Pending,
            [new RideOfferAttempt(offerId, driverId, now, now.AddMinutes(1), RideOfferStatuses.Pending)]);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!await condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed record SentMessage(string Group, string Method, JsonElement Payload);

    private sealed class RecordingHubContext : IHubContext<RideTrackingHub>
    {
        private readonly RecordingHubClients _clients;
        private readonly Func<SentMessage, CancellationToken, Task>? _send;
        private readonly TaskCompletionSource<bool> _messagesReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _expectedMessages;

        public RecordingHubContext(int expectedMessages, Func<SentMessage, CancellationToken, Task>? send = null)
        {
            _expectedMessages = expectedMessages;
            _send = send;
            _clients = new RecordingHubClients(this);
        }

        public ConcurrentQueue<SentMessage> Messages { get; } = new();
        public IHubClients Clients => _clients;
        public IGroupManager Groups { get; } = new NoOpGroupManager();

        public Task WaitForMessagesAsync(TimeSpan? timeout = null) =>
            _messagesReady.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(3));

        private async Task SendAsync(string group, string method, object?[] args, CancellationToken cancellationToken)
        {
            var payload = args.Length == 0
                ? JsonDocument.Parse("{}").RootElement.Clone()
                : JsonSerializer.SerializeToElement(args[0]);
            var message = new SentMessage(group, method, payload);
            if (_send is not null) await _send(message, cancellationToken);
            Messages.Enqueue(message);
            if (Messages.Count >= _expectedMessages) _messagesReady.TrySetResult(true);
        }

        private sealed class RecordingHubClients(RecordingHubContext owner) : IHubClients
        {
            private IClientProxy Proxy(string group) => new RecordingClientProxy(group, owner);
            public IClientProxy All => Proxy("all");
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy("all");
            public IClientProxy Client(string connectionId) => Proxy(connectionId);
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy("clients");
            public IClientProxy Group(string groupName) => Proxy(groupName);
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy("groups");
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy(groupName);
            public IClientProxy User(string userId) => Proxy(userId);
            public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy("users");
            public IClientProxy Others => Proxy("others");
            public IClientProxy OthersInGroup(string groupName) => Proxy(groupName);
        }

        private sealed class RecordingClientProxy(string group, RecordingHubContext owner) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
                owner.SendAsync(group, method, args, cancellationToken);
        }

        private sealed class NoOpGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }

    private sealed class StaleSnapshotStore(RideOfferState pending, RideOfferState current) : IRideOfferStore
    {
        public TaskCompletionSource<bool> ReadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<RideOfferState>> GetPendingNotificationsAsync(int maxItems, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RideOfferState>>([pending]);
        public Task<RideOfferState?> GetByRideAsync(string rideId, CancellationToken cancellationToken = default)
        {
            ReadCompleted.TrySetResult(true);
            return Task.FromResult<RideOfferState?>(current);
        }
        public Task<RideOfferState?> GetPendingForDriverAsync(string driverId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<RideOfferState>> GetExpiredAsync(DateTimeOffset nowUtc, int maxItems, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> TryCreateAsync(RideOfferState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> TryReplaceAsync(RideOfferState state, string expectedETag, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TwoWorkerBarrierStore(IRideOfferStore inner) : IRideOfferStore
    {
        private int _pendingReads;
        private int _rideReads;
        private readonly TaskCompletionSource<bool> _bothPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _bothRides = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<RideOfferState>> GetPendingNotificationsAsync(int maxItems, CancellationToken cancellationToken = default)
        {
            var result = await inner.GetPendingNotificationsAsync(maxItems, cancellationToken);
            if (Interlocked.Increment(ref _pendingReads) == 2) _bothPending.TrySetResult(true);
            await _bothPending.Task.WaitAsync(cancellationToken);
            return result;
        }

        public async Task<RideOfferState?> GetByRideAsync(string rideId, CancellationToken cancellationToken = default)
        {
            var result = await inner.GetByRideAsync(rideId, cancellationToken);
            if (Interlocked.Increment(ref _rideReads) == 2) _bothRides.TrySetResult(true);
            await _bothRides.Task.WaitAsync(cancellationToken);
            return result;
        }

        public Task<RideOfferState?> GetPendingForDriverAsync(string driverId, CancellationToken cancellationToken = default) =>
            inner.GetPendingForDriverAsync(driverId, cancellationToken);
        public Task<IReadOnlyList<RideOfferState>> GetExpiredAsync(DateTimeOffset nowUtc, int maxItems, CancellationToken cancellationToken = default) =>
            inner.GetExpiredAsync(nowUtc, maxItems, cancellationToken);
        public Task<bool> TryCreateAsync(RideOfferState state, CancellationToken cancellationToken = default) =>
            inner.TryCreateAsync(state, cancellationToken);
        public Task<bool> TryReplaceAsync(RideOfferState state, string expectedETag, CancellationToken cancellationToken = default) =>
            inner.TryReplaceAsync(state, expectedETag, cancellationToken);
    }
}
