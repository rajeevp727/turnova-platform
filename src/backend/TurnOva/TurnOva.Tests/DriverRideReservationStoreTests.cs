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
    public async Task TemporaryReservationExpiresAndCanBeReacquired()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireTemporaryAsync("driver-1", "ride-1", DateTimeOffset.UtcNow.AddMilliseconds(500)));
        Assert.True(await store.HasActiveAsync("driver-1"));
        Assert.Equal("ride-1", (await store.GetActiveAsync("driver-1"))?.RideId);
        await Task.Delay(700);
        Assert.Null(await store.GetActiveAsync("driver-1"));
        Assert.False(await store.HasActiveAsync("driver-1"));
        Assert.True(await store.TryAcquireAsync("driver-1", "ride-2"));
    }

    [Fact]
    public async Task TemporaryReservationCanBeMadePermanentOnlyForItsRide()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireTemporaryAsync("driver-1", "ride-1", DateTimeOffset.UtcNow.AddMilliseconds(500)));
        Assert.False(await store.TryMakePermanentAsync("driver-1", "ride-2"));
        Assert.True(await store.TryMakePermanentAsync("driver-1", "ride-1"));
        await Task.Delay(700);
        Assert.True(await store.HasActiveAsync("driver-1"));
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
}