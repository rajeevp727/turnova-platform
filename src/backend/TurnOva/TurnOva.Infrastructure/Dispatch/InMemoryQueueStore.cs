using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

// Development adapter only. Replace with a Cosmos DB implementation before multi-instance deployment.
public sealed class InMemoryQueueStore : IQueueStore
{
    private readonly ConcurrentDictionary<string, FifoQueue> _queues = new();
    private long _sequence;
    private readonly ConcurrentDictionary<string, string> _rotations = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<QueueAuditEvent> _auditEvents = new();
    private readonly ConcurrentDictionary<string, QueueAuditEvent> _exceptionAuditEvents = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<DriverQueueEntry>> GetEligibleAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(zoneId, serviceClass);
        if (!_queues.TryGetValue(key, out var queue))
            return Task.FromResult<IReadOnlyList<DriverQueueEntry>>(Array.Empty<DriverQueueEntry>());

        lock (queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DriverQueueEntry>>(queue.GetEntries(cancellationToken));
        }
    }

    public Task AddAsync(DriverQueueEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DriverQueueEntry.ValidateForJoin(entry);
        var key = Key(entry.ZoneId, entry.ServiceClass);
        var queue = _queues.GetOrAdd(key, _ => new FifoQueue());
        lock (queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            queue.Enqueue(entry with { Sequence = Interlocked.Increment(ref _sequence) }, cancellationToken);
            RecordAudit(key, entry.DriverId, QueueAuditEventTypes.Joined);
        }
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(zoneId, serviceClass);
        if (!_queues.TryGetValue(key, out var queue))
            return Task.FromResult(false);

        lock (queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var removed = queue.Remove(driverId, cancellationToken);
            if (removed)
                RecordAudit(key, driverId, QueueAuditEventTypes.Left);
            return Task.FromResult(removed);
        }
    }

    public Task<bool> MoveToBackAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(zoneId, serviceClass);
        if (!_queues.TryGetValue(key, out var queue))
            return Task.FromResult(false);

        lock (queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.FindEligible(driverId, cancellationToken);
            if (current is null) return Task.FromResult(false);

            // Once the mutation starts, finish the queue/audit update as one synchronous
            // critical section even if cancellation arrives during the non-awaiting writes.
            cancellationToken.ThrowIfCancellationRequested();
            queue.Remove(driverId);
            queue.Enqueue(current with
            {
                JoinedAt = DateTimeOffset.UtcNow,
                Sequence = Interlocked.Increment(ref _sequence)
            });
            RecordAudit(key, driverId, QueueAuditEventTypes.MovedToBack);
            return Task.FromResult(true);
        }
    }

    public Task<bool> MoveToBackOnceAsync(string driverId, string zoneId, string serviceClass, string offerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(offerId)) throw new ArgumentException("Offer ID is required.", nameof(offerId));
        var key = Key(zoneId, serviceClass);
        if (!_queues.TryGetValue(key, out var queue)) return Task.FromResult(false);
        lock (queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.FindEligible(driverId, cancellationToken);
            if (current is null) return Task.FromResult(false);
            var rotationKey = $"{key}|{driverId}";
            if (_rotations.TryGetValue(rotationKey, out var lastOfferId) && lastOfferId == offerId)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(true);
            }

            // Keep the queue rotation, idempotency marker and audit fact together once
            // the synchronous mutation begins.
            cancellationToken.ThrowIfCancellationRequested();
            queue.Remove(driverId);
            queue.Enqueue(current with { JoinedAt = DateTimeOffset.UtcNow, Sequence = Interlocked.Increment(ref _sequence) });
            _rotations[rotationKey] = offerId;
            RecordAudit(key, driverId, QueueAuditEventTypes.RotatedToBack, offerId);
            return Task.FromResult(true);
        }
    }

    public Task RecordDispatchExceptionAsync(
        string actorId,
        string zoneId,
        string serviceClass,
        string traceId,
        string reasonCategory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(zoneId, serviceClass);
        var eventId = $"dispatch-exception-{traceId}";
        var deduplicationKey = $"{key}|{traceId}";
        var audit = new QueueAuditEvent(
            eventId,
            key,
            actorId,
            QueueAuditEventTypes.DispatchException,
            DateTimeOffset.UtcNow,
            TraceId: traceId,
            ReasonCategory: reasonCategory);

        cancellationToken.ThrowIfCancellationRequested();
        if (_exceptionAuditEvents.TryAdd(deduplicationKey, audit))
        {
            // Complete the paired in-memory audit append after the deduplication marker
            // is committed; cancellation must not leave only half of the write visible.
            _auditEvents.Enqueue(audit);
        }
        else if (_exceptionAuditEvents.TryGetValue(deduplicationKey, out var existing) &&
                 (existing.DriverId != actorId || existing.ReasonCategory != reasonCategory))
        {
            throw new InvalidOperationException("A dispatch exception trace ID was reused with different audit metadata.");
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QueueAuditEvent>> GetAuditEventsAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(zoneId, serviceClass);
        var events = InMemoryQueueAuditSnapshots.GetForZoneService(_auditEvents, key, cancellationToken);
        return Task.FromResult(events);
    }

    private void RecordAudit(
        string zoneServiceKey,
        string driverId,
        string eventType,
        string? offerId = null,
        string? traceId = null,
        string? reasonCategory = null) =>
        _auditEvents.Enqueue(new QueueAuditEvent(
            Guid.NewGuid().ToString("N"),
            zoneServiceKey,
            driverId,
            eventType,
            DateTimeOffset.UtcNow,
            offerId,
            traceId,
            reasonCategory));

    private static string Key(string zoneId, string serviceClass) =>
        $"{zoneId.Trim().ToLowerInvariant()}|{serviceClass.Trim().ToLowerInvariant()}";
}
