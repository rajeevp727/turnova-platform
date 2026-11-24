using System.Net;
using Microsoft.Azure.Cosmos;
using TurnOva.Infrastructure.Database;

namespace TurnOva.API;

public static class CosmosRideIdempotencyCleanup
{
    public static async Task<bool> TryDeleteExactAsync(
        Container container,
        string id,
        PartitionKey partitionKey,
        string riderId,
        string rideId,
        string requestHash,
        DateTimeOffset expectedCreatedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(riderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rideId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        cancellationToken.ThrowIfCancellationRequested();

        ItemResponse<RideIdempotencyRecord> response;
        try
        {
            response = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => container.ReadItemAsync<RideIdempotencyRecord>(
                    id, partitionKey, cancellationToken: cancellationToken),
                cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        var record = CosmosReadResponseGuard.ReadResponseValue(
            response, static itemResponse => itemResponse.Resource, cancellationToken);
        if (record is null ||
            !string.Equals(record.id, id, StringComparison.Ordinal) ||
            !string.Equals(record.riderId, riderId, StringComparison.Ordinal) ||
            !string.Equals(record.rideId, rideId, StringComparison.Ordinal) ||
            !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal) ||
            record.CreatedAtUtc != expectedCreatedAtUtc)
            return false;

        var etag = CosmosReadResponseGuard.ReadResponseEtag(
            response, static itemResponse => itemResponse.ETag, cancellationToken);
        if (string.IsNullOrWhiteSpace(etag))
            return false;

        try
        {
            await container.DeleteItemAsync<RideIdempotencyRecord>(
                id,
                partitionKey,
                new ItemRequestOptions { IfMatchEtag = etag },
                cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (
            ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            // The record disappeared or was replaced after the read; leave the newer
            // record untouched and let the caller retry through the normal idempotency path.
            return false;
        }
    }
}
