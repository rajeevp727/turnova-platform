using System.Net;
using Microsoft.Azure.Cosmos;

namespace TurnOva.API;

public static class CosmosCreateConflictRead
{
    public static async Task<T?> ReadAfterCreateConflictAsync<T>(
        Container container,
        string id,
        PartitionKey partitionKey,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var response = await container.ReadItemAsync<T>(
                id, partitionKey, cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // A concurrent cleanup may remove the conflicting reservation between
            // CreateItem(409) and this point read. Treat only that race as absent;
            // other provider failures must remain visible to the caller.
            return null;
        }
    }
}
