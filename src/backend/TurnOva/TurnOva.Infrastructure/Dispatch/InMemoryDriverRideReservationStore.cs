using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class InMemoryDriverRideReservationStore : IDriverRideReservationStore
{
    private readonly ConcurrentDictionary<string, DriverRideReservation> _items = new(StringComparer.Ordinal);

    public Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default) =>
        TryAcquireAsync(driverId, rideId, null, cancellationToken);

    public Task<bool> TryAcquireAsync(
        string driverId,
        string rideId,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reservation = new DriverRideReservation(driverId, rideId, DateTimeOffset.UtcNow, expiresAtUtc);
        while (true)
        {
            if (_items.TryAdd(driverId, reservation))
                return Task.FromResult(true);
            if (!_items.TryGetValue(driverId, out var current))
                continue;
            if (current.ExpiresAtUtc is null || current.ExpiresAtUtc > DateTimeOffset.UtcNow)
                return Task.FromResult(false);
            if (_items.TryUpdate(driverId, reservation, current))
                return Task.FromResult(true);
        }
    }

    public Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = _items.TryGetValue(driverId, out var current) &&
            (current.ExpiresAtUtc is null || current.ExpiresAtUtc > DateTimeOffset.UtcNow);
        return Task.FromResult(active);
    }

    public Task<bool> IsReservedForAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reserved = _items.TryGetValue(driverId, out var current) &&
            current.RideId == rideId &&
            (current.ExpiresAtUtc is null || current.ExpiresAtUtc > DateTimeOffset.UtcNow);
        return Task.FromResult(reserved);
    }

    public Task<bool> TryReleaseAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_items.TryGetValue(driverId, out var current) || current.RideId != rideId)
            return Task.FromResult(false);
        return Task.FromResult(((ICollection<KeyValuePair<string, DriverRideReservation>>)_items)
            .Remove(new(driverId, current)));
    }
}
