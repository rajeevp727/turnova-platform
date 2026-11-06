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
            // The API must reconcile an expired lease against the ride before release.
            if (current.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
                return Task.FromResult(false);
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

    public Task<DriverRideReservation?> GetActiveAsync(string driverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Return expired leases too; the API reconciles the linked ride before release.
        return Task.FromResult(_items.TryGetValue(driverId, out var current) ? current : null);
    }

    public async Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default) =>
        await GetActiveAsync(driverId, cancellationToken) is not null;

    public Task<bool> TryReleaseAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_items.TryGetValue(driverId, out var current) || current.RideId != rideId) return Task.FromResult(false);
        return Task.FromResult(((ICollection<KeyValuePair<string, DriverRideReservation>>)_items).Remove(new(driverId, current)));
    }
}
