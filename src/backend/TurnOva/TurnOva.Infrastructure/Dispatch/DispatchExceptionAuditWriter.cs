using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

/// <summary>
/// Writes a trace-scoped dispatch exception once. A create conflict is success only after
/// a point read proves that the canonical record contains the same safe audit metadata.
/// </summary>
public sealed class DispatchExceptionAuditWriter(IQueueAuditEventStore store)
{
    public async Task RecordAsync(
        string actorId,
        string zoneId,
        string serviceClass,
        string traceId,
        string reasonCategory,
        CancellationToken cancellationToken)
    {
        var partitionKey = $"{zoneId.Trim().ToLowerInvariant()}|{serviceClass.Trim().ToLowerInvariant()}";
        var auditEvent = new QueueAuditEvent(
            $"dispatch-exception-{traceId}",
            partitionKey,
            actorId,
            QueueAuditEventTypes.DispatchException,
            DateTimeOffset.UtcNow,
            TraceId: traceId,
            ReasonCategory: reasonCategory);

        if (await store.TryCreateAsync(auditEvent, partitionKey, cancellationToken))
            return;

        var existing = await store.ReadAsync(auditEvent.EventId, partitionKey, cancellationToken);
        if (IsSameAudit(existing, auditEvent))
            return;

        // Do not silently treat a reused trace ID or a conflicting/missing canonical item as
        // an idempotent retry. The caller fails closed and the conflict remains observable.
        throw new InvalidOperationException(
            "A dispatch exception audit event ID conflict could not be verified against matching metadata.");
    }

    private static bool IsSameAudit(QueueAuditEvent? existing, QueueAuditEvent expected) =>
        existing is not null &&
        existing.EventId == expected.EventId &&
        existing.EventType == QueueAuditEventTypes.DispatchException &&
        existing.ZoneServiceKey == expected.ZoneServiceKey &&
        existing.DriverId == expected.DriverId &&
        existing.TraceId == expected.TraceId &&
        existing.ReasonCategory == expected.ReasonCategory &&
        existing.OfferId is null;
}
