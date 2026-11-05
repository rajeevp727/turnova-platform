using TurnOva.Domain.Dispatch;

namespace TurnOva.Application.Dispatch;

public interface IRideOfferExpiryProcessor
{
    Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken = default);
}

public sealed class RideOfferExpiryProcessor(
    IRideOfferStore offers,
    IQueueStore queues,
    TimeProvider timeProvider) : IRideOfferExpiryProcessor
{
    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var due = await offers.GetExpiredAsync(timeProvider.GetUtcNow(), batchSize, cancellationToken);
        var resolved = 0;
        foreach (var offer in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!offer.IsExpiredAt(timeProvider.GetUtcNow()) || string.IsNullOrWhiteSpace(offer.ETag))
                continue;
            var rotated = await queues.MoveToBackOnceAsync(
                offer.DriverId, offer.ZoneId, offer.ServiceClass, offer.CurrentOfferId, cancellationToken);
            if (!rotated) continue;
            var now = timeProvider.GetUtcNow();
            var attempts = offer.Attempts.Select(attempt =>
                attempt.OfferId == offer.CurrentOfferId && attempt.Status == RideOfferStatuses.Pending
                    ? attempt with { Status = RideOfferStatuses.Expired, ResolvedAtUtc = now }
                    : attempt).ToArray();
            if (await offers.TryReplaceAsync(
                offer with { Status = RideOfferStatuses.Expired, Attempts = attempts },
                offer.ETag, cancellationToken))
                resolved++;
        }
        return resolved;
    }
}