namespace TurnOva.Domain.Dispatch;

public sealed record DriverQueueEntry(
    string DriverId,
    string ZoneId,
    string ServiceClass,
    DateTimeOffset JoinedAt,
    long Sequence,
    bool IsEligible)
{
    public static void ValidateForJoin(DriverQueueEntry? entry, string parameterName = "entry")
    {
        ArgumentNullException.ThrowIfNull(entry, parameterName);
        if (string.IsNullOrWhiteSpace(entry.DriverId) ||
            string.IsNullOrWhiteSpace(entry.ZoneId) ||
            string.IsNullOrWhiteSpace(entry.ServiceClass) ||
            entry.JoinedAt == default)
            throw new ArgumentException(
                "Driver, zone, service class and join timestamp are required.",
                parameterName);
    }
}

public sealed class FifoQueue
{
    private readonly List<DriverQueueEntry> _entries = [];

    public IReadOnlyList<DriverQueueEntry> Entries => GetEntries(CancellationToken.None);

    internal IReadOnlyList<DriverQueueEntry> GetEntries(CancellationToken cancellationToken) =>
        FifoQueueSnapshots.GetEligible(_entries, cancellationToken);

    internal DriverQueueEntry? FindEligible(string driverId, CancellationToken cancellationToken) =>
        FifoQueueSnapshots.FindEligible(_entries, driverId, cancellationToken);

    public void Enqueue(DriverQueueEntry entry) =>
        Enqueue(entry, CancellationToken.None);

    internal void Enqueue(DriverQueueEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DriverQueueEntry.ValidateForJoin(entry);

        foreach (var existing in _entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existing.DriverId == entry.DriverId &&
                existing.ZoneId == entry.ZoneId &&
                existing.ServiceClass == entry.ServiceClass)
                throw new InvalidOperationException("Driver is already in this queue.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _entries.Add(entry);
    }

    public DriverQueueEntry? PeekNext() => Entries.FirstOrDefault();

    public bool Remove(string driverId) =>
        Remove(driverId, CancellationToken.None);

    internal bool Remove(string driverId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var matchingIndexes = new List<int>();
        for (var index = 0; index < _entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_entries[index].DriverId == driverId)
                matchingIndexes.Add(index);
        }

        cancellationToken.ThrowIfCancellationRequested();
        for (var index = matchingIndexes.Count - 1; index >= 0; index--)
            _entries.RemoveAt(matchingIndexes[index]);

        return matchingIndexes.Count > 0;
    }
}

internal static class FifoQueueSnapshots
{
    public static IReadOnlyList<DriverQueueEntry> GetEligible(
        IEnumerable<DriverQueueEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        var eligible = new List<DriverQueueEntry>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsEligible)
                eligible.Add(entry);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sequenceComparer = Comparer<long>.Create((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return left.CompareTo(right);
        });
        var joinedAtComparer = Comparer<DateTimeOffset>.Create((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return left.CompareTo(right);
        });

        // OrderBy/ThenBy retain the original stable ordering for exact key ties.
        var result = eligible
            .OrderBy(entry => entry.Sequence, sequenceComparer)
            .ThenBy(entry => entry.JoinedAt, joinedAtComparer)
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public static DriverQueueEntry? FindEligible(
        IEnumerable<DriverQueueEntry> entries,
        string driverId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsEligible &&
                string.Equals(entry.DriverId, driverId, StringComparison.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return entry;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }
}
