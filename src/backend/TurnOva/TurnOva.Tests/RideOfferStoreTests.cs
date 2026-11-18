using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class RideOfferStoreTests
{
    [Fact]
    public async Task TryCreateAsync_EnforcesOneAggregatePerRide()
    {
        var store = new InMemoryRideOfferStore();
        var state = CreateState("ride-1", "driver-1", DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.True(await store.TryCreateAsync(state));
        Assert.False(await store.TryCreateAsync(state with { CurrentOfferId = "offer-2" }));
    }

    [Fact]
    public async Task TryReplaceAsync_UsesETagToRejectStaleWriters()
    {
        var store = new InMemoryRideOfferStore();
        var state = CreateState("ride-1", "driver-1", DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.True(await store.TryCreateAsync(state));

        var loaded = await store.GetByRideAsync("ride-1");
        Assert.NotNull(loaded);
        var replacement = loaded! with { Status = RideOfferStatuses.Rejected };

        Assert.True(await store.TryReplaceAsync(replacement, loaded.ETag!));
        Assert.False(await store.TryReplaceAsync(replacement, loaded.ETag!));
    }

    [Fact]
    public async Task GetExpiredAsync_ReturnsOnlyPendingOffersWhoseDeadlineHasPassed()
    {
        var store = new InMemoryRideOfferStore();
        var now = DateTimeOffset.UtcNow;
        Assert.True(await store.TryCreateAsync(CreateState("expired", "driver-1", now.AddSeconds(-1))));
        Assert.True(await store.TryCreateAsync(CreateState("future", "driver-2", now.AddMinutes(1))));
        Assert.True(await store.TryCreateAsync(CreateState("resolved", "driver-3", now.AddSeconds(-1)) with
        {
            Status = RideOfferStatuses.Accepted
        }));

        var expired = await store.GetExpiredAsync(now, 10);

        Assert.Single(expired);
        Assert.Equal("expired", expired[0].RideId);
    }

    [Fact]
    public async Task GetPendingForDriverAsync_ReturnsCurrentPendingOffer()
    {
        var store = new InMemoryRideOfferStore();
        Assert.True(await store.TryCreateAsync(CreateState("ride-1", "driver-1", DateTimeOffset.UtcNow.AddMinutes(1))));

        var result = await store.GetPendingForDriverAsync("driver-1");

        Assert.NotNull(result);
        Assert.Equal("ride-1", result!.RideId);
    }

    private static RideOfferState CreateState(string rideId, string driverId, DateTimeOffset expiresAt)
    {
        var offeredAt = expiresAt.AddMinutes(-1);
        return new RideOfferState(
            rideId, "zone-1", "auto", $"offer-{rideId}", driverId, offeredAt, expiresAt,
            RideOfferStatuses.Pending,
            [new RideOfferAttempt($"offer-{rideId}", driverId, offeredAt, expiresAt, RideOfferStatuses.Pending)]);
    }
}
