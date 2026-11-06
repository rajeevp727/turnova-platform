namespace TurnOva.Domain.Dispatch;

public static class QueueAuditEventTypes
{
    public const string Joined = "Joined";
    public const string Left = "Left";
    public const string MovedToBack = "MovedToBack";
    public const string RotatedToBack = "RotatedToBack";
}

public sealed record QueueAuditEvent(
    string EventId,
    string ZoneServiceKey,
    string DriverId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string? OfferId = null)
{
    public string Id => EventId;

    public string DocumentType => "queue-audit";
}
