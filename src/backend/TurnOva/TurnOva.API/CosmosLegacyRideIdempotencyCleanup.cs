using System.Net;
using Microsoft.Azure.Cosmos;

namespace TurnOva.API;

public static class CosmosLegacyRideIdempotencyCleanup
{
    public static async Task<bool> TryDeleteExactAsync(
        Container container,
        string id,
        PartitionKey partitionKey,
        string riderId,
        string rideId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(riderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rideId);
        cancellationToken.ThrowIfCancellationRequested();

        ItemResponse<RideIdempotencyRecord> response;
        try
        {
            response = await container.ReadItemAsync<RideIdempotencyRecord>(
                id, partitionKey, cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        var record = response.Resource;
        if (record is null ||
            !string.Equals(record.id, id, StringComparison.Ordinal) ||
            !string.Equals(record.riderId, riderId, StringComparison.Ordinal) ||
            !string.Equals(record.rideId, rideId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(response.ETag))
            return false;

        try
        {
            await container.DeleteItemAsync<RideIdempotencyRecord>(
                id,
                partitionKey,
                new ItemRequestOptions { IfMatchEtag = response.ETag },
                cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (
            ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            // The exact legacy reservation disappeared or changed after the read.
            return false;
        }
    }
}
