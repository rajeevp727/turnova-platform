using System.Text.Json;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class DriverRideReservationStoreTests
{
    [Fact]
    public async Task ConcurrentReservationsForOneDriver_OnlyOneSucceedsAndOneAcquireAuditIsWritten()
    {
        var store = new InMemoryDriverRideReservationStore();
        var results = await Task.WhenAll(
            store.TryAcquireAsync("driver-1", "ride-1"),
            store.TryAcquireAsync("driver-1", "ride-2"));

        Assert.Single(results, result => result);
        var active = await store.GetActiveAsync("driver-1");
        Assert.NotNull(active);
        var audit = Assert.Single(store.GetAuditEvents("driver-1"));
        Assert.Equal(DriverRideReservationAuditEventTypes.Acquired, audit.EventType);
        Assert.Equal(active!.RideId, audit.ReservationId);
    }

    [Fact]
    public async Task HasActiveReturnsWhetherDriverReservationExistsAndKeepsAuditAfterRelease()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.False(await store.HasActiveAsync("driver-1"));
        Assert.True(await store.TryAcquireAsync("driver-1", "ride-1"));
        Assert.True(await store.HasActiveAsync("driver-1"));
        Assert.True(await store.TryReleaseAsync("driver-1", "ride-1"));
        Assert.False(await store.HasActiveAsync("driver-1"));

        Assert.Collection(
            store.GetAuditEvents("driver-1"),
            item => Assert.Equal(DriverRideReservationAuditEventTypes.Acquired, item.EventType),
            item => Assert.Equal(DriverRideReservationAuditEventTypes.Released, item.EventType));
        Assert.Null(await store.GetActiveAsync("driver-1"));
    }

    [Fact]
    public async Task ReservationLifecycleAppendsOneSafeAuditFactPerCommittedChange()
    {
        var store = new InMemoryDriverRideReservationStore();
        var firstExpiry = DateTimeOffset.UtcNow.AddHours(1);
        var secondExpiry = firstExpiry.AddMinutes(10);
        var rider = new DriverRideReservationAuditContext(
            "rider-1", "Rider", DriverRideReservationAuditReasons.SelectedCabBooking);
        var driver = new DriverRideReservationAuditContext(
            "driver-1", "Driver", DriverRideReservationAuditReasons.AgreementConfirmed);
        var completed = new DriverRideReservationAuditContext(
            "driver-1", "Driver", DriverRideReservationAuditReasons.RideCompleted);
        var system = DriverRideReservationAuditContext.ForSystem(
            DriverRideReservationAuditReasons.ReservationExpiryRestored);

        Assert.True(await store.TryAcquireTemporaryAsync(
            "driver-1", "ride-1", firstExpiry, auditContext: rider));
        Assert.True(await store.TryAcquireTemporaryAsync(
            "driver-1", "ride-1", firstExpiry, auditContext: rider));
        Assert.False(await store.TryAcquireAsync("driver-1", "ride-2"));
        Assert.True(await store.TrySetExpiryAsync(
            "driver-1", "ride-1", secondExpiry, auditContext: system));
        Assert.True(await store.TrySetExpiryAsync(
            "driver-1", "ride-1", secondExpiry, auditContext: system));
        Assert.True(await store.TryMakePermanentAsync("driver-1", "ride-1", auditContext: driver));
        Assert.True(await store.TryMakePermanentAsync("driver-1", "ride-1", auditContext: driver));
        Assert.False(await store.TryReleaseAsync("driver-1", "ride-2"));
        Assert.True(await store.TryReleaseAsync("driver-1", "ride-1", auditContext: completed));
        Assert.False(await store.TryReleaseAsync("driver-1", "ride-1"));

        var events = store.GetAuditEvents("driver-1");
        Assert.Equal(4, events.Count);
        Assert.Collection(
            events,
            item =>
            {
                Assert.Equal(DriverRideReservationAuditEventTypes.Acquired, item.EventType);
                Assert.Equal("rider-1", item.ActorId);
                Assert.Equal("Rider", item.ActorRole);
                Assert.Equal(DriverRideReservationAuditReasons.SelectedCabBooking, item.ReasonCategory);
                Assert.Null(item.PreviousExpiresAtUtc);
                Assert.Equal(firstExpiry, item.ExpiresAtUtc);
            },
            item =>
            {
                Assert.Equal(DriverRideReservationAuditEventTypes.ExpiryChanged, item.EventType);
                Assert.Equal("system", item.ActorId);
                Assert.Equal("System", item.ActorRole);
                Assert.Equal(firstExpiry, item.PreviousExpiresAtUtc);
                Assert.Equal(secondExpiry, item.ExpiresAtUtc);
            },
            item =>
            {
                Assert.Equal(DriverRideReservationAuditEventTypes.MadePermanent, item.EventType);
                Assert.Equal("driver-1", item.ActorId);
                Assert.Equal("Driver", item.ActorRole);
                Assert.Equal(secondExpiry, item.PreviousExpiresAtUtc);
                Assert.Null(item.ExpiresAtUtc);
            },
            item =>
            {
                Assert.Equal(DriverRideReservationAuditEventTypes.Released, item.EventType);
                Assert.Equal("driver-1", item.ActorId);
                Assert.Equal("Driver", item.ActorRole);
                Assert.Equal(DriverRideReservationAuditReasons.RideCompleted, item.ReasonCategory);
            });

        var serialized = JsonSerializer.Serialize(events);
        Assert.DoesNotContain("pickup", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latitude", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fare", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.All(events, item => Assert.Equal("driver-ride-reservation-audit", item.DocumentType));
    }

    [Fact]
    public async Task ExpiredTemporaryReservationRemainsHeldUntilRideLifecycleReconciliation()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireTemporaryAsync(
            "driver-1", "ride-1", DateTimeOffset.UtcNow.AddMilliseconds(100)));
        await Task.Delay(200);

        Assert.Equal("ride-1", (await store.GetActiveAsync("driver-1"))?.RideId);
        Assert.True(await store.HasActiveAsync("driver-1"));
        Assert.False(await store.TryAcquireAsync("driver-1", "ride-2"));

        Assert.True(await store.TryReleaseAsync("driver-1", "ride-1"));
        Assert.True(await store.TryAcquireAsync("driver-1", "ride-2"));
        Assert.Equal(
            new[] { DriverRideReservationAuditEventTypes.Acquired, DriverRideReservationAuditEventTypes.Released,
                DriverRideReservationAuditEventTypes.Acquired },
            store.GetAuditEvents("driver-1").Select(item => item.EventType));
    }

    [Fact]
    public async Task TemporaryReservationCanBeMadePermanentOnlyForItsRide()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireTemporaryAsync(
            "driver-1", "ride-1", DateTimeOffset.UtcNow.AddHours(1)));
        Assert.False(await store.TryMakePermanentAsync("driver-1", "ride-2"));
        Assert.True(await store.TryMakePermanentAsync("driver-1", "ride-1"));
        Assert.Null((await store.GetActiveAsync("driver-1"))?.ExpiresAtUtc);
        Assert.Single(store.GetAuditEvents("driver-1"), item =>
            item.EventType == DriverRideReservationAuditEventTypes.MadePermanent);
    }

    [Fact]
    public async Task ReleaseOnlyRemovesMatchingRideAndDoesNotLosePriorAuditEvents()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireAsync("driver-1", "ride-1"));
        Assert.False(await store.TryReleaseAsync("driver-1", "ride-2"));
        Assert.False(await store.TryAcquireAsync("driver-1", "ride-2"));
        Assert.True(await store.TryReleaseAsync("driver-1", "ride-1"));
        Assert.True(await store.TryAcquireAsync("driver-1", "ride-2"));

        var events = store.GetAuditEvents("driver-1");
        Assert.Equal(3, events.Count);
        Assert.Equal(
            new[] { "ride-1", "ride-1", "ride-2" },
            events.Select(item => item.ReservationId));
    }

    [Fact]
    public async Task InvalidAuditContextIsReducedToAllowListedMetadata()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireAsync(
            "driver-1",
            "ride-1",
            auditContext: new DriverRideReservationAuditContext(
                "contact@example.invalid", "Administrator", "customer-supplied-free-text")));

        var audit = Assert.Single(store.GetAuditEvents("driver-1"));
        Assert.Equal("system", audit.ActorId);
        Assert.Equal("System", audit.ActorRole);
        Assert.Equal(DriverRideReservationAuditReasons.Other, audit.ReasonCategory);
    }
    [Fact]
    public async Task BatchReadReturnsOnlyRequestedReservationsAndRetainsExpiredLeaseUntilReconciliation()
    {
        var store = new InMemoryDriverRideReservationStore();
        Assert.True(await store.TryAcquireAsync("driver-a", "ride-a"));
        Assert.True(await store.TryAcquireTemporaryAsync(
            "driver-b", "ride-b", DateTimeOffset.UtcNow.AddMilliseconds(150)));
        await Task.Delay(250);

        var reservations = await store.GetActiveForDriversAsync(
            ["driver-a", "driver-b", "missing-driver", "driver-a"]);

        Assert.Equal(2, reservations.Count);
        Assert.Contains(reservations, reservation =>
            reservation.DriverId == "driver-a" && reservation.RideId == "ride-a");
        var expired = Assert.Single(reservations, reservation => reservation.DriverId == "driver-b");
        Assert.Equal("ride-b", expired.RideId);
        Assert.True(expired.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow);
    }

}
