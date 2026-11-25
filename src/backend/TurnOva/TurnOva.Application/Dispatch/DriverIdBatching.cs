namespace TurnOva.Application.Dispatch;

public static class DriverIdBatching
{
    public const int MaxIdsPerBatch = 100;

    public static IReadOnlyList<string[]> Chunk(IEnumerable<string> driverIds) =>
        Chunk(driverIds, CancellationToken.None);

    public static IReadOnlyList<string[]> Chunk(
        IEnumerable<string> driverIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(driverIds);
        cancellationToken.ThrowIfCancellationRequested();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var batches = new List<string[]>();
        var current = new List<string>(MaxIdsPerBatch);

        foreach (var driverId in driverIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(driverId) || !seen.Add(driverId))
                continue;

            current.Add(driverId);
            if (current.Count != MaxIdsPerBatch)
                continue;

            batches.Add(current.ToArray());
            current.Clear();
        }

        // Observe cancellation triggered by the final MoveNext that reports end-of-input.
        cancellationToken.ThrowIfCancellationRequested();
        if (current.Count > 0)
            batches.Add(current.ToArray());

        return batches;
    }
}
