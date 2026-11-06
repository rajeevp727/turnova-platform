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
    public async Task ExpiredNegotiationReservationCanBeReplacedButActiveRideReservationCannot()
    {
        var store = new InMemoryDriverRideReservationStore();
    [Fact]
    public async Task ExpiredNegotiationReservationIsReleasedAndOnlyMatchingBookingCanRelease()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireNegotiationAsync("driver-1", "booking-1", DateTimeOffset.UtcNow.AddSeconds(2)));
        Assert.False(await store.TryReleaseAsync("driver-1", "booking-2"));
        Assert.True(await store.TryReleaseAsync("driver-1", "booking-1"));
        Assert.False(await store.HasActiveAsync("driver-1"));

        Assert.True(await store.TryAcquireNegotiationAsync("driver-1", "booking-expired", DateTimeOffset.UtcNow.AddMilliseconds(-1)));
        Assert.False(await store.HasActiveAsync("driver-1"));
        Assert.True(await store.TryAcquireAsync("driver-1", "active-ride"));
        Assert.False(await store.TryAcquireNegotiationAsync("driver-1", "new-booking", DateTimeOffset.UtcNow.AddMinutes(1)));
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