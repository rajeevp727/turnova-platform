namespace TurnOva.Application.Dispatch;

public static class DriverIdBatching
{
    public const int MaxIdsPerBatch = 100;

    public static IReadOnlyList<string[]> Chunk(IEnumerable<string> driverIds)
    {
        ArgumentNullException.ThrowIfNull(driverIds);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var batches = new List<string[]>();
        var current = new List<string>(MaxIdsPerBatch);

        foreach (var driverId in driverIds)
        {
            if (string.IsNullOrWhiteSpace(driverId) || !seen.Add(driverId))
                continue;

            current.Add(driverId);
            if (current.Count != MaxIdsPerBatch)
                continue;

            batches.Add(current.ToArray());
            current.Clear();
        }

        if (current.Count > 0)
            batches.Add(current.ToArray());

        return batches;
    }
}
