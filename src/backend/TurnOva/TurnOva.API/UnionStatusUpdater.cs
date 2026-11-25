using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;

namespace TurnOva.API;

public enum UnionStatusUpdateOutcome
{
    Updated,
    NotFound,
    Conflict
}

public sealed record UnionStatusUpdateResult(
    UnionStatusUpdateOutcome Outcome,
    DriverUnion? Union);

public static class UnionStatusUpdater
{
    public static async Task<UnionStatusUpdateResult> TryUpdateStatusAsync(
        Container? unionContainer,
        ConcurrentDictionary<string, DriverUnion> inMemoryUnions,
        string unionId,
        string status,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inMemoryUnions);
        ArgumentException.ThrowIfNullOrWhiteSpace(unionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        cancellationToken.ThrowIfCancellationRequested();

        if (unionContainer is null)
        {
            if (!inMemoryUnions.TryGetValue(unionId, out var current))
                return new UnionStatusUpdateResult(UnionStatusUpdateOutcome.NotFound, null);

            var updated = current with { Status = status, UpdatedAtUtc = updatedAtUtc };
            return inMemoryUnions.TryUpdate(unionId, updated, current)
                ? new UnionStatusUpdateResult(UnionStatusUpdateOutcome.Updated, updated)
                : new UnionStatusUpdateResult(UnionStatusUpdateOutcome.Conflict, null);
        }

        ItemResponse<DriverUnion> currentResponse;
        try
        {
            currentResponse = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => unionContainer.ReadItemAsync<DriverUnion>(
                    unionId, new PartitionKey(unionId), cancellationToken: cancellationToken),
                cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new UnionStatusUpdateResult(UnionStatusUpdateOutcome.NotFound, null);
        }

        var currentUnion = CosmosReadResponseGuard.ReadResponseValue(
            currentResponse, static response => response.Resource, cancellationToken);
        if (currentUnion is null ||
            !string.Equals(currentUnion.UnionId, unionId, StringComparison.Ordinal))
        {
            return new UnionStatusUpdateResult(UnionStatusUpdateOutcome.Conflict, null);
        }

        var etag = CosmosReadResponseGuard.ReadResponseEtag(
            currentResponse, static response => response.ETag, cancellationToken);
        if (!CosmosEtagPolicy.HasObservedEtag(etag))
            return new UnionStatusUpdateResult(UnionStatusUpdateOutcome.Conflict, null);

        var updatedUnion = currentUnion with { Status = status, UpdatedAtUtc = updatedAtUtc };
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _ = await CosmosReadResponseGuard.AwaitCommittedWriteWithCancellationPrecedenceAsync(
                () => unionContainer.ReplaceItemAsync(
                    updatedUnion,
                    unionId,
                    new PartitionKey(unionId),
                    new ItemRequestOptions { IfMatchEtag = etag },
                    cancellationToken),
                cancellationToken);
            return new UnionStatusUpdateResult(UnionStatusUpdateOutcome.Updated, updatedUnion);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new UnionStatusUpdateResult(UnionStatusUpdateOutcome.NotFound, null);
        }
        catch (CosmosException ex) when (
            ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new UnionStatusUpdateResult(UnionStatusUpdateOutcome.Conflict, null);
        }
    }
}
