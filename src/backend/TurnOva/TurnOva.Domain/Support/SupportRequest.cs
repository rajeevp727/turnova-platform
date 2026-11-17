namespace TurnOva.Domain.Support;

public sealed record SupportRequest(
    string Id,
    string ReporterId,
    string ReporterRole,
    string Subject,
    string Description,
    DateTimeOffset SubmittedAtUtc,
    int? Ttl = null);

public enum SupportRequestWriteStatus
{
    Created,
    Replayed,
    Conflict
}

public sealed record SupportRequestWriteResult(
    SupportRequestWriteStatus Status,
    SupportRequest Request);
