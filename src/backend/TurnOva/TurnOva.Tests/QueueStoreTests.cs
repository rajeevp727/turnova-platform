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
    public async Task AddAsync_StillRejectsDuplicateActiveMembership()
    {
        var store = new InMemoryQueueStore();
        var entry = Entry("driver-a", "zone-a", "auto", DateTimeOffset.UtcNow);
        await store.AddAsync(entry, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddAsync(entry, CancellationToken.None));
    }

    private static DriverQueueEntry Entry(
        string driverId, string zoneId, string serviceClass, DateTimeOffset joinedAt) =>
        new(driverId, zoneId, serviceClass, joinedAt, 0, true);
}
