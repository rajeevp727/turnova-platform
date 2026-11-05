using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

// Development adapter only. Replace with a Cosmos DB implementation before multi-instance deployment.
public sealed class InMemoryQueueStore : IQueueStore
{
    private readonly ConcurrentDictionary<string, FifoQueue> _queues = new();
    private long _sequence;

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
            return Task.FromResult(queue.Remove(driverId));
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
            return Task.FromResult(true);
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
            return Task.FromResult(true);
        }
    }

    private static string Key(string zoneId, string serviceClass) =>
        $"{zoneId.Trim().ToLowerInvariant()}|{serviceClass.Trim().ToLowerInvariant()}";
}