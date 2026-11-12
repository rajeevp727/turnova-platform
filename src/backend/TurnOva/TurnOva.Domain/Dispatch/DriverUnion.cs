namespace TurnOva.Domain.Dispatch;

public sealed record DriverUnion(
    string UnionId,
    string Name,
    string Code,
    string? Location,
    string? ManagerName,
    string? ManagerPhone,
    IReadOnlyList<string> ZoneIds,
    IReadOnlyList<string> ServiceClasses,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public string id => UnionId;
}

public sealed record DriverUnionAssignment(
    string DriverId,
    string UnionId,
    string UnionName,
    string Status,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string AssignmentReason,
    string? AuditEventId = null,
    string? AuditEventType = null,
    string? AuditActor = null,
    string? AuditPreviousUnionId = null,
    string? AuditPreviousUnionName = null,
    DateTimeOffset? AuditOccurredAtUtc = null)
{
    public string id => DriverId;
}

public sealed record DriverUnionAuditEvent(
    string EventId,
    string DriverId,
    string? PreviousUnionId,
    string? PreviousUnionName,
    string NewUnionId,
    string NewUnionName,
    string EventType,
    string Reason,
    string Actor,
    DateTimeOffset OccurredAtUtc)
{
    public string id => EventId;
}