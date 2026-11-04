namespace TurnOva.Domain.Dispatch;

public sealed record DriverQueueEntry(
    string DriverId,
    string ZoneId,
    string ServiceClass,
    DateTimeOffset JoinedAt,
    long Sequence,
    bool IsEligible);

public sealed class FifoQueue
{
    private readonly List<DriverQueueEntry> _entries = [];

    public IReadOnlyList<DriverQueueEntry> Entries =>
        _entries.Where(x => x.IsEligible)
            .OrderBy(x => x.Sequence)
            .ThenBy(x => x.JoinedAt)
            .ToArray();

    public void Enqueue(DriverQueueEntry entry)
    {
        if (_entries.Any(x => x.DriverId == entry.DriverId && x.ZoneId == entry.ZoneId && x.ServiceClass == entry.ServiceClass))
            throw new InvalidOperationException("Driver is already in this queue.");

        _entries.Add(entry);
    }

    public DriverQueueEntry? PeekNext() => Entries.FirstOrDefault();

    public bool Remove(string driverId) =>
        _entries.RemoveAll(x => x.DriverId == driverId) > 0;
}