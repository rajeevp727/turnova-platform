using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

internal static class InMemoryRideOfferStoreSnapshots
{
    public static RideOfferState? GetPendingForDriver(
        IEnumerable<RideOfferState> states,
        string driverId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(states);
        cancellationToken.ThrowIfCancellationRequested();

        RideOfferState? pendingOffer = null;
        foreach (var state in states)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.IsPending || !string.Equals(state.DriverId, driverId, StringComparison.Ordinal))
                continue;

            if (pendingOffer is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Multiple pending ride offers exist for the driver.");
            }

            pendingOffer = state;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return pendingOffer;
    }

    public static IReadOnlyList<RideOfferState> GetExpired(
        IEnumerable<RideOfferState> states,
        DateTimeOffset nowUtc,
        int maxItems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(states);
        cancellationToken.ThrowIfCancellationRequested();
        if (maxItems < 1)
            throw new ArgumentOutOfRangeException(nameof(maxItems));

        var expired = new List<RideOfferState>();
        foreach (var state in states)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.IsExpiredAt(nowUtc))
                expired.Add(state);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var comparer = Comparer<DateTimeOffset>.Create((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return left.CompareTo(right);
        });
        var result = expired
            .OrderBy(state => state.ExpiresAtUtc, comparer)
            .Take(maxItems)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public static IReadOnlyList<RideOfferState> GetPendingNotifications(
        IEnumerable<RideOfferState> states,
        int maxItems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(states);
        cancellationToken.ThrowIfCancellationRequested();
        if (maxItems < 1)
            throw new ArgumentOutOfRangeException(nameof(maxItems));

        var pending = new List<RideOfferState>();
        foreach (var state in states)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.IsPending && string.Equals(state.NotificationStatus, "Pending", StringComparison.Ordinal))
                pending.Add(state);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var comparer = Comparer<DateTimeOffset>.Create((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return left.CompareTo(right);
        });
        var result = pending
            .OrderBy(state => state.OfferedAtUtc, comparer)
            .Take(maxItems)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
