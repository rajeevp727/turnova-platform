using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

internal static class InMemoryQueueAuditSnapshots
{
    public static IReadOnlyList<QueueAuditEvent> GetForZoneService(
        IEnumerable<QueueAuditEvent> auditEvents,
        string zoneServiceKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvents);
        cancellationToken.ThrowIfCancellationRequested();

        var matching = new List<QueueAuditEvent>();
        foreach (var auditEvent in auditEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(auditEvent.ZoneServiceKey, zoneServiceKey, StringComparison.Ordinal))
                matching.Add(auditEvent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var occurredAtComparer = Comparer<DateTimeOffset>.Create((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return right.CompareTo(left);
        });
        var eventIdComparer = Comparer<string>.Create((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return StringComparer.Ordinal.Compare(right, left);
        });
        var result = matching
            .OrderBy(item => item.OccurredAtUtc, occurredAtComparer)
            .ThenBy(item => item.EventId, eventIdComparer)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
