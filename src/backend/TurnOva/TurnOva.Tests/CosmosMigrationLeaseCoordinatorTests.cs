using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Tests;

public sealed class CosmosMigrationLeaseCoordinatorTests
{
    [Fact]
    public async Task AcquireRecoversExpiredLeaseUsingTheObservedEtag()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = new CosmosMigrationLease(
            "schema-migrations", "previous-owner", "etag-old", now.AddMinutes(-1));
        var store = new FakeLeaseStore
        {
            ReadResult = expired,
            ReplaceResult = new CosmosMigrationLease(
                "schema-migrations", "replacement-owner", "etag-new", now.AddMinutes(30))
        };
        store.CreateResults.Enqueue(null);
        var coordinator = CreateCoordinator(store, new ImmediateDelay());

        var acquired = await coordinator.AcquireAsync(CancellationToken.None);

        Assert.NotEqual(expired.Owner, acquired.Owner);
        Assert.Equal("etag-new", acquired.ETag);
        Assert.Equal(expired.ETag, store.LastExpectedLease?.ETag);
        Assert.Equal(acquired.Owner, store.LastReplacementOwner);
    }

    [Fact]
    public async Task AcquireRetriesCreateWhenExpiredLeaseDisappearsBeforeReplace()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = new CosmosMigrationLease(
            "schema-migrations", "previous-owner", "etag-old", now.AddMinutes(-1));
        var created = new CosmosMigrationLease(
            "schema-migrations", "new-owner", "etag-created", now.AddMinutes(30));
        var store = new FakeLeaseStore
        {
            ReadResult = expired,
            ReplaceResult = null
        };
        store.CreateResults.Enqueue(null);
        store.CreateResults.Enqueue(created);
        var coordinator = CreateCoordinator(store, new ImmediateDelay());

        var acquired = await coordinator.AcquireAsync(CancellationToken.None);

        Assert.Equal(created, acquired);
        Assert.Equal(expired, store.LastExpectedLease);
        Assert.Equal(acquired.Owner, store.LastReplacementOwner);
    }

    [Fact]
    public async Task AcquireBacksOffWhenLeaseDisappearsBetweenCreateAndRead()
    {
        var created = new CosmosMigrationLease(
            "schema-migrations", "new-owner", "etag-created", DateTimeOffset.UtcNow.AddMinutes(30));
        var store = new FakeLeaseStore();
        store.CreateResults.Enqueue(null);
        store.CreateResults.Enqueue(created);
        var delay = new ImmediateDelay();
        var coordinator = CreateCoordinator(store, delay);

        var acquired = await coordinator.AcquireAsync(CancellationToken.None);

        Assert.Equal(created, acquired);
        Assert.Equal(1, delay.CallCount);
    }

    [Fact]
    public async Task AcquireWaitsForAnUnexpiredLeaseThenRetriesCreate()
    {
        var active = new CosmosMigrationLease(
            "schema-migrations", "active-owner", "etag-active", DateTimeOffset.UtcNow.AddHours(1));
        var created = new CosmosMigrationLease(
            "schema-migrations", "new-owner", "etag-created", DateTimeOffset.UtcNow.AddMinutes(30));
        var store = new FakeLeaseStore { ReadResult = active };
        store.CreateResults.Enqueue(null);
        store.CreateResults.Enqueue(created);
        var delay = new ImmediateDelay();
        var coordinator = CreateCoordinator(store, delay);

        var acquired = await coordinator.AcquireAsync(CancellationToken.None);

        Assert.Equal(created, acquired);
        Assert.Equal(1, delay.CallCount);
        Assert.Null(store.LastExpectedLease);
    }

    [Fact]
    public async Task RenewalUsesCurrentEtagAndPreservesOwner()
    {
        var current = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "etag-1", DateTimeOffset.UtcNow.AddMinutes(20));
        var renewed = current with
        {
            ETag = "etag-2",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        var store = new FakeLeaseStore { ReplaceResult = renewed };
        var coordinator = CreateCoordinator(store, new ImmediateDelay());

        var result = await coordinator.RenewOnceAsync(current, CancellationToken.None);

        Assert.Equal("etag-1", store.LastExpectedLease?.ETag);
        Assert.Equal("owner-1", store.LastReplacementOwner);
        Assert.Equal("etag-2", result.ETag);
        Assert.Equal("owner-1", result.Owner);
    }

    [Fact]
    public async Task RenewalLoopCancelsMigrationWhenEtagOwnershipIsLost()
    {
        var current = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "etag-1", DateTimeOffset.UtcNow.AddMinutes(20));
        var store = new FakeLeaseStore { ReplaceResult = null };
        var delay = new ControlledDelay();
        var coordinator = CreateCoordinator(store, delay);
        using var cancellation = new CancellationTokenSource();
        var loop = coordinator.RunRenewalLoopAsync(new CosmosMigrationLeaseState(current), cancellation);

        await delay.WaitUntilRequestedAsync();
        delay.Release();
        await loop;

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal("etag-1", store.LastExpectedLease?.ETag);
    }

    [Fact]
    public async Task RenewalLoopPublishesRenewedEtagBeforeTheNextHeartbeat()
    {
        var current = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "etag-1", DateTimeOffset.UtcNow.AddMinutes(20));
        var renewed = current with { ETag = "etag-2" };
        var store = new FakeLeaseStore { ReplaceResult = renewed };
        var delay = new TwoStepDelay();
        var coordinator = CreateCoordinator(store, delay);
        var state = new CosmosMigrationLeaseState(current);
        using var cancellation = new CancellationTokenSource();
        var loop = coordinator.RunRenewalLoopAsync(state, cancellation);

        await delay.WaitForFirstRequestAsync();
        delay.ReleaseFirst();
        await delay.WaitForSecondRequestAsync();

        Assert.Equal("etag-2", state.Current.ETag);
        cancellation.Cancel();
        await loop;
    }

    [Fact]
    public async Task ReleaseUsesTheLatestRenewedEtag()
    {
        var current = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "etag-1", DateTimeOffset.UtcNow.AddMinutes(20));
        var renewed = current with { ETag = "etag-2" };
        var store = new FakeLeaseStore();
        var coordinator = CreateCoordinator(store, new ImmediateDelay());
        var state = new CosmosMigrationLeaseState(current);
        state.Update(renewed);

        await coordinator.ReleaseAsync(state);

        Assert.Equal("etag-2", store.ReleasedLease?.ETag);
        Assert.Equal("owner-1", store.ReleasedLease?.Owner);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(30, 0, 1)]
    [InlineData(30, 30, 1)]
    [InlineData(30, 10, 0)]
    public async Task AcquireRejectsInvalidLeaseTimingBeforeStoreAccess(
        int leaseMinutes,
        int renewalMinutes,
        int retrySeconds)
    {
        var store = new Mock<ICosmosMigrationLeaseStore>(MockBehavior.Strict);
        var coordinator = new CosmosMigrationLeaseCoordinator(
            store.Object,
            TimeProvider.System,
            new ImmediateDelay(),
            new CosmosMigrationLeaseOptions(
                TimeSpan.FromMinutes(leaseMinutes),
                TimeSpan.FromMinutes(renewalMinutes),
                TimeSpan.FromSeconds(retrySeconds)),
            NullLogger<CosmosMigrationLeaseCoordinator>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.AcquireAsync(CancellationToken.None));

        store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("other-id", "owner-1")]
    [InlineData("schema-migrations", "other-owner")]
    public void LeaseStateRejectsAnUpdateFromAnotherLeaseIdentity(string id, string owner)
    {
        var initial = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "etag-1", DateTimeOffset.UtcNow.AddMinutes(10));
        var state = new CosmosMigrationLeaseState(initial);

        Assert.Throws<InvalidOperationException>(() => state.Update(
            new CosmosMigrationLease(id, owner, "etag-2", DateTimeOffset.UtcNow.AddMinutes(30))));

        Assert.Equal(initial, state.Current);
    }

    private static CosmosMigrationLeaseCoordinator CreateCoordinator(
        FakeLeaseStore store,
        ICosmosMigrationDelay delay) =>
        new(
            store,
            TimeProvider.System,
            delay,
            new CosmosMigrationLeaseOptions(
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMilliseconds(1)),
            NullLogger<CosmosMigrationLeaseCoordinator>.Instance);

    private sealed class FakeLeaseStore : ICosmosMigrationLeaseStore
    {
        public Queue<CosmosMigrationLease?> CreateResults { get; } = new();
        public CosmosMigrationLease? ReadResult { get; init; }
        public CosmosMigrationLease? ReplaceResult { get; init; }
        public CosmosMigrationLease? LastExpectedLease { get; private set; }
        public string? LastReplacementOwner { get; private set; }
        public CosmosMigrationLease? ReleasedLease { get; private set; }

        public Task<CosmosMigrationLease?> TryCreateAsync(
            string owner, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(CreateResults.Count == 0 ? null : CreateResults.Dequeue());

        public Task<CosmosMigrationLease?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ReadResult);

        public Task<CosmosMigrationLease?> TryReplaceAsync(
            CosmosMigrationLease expected, string newOwner, DateTimeOffset expiresAtUtc,
            CancellationToken cancellationToken)
        {
            LastExpectedLease = expected;
            LastReplacementOwner = newOwner;
            return Task.FromResult(ReplaceResult is null
                ? null
                : ReplaceResult with { Owner = newOwner, ExpiresAtUtc = expiresAtUtc });
        }

        public Task<bool> TryReleaseAsync(CosmosMigrationLease expected, CancellationToken cancellationToken)
        {
            ReleasedLease = expected;
            return Task.FromResult(true);
        }
    }

    private sealed class ImmediateDelay : ICosmosMigrationDelay
    {
        public int CallCount { get; private set; }
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TwoStepDelay : ICosmosMigrationDelay
    {
        private readonly TaskCompletionSource firstRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource secondRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;

        public Task WaitForFirstRequestAsync() => firstRequested.Task;
        public Task WaitForSecondRequestAsync() => secondRequested.Task;
        public void ReleaseFirst() => releaseFirst.TrySetResult();

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1)
            {
                firstRequested.TrySetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
                return;
            }

            if (call == 2)
                secondRequested.TrySetResult();

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class ControlledDelay : ICosmosMigrationDelay
    {
        private readonly TaskCompletionSource requested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilRequestedAsync() => requested.Task;
        public void Release() => release.TrySetResult();

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            requested.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
    }
}
