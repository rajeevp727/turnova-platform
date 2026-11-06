using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
namespace TurnOva.Infrastructure.Dispatch;
public sealed class InMemoryDriverRideReservationStore : IDriverRideReservationStore
{
    private readonly ConcurrentDictionary<string, DriverRideReservation> _items = new(StringComparer.Ordinal);

    public Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default) =>
        TryAcquireCoreAsync(driverId, rideId, null, cancellationToken);

    public Task<bool> TryAcquireTemporaryAsync(string driverId, string rideId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default) =>
        TryAcquireCoreAsync(driverId, rideId, expiresAtUtc, cancellationToken);

    private Task<bool> TryAcquireCoreAsync(string driverId, string rideId, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow) return Task.FromResult(false);
        while (true)
        {
            if (!_items.TryGetValue(driverId, out var current))
                return Task.FromResult(_items.TryAdd(driverId, new(driverId, rideId, DateTimeOffset.UtcNow, expiresAtUtc)));
            if (current.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
            {
                if (((ICollection<KeyValuePair<string, DriverRideReservation>>)_items).Remove(new(driverId, current)))
                    continue;
                continue;
            }
            return Task.FromResult(current.RideId == rideId);
        }
    }

    public Task<bool> TryMakePermanentAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_items.TryGetValue(driverId, out var current) || current.RideId != rideId) return Task.FromResult(false);
        if (current.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow) return Task.FromResult(false);
        if (current.ExpiresAtUtc is null) return Task.FromResult(true);
        return Task.FromResult(_items.TryUpdate(driverId, current with { ExpiresAtUtc = null }, current));
    }

    public Task<bool> TrySetExpiryAsync(string driverId, string rideId, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow) return Task.FromResult(false);
        if (!_items.TryGetValue(driverId, out var current) || current.RideId != rideId) return Task.FromResult(false);
        return Task.FromResult(_items.TryUpdate(driverId, current with { ExpiresAtUtc = expiresAtUtc }, current));
    }

    public Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (_items.TryGetValue(driverId, out var current))
        {
            if (current.ExpiresAtUtc is null || current.ExpiresAtUtc > DateTimeOffset.UtcNow) return Task.FromResult(true);
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
