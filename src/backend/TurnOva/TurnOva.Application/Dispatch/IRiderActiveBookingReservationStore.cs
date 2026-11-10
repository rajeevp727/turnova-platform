using TurnOva.Domain.Dispatch;

namespace TurnOva.Application.Dispatch;

public interface IRiderActiveBookingReservationStore
{
    Task<RiderBookingReservationAcquireResult> TryAcquireAsync(
        RiderActiveBookingReservation reservation,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null);

    Task<RiderActiveBookingReservation?> GetActiveAsync(
        string riderId,
        CancellationToken cancellationToken = default);

    Task<bool> TryMakePermanentAsync(
        string riderId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null);

    Task<bool> TryReleaseAsync(
        string riderId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null);

    IReadOnlyList<RiderActiveBookingReservationAuditEvent> GetAuditEvents(string riderId);
}
