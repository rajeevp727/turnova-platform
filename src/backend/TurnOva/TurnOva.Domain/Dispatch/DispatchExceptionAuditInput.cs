namespace TurnOva.Domain.Dispatch;

/// <summary>
/// Validates the safe, Cab-only metadata accepted for a dispatch-exception audit record.
/// Keep this contract shared by application and persistence boundaries so direct adapter
/// calls cannot bypass the application-service checks.
/// </summary>
public static class DispatchExceptionAuditInput
{
    public static void Validate(
        string actorId,
        string zoneId,
        string serviceClass,
        string traceId,
        string reasonCategory)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Actor ID is required.", nameof(actorId));
        if (string.IsNullOrWhiteSpace(zoneId))
            throw new ArgumentException("Zone ID is required.", nameof(zoneId));
        if (string.IsNullOrWhiteSpace(serviceClass))
            throw new ArgumentException("Service class is required.", nameof(serviceClass));
        if (string.IsNullOrWhiteSpace(traceId) ||
            traceId.Length > 128 ||
            traceId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not ':' and not '-' and not '_'))
            throw new ArgumentException("A valid server trace identifier is required.", nameof(traceId));

        if (!string.Equals(serviceClass, "cab", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Dispatch exception auditing is limited to the Cab MVP.", nameof(serviceClass));

        if (reasonCategory is not QueueAuditReasonCategories.QueueReadFailed and
            not QueueAuditReasonCategories.DriverReservationFailed and
            not QueueAuditReasonCategories.DriverReservationPromotionRecovered)
            throw new ArgumentException("The dispatch exception category is not allow-listed.", nameof(reasonCategory));
    }
}
