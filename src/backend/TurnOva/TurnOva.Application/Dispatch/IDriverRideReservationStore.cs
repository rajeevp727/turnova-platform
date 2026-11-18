using TurnOva.Domain.Dispatch;
namespace TurnOva.Application.Dispatch;
public interface IDriverRideReservationStore
{
    Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default, DriverRideReservationAuditContext? auditContext = null);
    Task<bool> TryAcquireTemporaryAsync(string driverId, string rideId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default, DriverRideReservationAuditContext? auditContext = null);
    Task<bool> TryMakePermanentAsync(string driverId, string rideId, CancellationToken cancellationToken = default, DriverRideReservationAuditContext? auditContext = null);
    Task<bool> TrySetExpiryAsync(string driverId, string rideId, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken = default, DriverRideReservationAuditContext? auditContext = null);
    Task<DriverRideReservation?> GetActiveAsync(string driverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads current reservation documents for a bounded set of Drivers in batches.
    /// Expired leases are returned unchanged; only the ride lifecycle may reconcile and release them.
    /// </summary>
    Task<IReadOnlyList<DriverRideReservation>> GetActiveForDriversAsync(
        IReadOnlyCollection<string> driverIds,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default);
    Task<bool> TryReleaseAsync(string driverId, string rideId, CancellationToken cancellationToken = default, DriverRideReservationAuditContext? auditContext = null);
}
