using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class InMemoryRiderActiveBookingReservationStore : IRiderActiveBookingReservationStore
{
    private sealed record ReservationState(
        RiderActiveBookingReservation? Reservation,
        IReadOnlyList<RiderActiveBookingReservationAuditEvent> AuditEvents);

    private readonly ConcurrentDictionary<string, ReservationState> _items =
        new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public InMemoryRiderActiveBookingReservationStore(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<RiderBookingReservationAcquireResult> TryAcquireAsync(
        RiderActiveBookingReservation reservation,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reservation.ExpiresAtUtc is { } expiry && expiry <= _timeProvider.GetUtcNow())
            return Task.FromResult(RiderBookingReservationAcquireResult.Conflict);

        while (true)
        {
            if (!_items.TryGetValue(reservation.RiderId, out var state))
            {
                var audit = RiderActiveBookingReservationAuditEvent.Create(
                    reservation, RiderActiveBookingReservationAuditEventTypes.Acquired,
                    auditContext, previousExpiresAtUtc: null, reservation.ExpiresAtUtc);
                if (_items.TryAdd(reservation.RiderId, new ReservationState(reservation, [audit])))
                    return Task.FromResult(RiderBookingReservationAcquireResult.Acquired);
                continue;
            }

            if (state.Reservation is { } current)
            {
                if (current.RideId == reservation.RideId &&
                    current.RequestHash == reservation.RequestHash &&
                    (current.ExpiresAtUtc is null || current.ExpiresAtUtc > _timeProvider.GetUtcNow()))
                    return Task.FromResult(RiderBookingReservationAcquireResult.AlreadyOwned);

                // Expiry is reconciled by the API against the authoritative ride state.
                // The store never steals a reservation based on its timestamp alone.
                return Task.FromResult(RiderBookingReservationAcquireResult.Conflict);
            }

            var created = RiderActiveBookingReservationAuditEvent.Create(
                reservation, RiderActiveBookingReservationAuditEventTypes.Acquired,
                auditContext, previousExpiresAtUtc: null, reservation.ExpiresAtUtc);
            var next = state with
            {
                Reservation = reservation,
                AuditEvents = [.. state.AuditEvents, created]
            };
            if (_items.TryUpdate(reservation.RiderId, next, state))
                return Task.FromResult(RiderBookingReservationAcquireResult.Acquired);
        }
    }

    public Task<RiderActiveBookingReservation?> GetActiveAsync(
        string riderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _items.TryGetValue(riderId, out var state) ? state.Reservation : null);
    }

    public Task<bool> TryMakePermanentAsync(
        string riderId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null,
        bool allowExpiredForAuthoritativeActiveRide = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (_items.TryGetValue(riderId, out var state))
        {
            var current = state.Reservation;
            if (current is null || current.RideId != rideId)
                return Task.FromResult(false);
            if (current.ExpiresAtUtc is { } expiry && expiry <= _timeProvider.GetUtcNow() &&
                !allowExpiredForAuthoritativeActiveRide)
                return Task.FromResult(false);
            if (current.ExpiresAtUtc is null)
                return Task.FromResult(true);

            var updated = current with { ExpiresAtUtc = null };
            var audit = RiderActiveBookingReservationAuditEvent.Create(
                updated, RiderActiveBookingReservationAuditEventTypes.MadePermanent,
                auditContext, current.ExpiresAtUtc, expiresAtUtc: null);
            var next = state with
            {
                Reservation = updated,
                AuditEvents = [.. state.AuditEvents, audit]
            };
            if (_items.TryUpdate(riderId, next, state))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> TryReleaseAsync(
        string riderId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (_items.TryGetValue(riderId, out var state))
        {
            var current = state.Reservation;
            if (current is null || current.RideId != rideId)
                return Task.FromResult(false);

            var audit = RiderActiveBookingReservationAuditEvent.Create(
                current, RiderActiveBookingReservationAuditEventTypes.Released,
                auditContext, current.ExpiresAtUtc, expiresAtUtc: null);
            var next = state with
            {
                Reservation = null,
                AuditEvents = [.. state.AuditEvents, audit]
            };
            if (_items.TryUpdate(riderId, next, state))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public IReadOnlyList<RiderActiveBookingReservationAuditEvent> GetAuditEvents(string riderId) =>
        _items.TryGetValue(riderId, out var state) ? state.AuditEvents.ToArray() : [];
}
