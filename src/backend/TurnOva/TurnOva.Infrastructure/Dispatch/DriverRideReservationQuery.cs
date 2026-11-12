using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public static class DriverRideReservationQuery
{
    // The same container stores append-only audit facts. A reservation document's id
    // equals its driverId; audit event ids do not, so the predicate excludes audit facts.
    public const string Sql =
        "SELECT VALUE c FROM c WHERE ARRAY_CONTAINS(@driverIds, c.driverId) AND c.id = c.driverId";

    public static QueryDefinition Create(IReadOnlyCollection<string> driverIds)
    {
        ArgumentNullException.ThrowIfNull(driverIds);
        var ids = driverIds
            .Where(driverId => !string.IsNullOrWhiteSpace(driverId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length is 0 or > DriverIdBatching.MaxIdsPerBatch)
            throw new ArgumentOutOfRangeException(nameof(driverIds),
                $"A reservation query batch must contain between 1 and {DriverIdBatching.MaxIdsPerBatch} distinct Driver IDs.");

        return new QueryDefinition(Sql).WithParameter("@driverIds", ids);
    }
}
