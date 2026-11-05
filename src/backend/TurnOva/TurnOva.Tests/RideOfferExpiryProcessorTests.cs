using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class RideOfferExpiryProcessorTests
{
    [Fact]
    public async Task ProcessBatchAsync_ExpiresOfferAndRotatesDriverOnlyOnce()
    {
        var now = DateTimeOffset.Parse("2026-11-06T10:00:00Z");
        var offers = new InMemoryRideOfferStore();
        var queue = new InMemoryQueueStore();
        await queue.AddAsync(new DriverQueueEntry("driver-a", "zone-a", "auto", now.AddMinutes(-5), 0, true), default);
        await queue.AddAsync(new DriverQueueEntry("driver-b", "zone-a", "auto", now.AddMinutes(-4), 0, true), default);
        var state = State(now);
        Assert.True(await offers.TryCreateAsync(state));
        var processor = new RideOfferExpiryProcessor(offers, queue, new FixedTimeProvider(now));
        Assert.Equal(1, await processor.ProcessBatchAsync(10));
        Assert.Equal(0, await processor.ProcessBatchAsync(10));
        Assert.Equal(new[] { "driver-b", "driver-a" },
            (await queue.GetEligibleAsync("zone-a", "auto", default)).Select(x => x.DriverId));
        var saved = await offers.GetByRideAsync(state.RideId);
        Assert.Equal(RideOfferStatuses.Expired, saved!.Status);
        Assert.Equal(RideOfferStatuses.Expired, saved.Attempts.Single().Status);
        Assert.NotNull(saved.Attempts.Single().ResolvedAtUtc);
    }

    [Fact]
    public async Task ProcessBatchAsync_LeavesOfferPendingWhenQueueMembershipIsMissing()
    {
        var now = DateTimeOffset.Parse("2026-11-06T10:00:00Z");
        var offers = new InMemoryRideOfferStore();
        var state = State(now);
        Assert.True(await offers.TryCreateAsync(state));
        var processor = new RideOfferExpiryProcessor(offers, new InMemoryQueueStore(), new FixedTimeProvider(now));
        Assert.Equal(0, await processor.ProcessBatchAsync(10));
        Assert.Equal(RideOfferStatuses.Pending, (await offers.GetByRideAsync(state.RideId))!.Status);
    }


    [Fact]
    public async Task ProcessBatchAsync_AdvancesAfterOfferIsMarkedExpired()
    {
        var now = DateTimeOffset.Parse("2026-11-06T10:00:00Z");
        var offers = new InMemoryRideOfferStore();
        var queue = new InMemoryQueueStore();
        await queue.AddAsync(new DriverQueueEntry("driver-a", "zone-a", "auto", now.AddMinutes(-5), 0, true), default);
        var state = State(now);
        Assert.True(await offers.TryCreateAsync(state));
        var advancer = new RecordingAdvancer();
        var processor = new RideOfferExpiryProcessor(offers, queue, new FixedTimeProvider(now), advancer);

        Assert.Equal(1, await processor.ProcessBatchAsync(10));
        Assert.Equal(new[] { state.RideId }, advancer.AdvancedRideIds);
        Assert.Equal(RideOfferStatuses.Expired, (await offers.GetByRideAsync(state.RideId))!.Status);
    }

    [Fact]
    public async Task PendingNotifications_ReturnOnlyCurrentPendingOffersAndCanBeMarkedSent()
    {
        var now = DateTimeOffset.Parse("2026-11-06T10:00:00Z");
        var store = new InMemoryRideOfferStore();
        var state = new RideOfferState("ride-notification", "zone-a", "auto", "offer-notification", "driver-a",
            now, now.AddSeconds(30), RideOfferStatuses.Pending,
            [new RideOfferAttempt("offer-notification", "driver-a", now, now.AddSeconds(30), RideOfferStatuses.Pending)]);
        Assert.True(await store.TryCreateAsync(state));

        var pending = await store.GetPendingNotificationsAsync(10);
        var current = Assert.Single(pending);
        Assert.Equal("Pending", current.NotificationStatus);
        Assert.NotNull(current.ETag);
        Assert.True(await store.TryReplaceAsync(current with
        {
            NotificationStatus = "Sent",
            NotificationSentAtUtc = now
        }, current.ETag!));

        Assert.Empty(await store.GetPendingNotificationsAsync(10));
    }

    private sealed class RecordingAdvancer : IRideOfferAdvancer
    {
        public List<string> AdvancedRideIds { get; } = [];
        public Task<bool> AdvanceAsync(string rideId, CancellationToken cancellationToken = default)
        {
            AdvancedRideIds.Add(rideId);
            return Task.FromResult(true);
        }
    }

    private static RideOfferState State(DateTimeOffset now) =>
        new("ride-expiry", "zone-a", "auto", "offer-expiry", "driver-a",
            now.AddMinutes(-2), now.AddMinutes(-1), RideOfferStatuses.Pending,
            [new RideOfferAttempt("offer-expiry", "driver-a", now.AddMinutes(-2), now.AddMinutes(-1), RideOfferStatuses.Pending)]);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}