using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;

namespace TurnOva.API;

public static class UnionCreateIfAbsent
{
    public static async Task<bool> TryCreateAsync(
        Container? unionContainer,
        ConcurrentDictionary<string, DriverUnion> inMemoryUnions,
        DriverUnion union,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inMemoryUnions);
        ArgumentNullException.ThrowIfNull(union);
        cancellationToken.ThrowIfCancellationRequested();

        if (unionContainer is null)
            return inMemoryUnions.TryAdd(union.UnionId, union);

        try
        {
            _ = await CosmosReadResponseGuard.AwaitCommittedWriteWithCancellationPrecedenceAsync(
                () => unionContainer.CreateItemAsync(
                    union, new PartitionKey(union.UnionId), cancellationToken: cancellationToken),
                cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }
}
