using System.Text.Json;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class RiderActiveBookingReservationStoreTests
{
    [Fact]
    public async Task ConcurrentDifferentRideRequestsForOneRiderAllowOnlyOneReservation()
    {
        var store = new InMemoryRiderActiveBookingReservationStore();
        using var barrier = new Barrier(2);
        var now = DateTimeOffset.UtcNow;
        var requests = new[]
        {
            new RiderActiveBookingReservation("rider-concurrent", "RIDE-A", "HASH-A", now, now.AddMinutes(15)),
            new RiderActiveBookingReservation("rider-concurrent", "RIDE-B", "HASH-B", now, now.AddMinutes(15))
        };

        var results = await Task.WhenAll(requests.Select(request => Task.Factory.StartNew(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            return await store.TryAcquireAsync(request);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()));

        Assert.Single(results, result => result == RiderBookingReservationAcquireResult.Acquired);
        Assert.Single(results, result => result == RiderBookingReservationAcquireResult.Conflict);
        Assert.NotNull(await store.GetActiveAsync("rider-concurrent"));
        Assert.Single(store.GetAuditEvents("rider-concurrent"),
            item => item.EventType == RiderActiveBookingReservationAuditEventTypes.Acquired);
    }

    [Fact]
    public async Task SameRideAndRequestHashReplayIsIdempotentWithoutDuplicateAudit()
    {
        var store = new InMemoryRiderActiveBookingReservationStore();
        var reservation = CreateReservation("rider-replay", "RIDE-REPLAY", "HASH-REPLAY");

        Assert.Equal(RiderBookingReservationAcquireResult.Acquired,
            await store.TryAcquireAsync(reservation));
        Assert.Equal(RiderBookingReservationAcquireResult.AlreadyOwned,
            await store.TryAcquireAsync(reservation));

        var active = await store.GetActiveAsync(reservation.RiderId);
        Assert.Equal(reservation.RideId, active?.RideId);
        Assert.Single(store.GetAuditEvents(reservation.RiderId));
    }

    [Fact]
    public async Task DifferentRidersCanHoldIndependentReservations()
    {
        var store = new InMemoryRiderActiveBookingReservationStore();

        var first = await store.TryAcquireAsync(CreateReservation("rider-a", "RIDE-A", "HASH-A"));
        var second = await store.TryAcquireAsync(CreateReservation("rider-b", "RIDE-B", "HASH-B"));

        Assert.Equal(RiderBookingReservationAcquireResult.Acquired, first);
        Assert.Equal(RiderBookingReservationAcquireResult.Acquired, second);
        Assert.Equal("RIDE-A", (await store.GetActiveAsync("rider-a"))?.RideId);
        Assert.Equal("RIDE-B", (await store.GetActiveAsync("rider-b"))?.RideId);
    }

    [Fact]
    public async Task PromotionMakesReservationPermanentAndMatchingReleaseIsAudited()
    {
        var store = new InMemoryRiderActiveBookingReservationStore();
        var reservation = CreateReservation("rider-promote", "RIDE-PROMOTE", "HASH-PROMOTE");
        Assert.Equal(RiderBookingReservationAcquireResult.Acquired,
            await store.TryAcquireAsync(reservation));

        Assert.False(await store.TryReleaseAsync(reservation.RiderId, "RIDE-OTHER"));
        Assert.True(await store.TryMakePermanentAsync(reservation.RiderId, reservation.RideId));
        Assert.Null((await store.GetActiveAsync(reservation.RiderId))?.ExpiresAtUtc);
        Assert.True(await store.TryMakePermanentAsync(reservation.RiderId, reservation.RideId));

        Assert.True(await store.TryReleaseAsync(reservation.RiderId, reservation.RideId));
        Assert.Null(await store.GetActiveAsync(reservation.RiderId));
        Assert.False(await store.TryReleaseAsync(reservation.RiderId, reservation.RideId));

        var events = store.GetAuditEvents(reservation.RiderId);
        Assert.Equal(
            [
                RiderActiveBookingReservationAuditEventTypes.Acquired,
                RiderActiveBookingReservationAuditEventTypes.MadePermanent,
                RiderActiveBookingReservationAuditEventTypes.Released
            ],
            events.Select(item => item.EventType));
    }

    [Fact]
    public async Task StaleReleaseCannotDeleteANewerRideReservation()
    {
        var store = new InMemoryRiderActiveBookingReservationStore();
        Assert.Equal(RiderBookingReservationAcquireResult.Acquired,
            await store.TryAcquireAsync(CreateReservation("rider-stale-release", "RIDE-OLD", "HASH-OLD")));
        Assert.True(await store.TryReleaseAsync("rider-stale-release", "RIDE-OLD"));

        Assert.Equal(RiderBookingReservationAcquireResult.Acquired,
            await store.TryAcquireAsync(CreateReservation("rider-stale-release", "RIDE-NEW", "HASH-NEW")));
        Assert.False(await store.TryReleaseAsync("rider-stale-release", "RIDE-OLD"));
        Assert.Equal("RIDE-NEW", (await store.GetActiveAsync("rider-stale-release"))?.RideId);
    }

    [Fact]
    public async Task AuditEventsContainNoRouteLocationContactOrFareData()
    {
        var store = new InMemoryRiderActiveBookingReservationStore();
        var reservation = CreateReservation("rider-private-audit", "RIDE-PRIVATE", "HASH-PRIVATE");
        Assert.Equal(RiderBookingReservationAcquireResult.Acquired,
            await store.TryAcquireAsync(reservation,
                auditContext: new DriverRideReservationAuditContext(
                    "rider-private-audit", "Rider", DriverRideReservationAuditReasons.SelectedCabBooking)));

        var audit = JsonSerializer.Serialize(Assert.Single(store.GetAuditEvents(reservation.RiderId)));
        foreach (var forbidden in new[]
        {
            "pickup", "dropoff", "latitude", "longitude", "phone", "email",
            "token", "fare", "amount", "chat", "requestBody"
        })
            Assert.DoesNotContain(forbidden, audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpiredReservationIsNotStolenWithoutApiLifecycleReconciliation()
    {
        var store = new InMemoryRiderActiveBookingReservationStore();
        var expired = CreateReservation("rider-expired", "RIDE-EXPIRED", "HASH-EXPIRED") with
        {
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        Assert.Equal(RiderBookingReservationAcquireResult.Conflict, await store.TryAcquireAsync(expired));
        Assert.Null(await store.GetActiveAsync("rider-expired"));
    }

    private static RiderActiveBookingReservation CreateReservation(string riderId, string rideId, string requestHash)
    {
        var now = DateTimeOffset.UtcNow;
        return new RiderActiveBookingReservation(riderId, rideId, requestHash, now, now.AddMinutes(15));
    }
}
