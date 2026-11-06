using TurnOva.Domain.Dispatch;
namespace TurnOva.Application.Dispatch;
public interface IDriverRideReservationStore
{
    Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default);
    Task<bool> TryAcquireAsync(string driverId, string rideId, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken = default);
    Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default);
    Task<bool> IsReservedForAsync(string driverId, string rideId, CancellationToken cancellationToken = default);
    Task<bool> TryReleaseAsync(string driverId, string rideId, CancellationToken cancellationToken = default);
}
