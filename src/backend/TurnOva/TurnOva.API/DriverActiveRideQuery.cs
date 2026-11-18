using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.API;

public static class DriverActiveRideQuery
{
    public static string Sql { get; } =
        "SELECT VALUE { itemId: c.id, rideId: c.rideId, driverId: c.driverId, status: c.status, negotiationExpiresAtUtc: c.negotiationExpiresAtUtc } " +
        "FROM c WHERE ARRAY_CONTAINS(@driverIds, c.driverId) AND c.status IN (" +
        string.Join(", ", RideLifecycle.ActiveStatuses.Select(status => $"'{status}'")) + ")";

    public static QueryDefinition Create(IReadOnlyCollection<string> driverIds)
    {
        ArgumentNullException.ThrowIfNull(driverIds);
        var ids = driverIds
            .Where(driverId => !string.IsNullOrWhiteSpace(driverId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length is 0 or > DriverIdBatching.MaxIdsPerBatch)
            throw new ArgumentOutOfRangeException(nameof(driverIds),
                $"An active-ride query batch must contain between 1 and {DriverIdBatching.MaxIdsPerBatch} distinct Driver IDs.");

        return new QueryDefinition(Sql).WithParameter("@driverIds", ids);
    }
}

public sealed class ActiveDriverRideProjection
{
    public string ItemId { get; set; } = string.Empty;
    public string RideId { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? NegotiationExpiresAtUtc { get; set; }
}
