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
        return Task.FromResult(_items.TryAdd(driverId, new(driverId, rideId, DateTimeOffset.UtcNow)));
    }
    public Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_items.ContainsKey(driverId));
    }

    public Task<bool> TryReleaseAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_items.TryGetValue(driverId, out var current) || current.RideId != rideId) return Task.FromResult(false);
        return Task.FromResult(((ICollection<KeyValuePair<string, DriverRideReservation>>)_items).Remove(new(driverId, current)));
    }
}