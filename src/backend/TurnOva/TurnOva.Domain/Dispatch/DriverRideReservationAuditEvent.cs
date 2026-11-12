using System.Text.Json.Serialization;

namespace TurnOva.Domain.Dispatch;

public static class DriverRideReservationAuditEventTypes
{
    public const string Acquired = "ReservationAcquired";
    public const string MadePermanent = "ReservationMadePermanent";
    public const string ExpiryChanged = "ReservationExpiryChanged";
    public const string Released = "ReservationReleased";

    public static bool IsAllowed(string? eventType) => eventType is
        Acquired or MadePermanent or ExpiryChanged or Released;
}

public static class DriverRideReservationAuditReasons
{
    public const string InternalReconciliation = "InternalReconciliation";
    public const string DriverOperationLock = "DriverOperationLock";
    public const string DriverProfileReview = "DriverProfileReview";
    public const string UnionTransfer = "UnionTransfer";
    public const string AvailabilityChange = "AvailabilityChange";
    public const string DriverZoneExit = "DriverZoneExit";
    public const string SelectedCabBooking = "SelectedCabBooking";
    public const string BookingEligibilityChanged = "BookingEligibilityChanged";
    public const string AgreementConfirmed = "AgreementConfirmed";
    public const string ReservationExpiryRestored = "ReservationExpiryRestored";
    public const string LegacyRideAccept = "LegacyRideAccept";
    public const string RideAcceptConflict = "RideAcceptConflict";
    public const string DriverDeclined = "DriverDeclined";
    public const string RideCompleted = "RideCompleted";
    public const string RiderCancelled = "RiderCancelled";
    public const string Other = "Other";

    public static bool IsAllowed(string? value) => value is
        InternalReconciliation or DriverOperationLock or DriverProfileReview or UnionTransfer or
        AvailabilityChange or DriverZoneExit or SelectedCabBooking or BookingEligibilityChanged or
        AgreementConfirmed or ReservationExpiryRestored or LegacyRideAccept or RideAcceptConflict or
        DriverDeclined or RideCompleted or RiderCancelled or Other;
}

public sealed record DriverRideReservationAuditContext(
    string ActorId,
    string ActorRole,
    string ReasonCategory)
{
    public static DriverRideReservationAuditContext ForSystem(
        string reasonCategory = DriverRideReservationAuditReasons.InternalReconciliation) =>
        new("system", "System", reasonCategory);
}

public sealed record DriverRideReservationAuditEvent(
    string EventId,
    string DriverId,
    string ReservationId,
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

    public string DocumentType => "driver-ride-reservation-audit";

    public static DriverRideReservationAuditEvent Create(
        DriverRideReservation reservation,
        string eventType,
        DriverRideReservationAuditContext? context,
        DateTimeOffset? previousExpiresAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        if (!DriverRideReservationAuditEventTypes.IsAllowed(eventType))
            throw new ArgumentException(
                "Unsupported driver reservation audit event type.",
                nameof(eventType));

        var supplied = context ?? DriverRideReservationAuditContext.ForSystem();
        var actorId = supplied.ActorId?.Trim();
        var containsNonIdentifierCharacters = actorId?.Any(character =>
            !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':')) ?? true;
        var containsOnlyPhoneCharacters = actorId is not null && actorId.All(character =>
            char.IsDigit(character) || character is '-' or '_' or '.' or ':');
        var phoneDigitCount = actorId?.Count(char.IsDigit) ?? 0;
        var looksLikePhoneNumber = containsOnlyPhoneCharacters && phoneDigitCount >= 8;
        var actorIdIsInvalid = string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128 ||
            actorId.Any(char.IsControl) || containsNonIdentifierCharacters || looksLikePhoneNumber;
        if (actorIdIsInvalid)
            actorId = "system";

        var actorRole = !actorIdIsInvalid &&
            supplied.ActorRole is "Rider" or "Driver" or "UnionManager" or "Ops" or "System"
                ? supplied.ActorRole
                : "System";
        var reason = DriverRideReservationAuditReasons.IsAllowed(supplied.ReasonCategory)
            ? supplied.ReasonCategory
            : DriverRideReservationAuditReasons.Other;

        return new DriverRideReservationAuditEvent(
            $"driver-reservation-audit-{Guid.NewGuid():N}",
            reservation.DriverId,
            reservation.RideId,
            eventType,
            actorId,
            actorRole,
            reason,
            DateTimeOffset.UtcNow,
            previousExpiresAtUtc,
            expiresAtUtc);
    }
}
