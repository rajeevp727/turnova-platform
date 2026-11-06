using TurnOva.Domain.Dispatch;
namespace TurnOva.Application.Dispatch;
public interface IDriverRideReservationStore
{
    Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default);
    Task<bool> TryAcquireTemporaryAsync(string driverId, string rideId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default);
    Task<bool> TryMakePermanentAsync(string driverId, string rideId, CancellationToken cancellationToken = default);
    Task<bool> TrySetExpiryAsync(string driverId, string rideId, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken = default);
    Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default);
    Task<bool> TryReleaseAsync(string driverId, string rideId, CancellationToken cancellationToken = default);
}
