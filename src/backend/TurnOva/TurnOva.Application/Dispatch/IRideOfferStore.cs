using TurnOva.Domain.Dispatch;

namespace TurnOva.Application.Dispatch;

public interface IRideOfferStore
{
    Task<RideOfferState?> GetByRideAsync(string rideId, CancellationToken cancellationToken = default);
    Task<RideOfferState?> GetPendingForDriverAsync(string driverId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RideOfferState>> GetExpiredAsync(DateTimeOffset nowUtc, int maxItems, CancellationToken cancellationToken = default);
    Task<bool> TryCreateAsync(RideOfferState state, CancellationToken cancellationToken = default);
    Task<bool> TryReplaceAsync(RideOfferState state, string expectedETag, CancellationToken cancellationToken = default);
}
