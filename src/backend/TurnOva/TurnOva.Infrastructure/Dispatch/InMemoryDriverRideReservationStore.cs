using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class InMemoryDriverRideReservationStore : IDriverRideReservationStore
{
    private sealed record ReservationState(
        DriverRideReservation? Reservation,
        IReadOnlyList<DriverRideReservationAuditEvent> AuditEvents);

    private readonly ConcurrentDictionary<string, ReservationState> _items = new(StringComparer.Ordinal);

    public Task<bool> TryAcquireAsync(
        string driverId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null) =>
        TryAcquireCoreAsync(driverId, rideId, null, cancellationToken, auditContext);

    public Task<bool> TryAcquireTemporaryAsync(
        string driverId,
        string rideId,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null) =>
        TryAcquireCoreAsync(driverId, rideId, expiresAtUtc, cancellationToken, auditContext);

    private Task<bool> TryAcquireCoreAsync(
        string driverId,
        string rideId,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken,
        DriverRideReservationAuditContext? auditContext)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow)
            return Task.FromResult(false);

        while (true)
        {
            var reservation = new DriverRideReservation(driverId, rideId, DateTimeOffset.UtcNow, expiresAtUtc);
            if (!_items.TryGetValue(driverId, out var currentState))
            {
                var auditEvent = DriverRideReservationAuditEvent.Create(
                    reservation, DriverRideReservationAuditEventTypes.Acquired, auditContext,
                    previousExpiresAtUtc: null, expiresAtUtc);
                var initial = new ReservationState(reservation, [auditEvent]);
                if (_items.TryAdd(driverId, initial))
                    return Task.FromResult(true);
                continue;
            }

            if (currentState.Reservation is { } current)
            {
                // Expiry is reconciled against the ride lifecycle by the API; the store never
                // steals a lease. An idempotent same-ride retry adds no duplicate audit event.
                if (current.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
                    return Task.FromResult(false);
                return Task.FromResult(current.RideId == rideId);
            }

            var created = DriverRideReservationAuditEvent.Create(
                reservation, DriverRideReservationAuditEventTypes.Acquired, auditContext,
                previousExpiresAtUtc: null, expiresAtUtc);
            var nextState = currentState with
            {
                Reservation = reservation,
                AuditEvents = [.. currentState.AuditEvents, created]
            };
            if (_items.TryUpdate(driverId, nextState, currentState))
                return Task.FromResult(true);
        }
    }

    public Task<bool> TryMakePermanentAsync(
        string driverId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (_items.TryGetValue(driverId, out var state))
        {
            var current = state.Reservation;
            if (current is null || current.RideId != rideId)
                return Task.FromResult(false);
            if (current.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
                return Task.FromResult(false);
            if (current.ExpiresAtUtc is null)
                return Task.FromResult(true);

            var updated = current with { ExpiresAtUtc = null };
            var auditEvent = DriverRideReservationAuditEvent.Create(
                updated, DriverRideReservationAuditEventTypes.MadePermanent, auditContext,
                current.ExpiresAtUtc, expiresAtUtc: null);
            var nextState = state with
            {
                Reservation = updated,
                AuditEvents = [.. state.AuditEvents, auditEvent]
            };
            if (_items.TryUpdate(driverId, nextState, state))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> TrySetExpiryAsync(
        string driverId,
        string rideId,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow)
            return Task.FromResult(false);

        while (_items.TryGetValue(driverId, out var state))
        {
            var current = state.Reservation;
            if (current is null || current.RideId != rideId)
                return Task.FromResult(false);
            if (current.ExpiresAtUtc == expiresAtUtc)
                return Task.FromResult(true);

            var updated = current with { ExpiresAtUtc = expiresAtUtc };
            var auditEvent = DriverRideReservationAuditEvent.Create(
                updated, DriverRideReservationAuditEventTypes.ExpiryChanged, auditContext,
                current.ExpiresAtUtc, expiresAtUtc);
            var nextState = state with
            {
                Reservation = updated,
                AuditEvents = [.. state.AuditEvents, auditEvent]
            };
            if (_items.TryUpdate(driverId, nextState, state))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<DriverRideReservation?> GetActiveAsync(
        string driverId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _items.TryGetValue(driverId, out var state) ? state.Reservation : null);
    }

    public Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default) =>
        HasActiveCoreAsync(driverId, cancellationToken);

    private async Task<bool> HasActiveCoreAsync(string driverId, CancellationToken cancellationToken) =>
        await GetActiveAsync(driverId, cancellationToken) is not null;

    public Task<bool> TryReleaseAsync(
        string driverId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (_items.TryGetValue(driverId, out var state))
        {
            var current = state.Reservation;
            if (current is null || current.RideId != rideId)
                return Task.FromResult(false);

            var auditEvent = DriverRideReservationAuditEvent.Create(
                current, DriverRideReservationAuditEventTypes.Released, auditContext,
                current.ExpiresAtUtc, expiresAtUtc: null);
            var nextState = state with
            {
                Reservation = null,
                AuditEvents = [.. state.AuditEvents, auditEvent]
            };
            if (_items.TryUpdate(driverId, nextState, state))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public IReadOnlyList<DriverRideReservationAuditEvent> GetAuditEvents(string driverId) =>
        _items.TryGetValue(driverId, out var state) ? state.AuditEvents.ToArray() : [];
}
