using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

/// <summary>
/// Minimal persistence boundary for idempotent dispatch-exception audit records.
/// </summary>
public interface IQueueAuditEventStore
{
    /// <summary>Returns true when the item is created, or false when its deterministic ID already exists.</summary>
    Task<bool> TryCreateAsync(
        QueueAuditEvent auditEvent,
        string partitionKey,
        CancellationToken cancellationToken);

    /// <summary>Reads the canonical item by ID within the supplied logical partition.</summary>
    Task<QueueAuditEvent?> ReadAsync(
        string eventId,
        string partitionKey,
        CancellationToken cancellationToken);
}
