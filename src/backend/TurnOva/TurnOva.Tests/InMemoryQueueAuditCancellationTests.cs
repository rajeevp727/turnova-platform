using System.Collections;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class InMemoryQueueAuditCancellationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AuditSnapshotHonorsCancellationDuringOrAfterEnumeration(bool yieldSecond)
    {
        using var cancellation = new CancellationTokenSource();
        var now = DateTimeOffset.UtcNow;
        var first = CreateEvent("event-a", "zone-a|cab", now);
        var second = yieldSecond
            ? CreateEvent("event-b", "zone-a|cab", now.AddSeconds(-1))
            : null;
        var events = new CancellingQueueAuditEvents(cancellation, first, second);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            InMemoryQueueAuditSnapshots.GetForZoneService(events, "zone-a|cab", cancellation.Token));
    }

    private static QueueAuditEvent CreateEvent(string eventId, string zoneServiceKey, DateTimeOffset occurredAtUtc) =>
        new(eventId, zoneServiceKey, "driver-1", QueueAuditEventTypes.Joined, occurredAtUtc);

    private sealed class CancellingQueueAuditEvents(
        CancellationTokenSource cancellation,
        QueueAuditEvent first,
        QueueAuditEvent? second) : IEnumerable<QueueAuditEvent>
    {
        public IEnumerator<QueueAuditEvent> GetEnumerator()
        {
            yield return first;
            cancellation.Cancel();
            if (second is not null)
                yield return second;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
