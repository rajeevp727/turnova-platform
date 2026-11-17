using Microsoft.Extensions.Logging.Abstractions;
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

        Assert.Equal("replacement-owner", acquired.Owner);
        Assert.Equal("etag-new", acquired.ETag);
        Assert.Equal(expired.ETag, store.LastExpectedLease?.ETag);
        Assert.Equal("replacement-owner", store.LastReplacementOwner);
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
