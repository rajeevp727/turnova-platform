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

    public Task<IReadOnlyList<DriverQueueEntry>> GetEligibleAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        var key = Key(zoneId, serviceClass);
        if (!_queues.TryGetValue(key, out var queue))
            return Task.FromResult<IReadOnlyList<DriverQueueEntry>>(Array.Empty<DriverQueueEntry>());

        lock (queue)
            return Task.FromResult<IReadOnlyList<DriverQueueEntry>>(queue.Entries);
    }

    public Task AddAsync(DriverQueueEntry entry, CancellationToken cancellationToken)
    {
        var key = Key(entry.ZoneId, entry.ServiceClass);
        var queue = _queues.GetOrAdd(key, _ => new FifoQueue());
        lock (queue)
        {
            queue.Enqueue(entry with { Sequence = Interlocked.Increment(ref _sequence) });
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
            var removed = queue.Remove(driverId);
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
            var current = queue.Entries.FirstOrDefault(entry => entry.DriverId == driverId);
            if (current is null) return Task.FromResult(false);
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
            var current = queue.Entries.FirstOrDefault(entry => entry.DriverId == driverId);
            if (current is null) return Task.FromResult(false);
            var rotationKey = $"{key}|{driverId}";
            if (_rotations.TryGetValue(rotationKey, out var lastOfferId) && lastOfferId == offerId)
                return Task.FromResult(true);
            queue.Remove(driverId);
            queue.Enqueue(current with { JoinedAt = DateTimeOffset.UtcNow, Sequence = Interlocked.Increment(ref _sequence) });
            _rotations[rotationKey] = offerId;
            RecordAudit(key, driverId, QueueAuditEventTypes.RotatedToBack, offerId);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<QueueAuditEvent>> GetAuditEventsAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(zoneId, serviceClass);
        IReadOnlyList<QueueAuditEvent> events = _auditEvents
            .Where(item => item.ZoneServiceKey == key)
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.EventId, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(events);
    }

    private void RecordAudit(string zoneServiceKey, string driverId, string eventType, string? offerId = null) =>
        _auditEvents.Enqueue(new QueueAuditEvent(
            Guid.NewGuid().ToString("N"), zoneServiceKey, driverId, eventType, DateTimeOffset.UtcNow, offerId));

    private static string Key(string zoneId, string serviceClass) =>
        $"{zoneId.Trim().ToLowerInvariant()}|{serviceClass.Trim().ToLowerInvariant()}";
}