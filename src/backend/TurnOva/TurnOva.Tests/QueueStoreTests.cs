using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class QueueStoreTests
{
    [Fact]
    public async Task RemoveAsync_RemovesOnlyTheMatchingQueueMembership()
    {
        var store = new InMemoryQueueStore();
        var joinedAt = DateTimeOffset.Parse("2026-11-05T00:00:00Z");
        await store.AddAsync(Entry("driver-a", "zone-a", "auto", joinedAt), CancellationToken.None);
        await store.AddAsync(Entry("driver-a", "zone-b", "auto", joinedAt), CancellationToken.None);

        var removed = await store.RemoveAsync("driver-a", "zone-a", "auto", CancellationToken.None);

        Assert.True(removed);
        Assert.Empty(await store.GetEligibleAsync("zone-a", "auto", CancellationToken.None));
        Assert.Single(await store.GetEligibleAsync("zone-b", "auto", CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_ReturnsFalse_WhenMembershipDoesNotExist()
    {
        var store = new InMemoryQueueStore();

        var removed = await store.RemoveAsync("missing-driver", "zone-a", "auto", CancellationToken.None);

        Assert.False(removed);
    }

    [Fact]
    public async Task AddAsync_AllowsDriverToRejoinAtTheEndAfterLeaving()
    {
        var store = new InMemoryQueueStore();
        var firstJoin = DateTimeOffset.Parse("2026-11-05T00:00:00Z");
        await store.AddAsync(Entry("driver-a", "zone-a", "auto", firstJoin), CancellationToken.None);
        await store.AddAsync(Entry("driver-b", "zone-a", "auto", firstJoin.AddMinutes(1)), CancellationToken.None);

        Assert.True(await store.RemoveAsync("driver-a", "zone-a", "auto", CancellationToken.None));

        await store.AddAsync(Entry("driver-a", "zone-a", "auto", firstJoin.AddMinutes(2)), CancellationToken.None);

        var queue = await store.GetEligibleAsync("zone-a", "auto", CancellationToken.None);
        Assert.Equal(new[] { "driver-b", "driver-a" }, queue.Select(entry => entry.DriverId));
    }

    [Fact]
    public async Task ConcurrentDuplicateJoinsForSameDriverKeepSingleQueueMembership()
    {
        var store = new InMemoryQueueStore();
        var entry = Entry("driver-concurrent-join", "zone-a", "auto", DateTimeOffset.UtcNow);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            try
            {
                await store.AddAsync(entry, CancellationToken.None);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }));

        Assert.Single(attempts.Where(succeeded => succeeded));
        var queue = await store.GetEligibleAsync("zone-a", "auto", CancellationToken.None);
        Assert.Single(queue);
        Assert.Equal("driver-concurrent-join", queue[0].DriverId);
    }

    [Fact]
    public async Task AddAsync_StillRejectsDuplicateActiveMembership()
    {
        var store = new InMemoryQueueStore();
        var entry = Entry("driver-a", "zone-a", "auto", DateTimeOffset.UtcNow);
        await store.AddAsync(entry, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddAsync(entry, CancellationToken.None));
    }

    [Fact]
    public async Task MoveToBackAsync_PreservesFifoOrderAndMovesDriverBehindExistingMembers()
    {
        var store = new InMemoryQueueStore();
        var start = DateTimeOffset.Parse("2026-11-05T00:00:00Z");
        await store.AddAsync(Entry("driver-a", "zone-a", "auto", start), CancellationToken.None);
        await store.AddAsync(Entry("driver-b", "zone-a", "auto", start.AddMinutes(1)), CancellationToken.None);
        await store.AddAsync(Entry("driver-c", "zone-a", "auto", start.AddMinutes(2)), CancellationToken.None);

        var moved = await store.MoveToBackAsync("driver-a", "zone-a", "auto", CancellationToken.None);

        Assert.True(moved);
        var queue = await store.GetEligibleAsync("zone-a", "auto", CancellationToken.None);
        Assert.Equal(new[] { "driver-b", "driver-c", "driver-a" }, queue.Select(entry => entry.DriverId));
    }

    [Fact]
    public async Task MoveToBackAsync_ReturnsFalse_WhenDriverIsNotInQueue()
    {
        var store = new InMemoryQueueStore();

        var moved = await store.MoveToBackAsync("missing-driver", "zone-a", "auto", CancellationToken.None);

        Assert.False(moved);
    }

    private static DriverQueueEntry Entry(
        string driverId, string zoneId, string serviceClass, DateTimeOffset joinedAt) =>
        new(driverId, zoneId, serviceClass, joinedAt, 0, true);

    [Fact]
    public async Task MoveToBackOnceAsync_DoesNotRotateTwiceForTheSameOffer()
    {
        var store = new InMemoryQueueStore();
        var start = DateTimeOffset.Parse("2026-11-05T00:00:00Z");
        await store.AddAsync(Entry("driver-a", "zone-a", "auto", start), CancellationToken.None);
        await store.AddAsync(Entry("driver-b", "zone-a", "auto", start.AddMinutes(1)), CancellationToken.None);
        await store.AddAsync(Entry("driver-c", "zone-a", "auto", start.AddMinutes(2)), CancellationToken.None);

        Assert.True(await store.MoveToBackOnceAsync("driver-a", "zone-a", "auto", "offer-1", CancellationToken.None));
        var afterFirstRotation = await store.GetEligibleAsync("zone-a", "auto", CancellationToken.None);
        Assert.Equal(new[] { "driver-b", "driver-c", "driver-a" }, afterFirstRotation.Select(entry => entry.DriverId));

        Assert.True(await store.MoveToBackOnceAsync("driver-a", "zone-a", "auto", "offer-1", CancellationToken.None));
        var afterRetry = await store.GetEligibleAsync("zone-a", "auto", CancellationToken.None);

        Assert.Equal(afterFirstRotation.Select(entry => entry.DriverId), afterRetry.Select(entry => entry.DriverId));
    }

    [Fact]
    public async Task MoveToBackOnceAsync_AllowsANewOfferToRotateTheDriverAgain()
    {
        var store = new InMemoryQueueStore();
        var start = DateTimeOffset.Parse("2026-11-05T00:00:00Z");
        await store.AddAsync(Entry("driver-a", "zone-a", "auto", start), CancellationToken.None);
        await store.AddAsync(Entry("driver-b", "zone-a", "auto", start.AddMinutes(1)), CancellationToken.None);
        await store.AddAsync(Entry("driver-c", "zone-a", "auto", start.AddMinutes(2)), CancellationToken.None);

        Assert.True(await store.MoveToBackOnceAsync("driver-a", "zone-a", "auto", "offer-1", CancellationToken.None));
        Assert.True(await store.MoveToBackAsync("driver-b", "zone-a", "auto", CancellationToken.None));
        Assert.True(await store.MoveToBackOnceAsync("driver-a", "zone-a", "auto", "offer-2", CancellationToken.None));

        var queue = await store.GetEligibleAsync("zone-a", "auto", CancellationToken.None);
        Assert.Equal(new[] { "driver-c", "driver-b", "driver-a" }, queue.Select(entry => entry.DriverId));
    }

    [Fact]
    public async Task MoveToBackOnceAsync_ReturnsFalse_WhenDriverIsNotInQueue()
    {
        var store = new InMemoryQueueStore();
        var moved = await store.MoveToBackOnceAsync("missing-driver", "zone-a", "auto", "offer-1", CancellationToken.None);
        Assert.False(moved);
    }

    [Fact]
    public async Task MoveToBackOnceAsync_RejectsAnEmptyOfferId()
    {
        var store = new InMemoryQueueStore();
        await store.AddAsync(Entry("driver-a", "zone-a", "auto", DateTimeOffset.UtcNow), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.MoveToBackOnceAsync("driver-a", "zone-a", "auto", " ", CancellationToken.None));
    }
}
