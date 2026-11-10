using System.Text.Json.Serialization;

namespace TurnOva.Domain.Dispatch;

/// <summary>
/// One active selected-driver booking per Rider. Stored in the existing RideIdempotency
/// container's /riderId partition; it is not a payment, fare or dispatch record.
/// </summary>
public sealed record RiderActiveBookingReservation(
    string RiderId,
    string RideId,
    string RequestHash,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset? ExpiresAtUtc)
{
    public const string DocumentId = "active-selected-booking";

    [JsonPropertyName("id")]
    public string Id => DocumentId;
}

public enum RiderBookingReservationAcquireResult
{
    Acquired,
    AlreadyOwned,
    Conflict
}

public static class RiderActiveBookingReservationAuditEventTypes
{
    public const string Acquired = "RiderBookingReservationAcquired";
    public const string MadePermanent = "RiderBookingReservationMadePermanent";
    public const string Released = "RiderBookingReservationReleased";
}

/// <summary>
/// Append-only, privacy-minimised Rider reservation audit fact. Never add route,
/// precise location, contact details, tokens, fare or chat content.
/// </summary>
public sealed record RiderActiveBookingReservationAuditEvent(
    string EventId,
    string RiderId,
    string ReservationId,
    string RideId,
    string EventType,
    string ActorId,
    string ActorRole,
    string ReasonCategory,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset? PreviousExpiresAtUtc = null,
    DateTimeOffset? ExpiresAtUtc = null)
{
    [JsonPropertyName("id")]
    public string Id => EventId;

    public string DocumentType => "rider-active-booking-reservation-audit";

    public static RiderActiveBookingReservationAuditEvent Create(
        RiderActiveBookingReservation reservation,
        string eventType,
        DriverRideReservationAuditContext? context,
        DateTimeOffset? previousExpiresAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        var supplied = context ?? DriverRideReservationAuditContext.ForSystem();
        var actorId = supplied.ActorId?.Trim();
        var invalidActor = string.IsNullOrWhiteSpace(actorId) ||
            actorId.Length > 128 ||
            actorId.Any(char.IsControl) ||
            actorId.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':')) ||
            (actorId.Length >= 8 && actorId.All(char.IsDigit));
        if (invalidActor)
            actorId = "system";

        var actorRole = supplied.ActorRole is "Rider" or "Driver" or "UnionManager" or "Ops" or "System"
            ? supplied.ActorRole
            : "System";
        var reason = DriverRideReservationAuditReasons.IsAllowed(supplied.ReasonCategory)
            ? supplied.ReasonCategory
            : DriverRideReservationAuditReasons.Other;

        return new RiderActiveBookingReservationAuditEvent(
            $"rider-booking-reservation-audit-{Guid.NewGuid():N}",
            reservation.RiderId,
            RiderActiveBookingReservation.DocumentId,
            reservation.RideId,
            eventType,
            actorId!,
            actorRole,
            reason,
            DateTimeOffset.UtcNow,
            previousExpiresAtUtc,
            expiresAtUtc);
    }
}
