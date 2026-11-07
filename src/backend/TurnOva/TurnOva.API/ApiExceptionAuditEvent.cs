namespace TurnOva.API;

public sealed record ApiExceptionAuditEvent(
    string Id,
    string ActorReference,
    string ActorRole,
    string HttpMethod,
    string RouteTemplate,
    string ExceptionCategory,
    string ReasonCode,
    string TraceId,
    int StatusCode,
    DateTimeOffset OccurredAtUtc);
