using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class InMemoryRideOfferStore : IRideOfferStore
{
    private readonly ConcurrentDictionary<string, RideOfferState> _states = new(StringComparer.Ordinal);
    private long _version;

    public Task<RideOfferState?> GetByRideAsync(string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var found = _states.TryGetValue(rideId, out var state);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(found ? state : null);
    }

    public Task<RideOfferState?> GetPendingForDriverAsync(string driverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InMemoryRideOfferStoreSnapshots.GetPendingForDriver(
            _states.Values, driverId, cancellationToken));
    }

    public Task<IReadOnlyList<RideOfferState>> GetExpiredAsync(DateTimeOffset nowUtc, int maxItems, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxItems < 1) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var result = InMemoryRideOfferStoreSnapshots.GetExpired(
            _states.Values, nowUtc, maxItems, cancellationToken);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<RideOfferState>> GetPendingNotificationsAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxItems < 1) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var result = InMemoryRideOfferStoreSnapshots.GetPendingNotifications(
            _states.Values, maxItems, cancellationToken);
        return Task.FromResult(result);
    }

    public Task<bool> TryCreateAsync(RideOfferState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);
        var versioned = state with { ETag = NextETag() };
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_states.TryAdd(state.RideId, versioned));
    }

    public Task<bool> TryReplaceAsync(RideOfferState state, string expectedETag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);
        if (!_states.TryGetValue(state.RideId, out var current) || current.ETag != expectedETag)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
        var replacement = state with { ETag = NextETag() };
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_states.TryUpdate(state.RideId, replacement, current));
    }

    private string NextETag() => Interlocked.Increment(ref _version).ToString(System.Globalization.CultureInfo.InvariantCulture);
}