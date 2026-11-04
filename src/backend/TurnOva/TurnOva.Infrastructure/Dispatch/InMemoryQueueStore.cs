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
        var entries = _queues.TryGetValue(key, out var queue)
            ? queue.Entries
            : Array.Empty<DriverQueueEntry>();
        return Task.FromResult<IReadOnlyList<DriverQueueEntry>>(entries);
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

    private static string Key(string zoneId, string serviceClass) => $"{zoneId}:{serviceClass}";
}