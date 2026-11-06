using TurnOva.Infrastructure.Dispatch;
namespace TurnOva.Tests;
public sealed class DriverRideReservationStoreTests
{
    [Fact]
    public async Task ConcurrentReservationsForOneDriver_OnlyOneSucceeds()
    {
        var store = new InMemoryDriverRideReservationStore();
        var results = await Task.WhenAll(store.TryAcquireAsync("driver-1", "ride-1"), store.TryAcquireAsync("driver-1", "ride-2"));
        Assert.Single(results.Where(x => x));
    }
    [Fact]
    public async Task HasActiveReturnsWhetherDriverReservationExists()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.False(await store.HasActiveAsync("driver-1"));
        Assert.True(await store.TryAcquireAsync("driver-1", "ride-1"));
        Assert.True(await store.HasActiveAsync("driver-1"));
        Assert.True(await store.TryReleaseAsync("driver-1", "ride-1"));
        Assert.False(await store.HasActiveAsync("driver-1"));
    }

    [Fact]
    public async Task ReleaseOnlyRemovesMatchingRide()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireAsync("driver-1", "ride-1"));
        Assert.False(await store.TryReleaseAsync("driver-1", "ride-2"));
        Assert.False(await store.TryAcquireAsync("driver-1", "ride-2"));
        Assert.True(await store.TryReleaseAsync("driver-1", "ride-1"));
        Assert.True(await store.TryAcquireAsync("driver-1", "ride-2"));
    }
    [Fact]
    public async Task ExpiredReservationCanBeReplacedAndNoLongerBlocksDiscovery()
    {
        var store = new InMemoryDriverRideReservationStore();
        var expiredAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        Assert.True(await store.TryAcquireAsync("driver-1", "old-selection", expiredAt));
        Assert.False(await store.HasActiveAsync("driver-1"));
        Assert.False(await store.IsReservedForAsync("driver-1", "old-selection"));

        Assert.True(await store.TryAcquireAsync("driver-1", "new-selection"));
        Assert.True(await store.HasActiveAsync("driver-1"));
        Assert.True(await store.IsReservedForAsync("driver-1", "new-selection"));
        Assert.False(await store.TryReleaseAsync("driver-1", "old-selection"));
    }

    [Fact]
    public async Task ActiveReservationCannotBeReplacedByAnotherSelection()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireAsync("driver-1", "active-selection", DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.False(await store.TryAcquireAsync("driver-1", "competing-selection", DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.True(await store.IsReservedForAsync("driver-1", "active-selection"));
        Assert.False(await store.IsReservedForAsync("driver-1", "competing-selection"));
    }

}