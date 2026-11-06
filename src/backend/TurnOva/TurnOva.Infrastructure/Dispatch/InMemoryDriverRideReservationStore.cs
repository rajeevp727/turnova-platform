using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
namespace TurnOva.Infrastructure.Dispatch;
public sealed class InMemoryDriverRideReservationStore : IDriverRideReservationStore
{
    private readonly ConcurrentDictionary<string, DriverRideReservation> _items = new(StringComparer.Ordinal);

    public Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TryAcquire(driverId, rideId, null));
    }

    public Task<bool> TryAcquireUntilAsync(string driverId, string rideId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresAtUtc <= DateTimeOffset.UtcNow) throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        return Task.FromResult(TryAcquire(driverId, rideId, expiresAtUtc));
    }

    private bool TryAcquire(string driverId, string rideId, DateTimeOffset? expiresAtUtc)
    {
        while (true)
        {
            if (_items.TryGetValue(driverId, out var current))
            {
                if (current.ExpiresAtUtc is null || current.ExpiresAtUtc > DateTimeOffset.UtcNow)
                    return false;
                if (!((ICollection<KeyValuePair<string, DriverRideReservation>>)_items).Remove(new(driverId, current)))
                    continue;
            }
            return _items.TryAdd(driverId, new(driverId, rideId, DateTimeOffset.UtcNow, expiresAtUtc));
        }
    }

    public Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (_items.TryGetValue(driverId, out var current))
        {
            if (current.ExpiresAtUtc is null || current.ExpiresAtUtc > DateTimeOffset.UtcNow)
                return Task.FromResult(true);
            if (((ICollection<KeyValuePair<string, DriverRideReservation>>)_items).Remove(new(driverId, current)))
                return Task.FromResult(false);
        }
        return Task.FromResult(false);
    }

    public Task<bool> TryReleaseAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_items.TryGetValue(driverId, out var current) || current.RideId != rideId) return Task.FromResult(false);
        return Task.FromResult(((ICollection<KeyValuePair<string, DriverRideReservation>>)_items).Remove(new(driverId, current)));
    }
}