using System.Net;
using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database;

/// <summary>
/// Validates Cosmos point-read and query results before application code trusts their
/// identity. Read-only operations also preserve caller cancellation when a provider
/// response completes concurrently with request cancellation.
/// </summary>
public static class CosmosReadResponseGuard
{
    public static async Task<T> AwaitWithCancellationPrecedenceAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await operation();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch
        {
            // A provider failure can race request cancellation. Preserve cancellation
            // semantics instead of leaking a stale Cosmos/provider error to the caller.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    public static T RequireResource<T>(
        T? resource,
        Func<T, bool> identityMatches,
        string resourceDescription)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(identityMatches);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceDescription);

        if (resource is null)
            throw new InvalidOperationException($"Cosmos returned an empty {resourceDescription} resource.");

        if (!identityMatches(resource))
            throw new InvalidOperationException($"Cosmos returned a mismatched {resourceDescription} resource.");

        return resource;
    }

    public static T ReadResource<T>(
        ItemResponse<T> response,
        Func<T, bool> identityMatches,
        string resourceDescription,
        CancellationToken cancellationToken,
        string? identityMismatchMessage = null,
        string? emptyResourceMessage = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(identityMatches);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceDescription);
        cancellationToken.ThrowIfCancellationRequested();

        T? resource;
        try
        {
            resource = response.Resource;
        }
        catch
        {
            // Cosmos may hydrate/deserialise a point-read resource after the provider task
            // has completed. Preserve request cancellation if hydration itself fails.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (resource is null)
                throw new InvalidOperationException(emptyResourceMessage ??
                    $"Cosmos returned an empty {resourceDescription} resource.");

            if (!identityMatches(resource))
                throw new InvalidOperationException(identityMismatchMessage ??
                    $"Cosmos returned a mismatched {resourceDescription} resource.");
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return resource;
    }

    public static async Task<T?> ReadItemAsync<T>(
        Container container,
        string id,
        PartitionKey partitionKey,
        Func<T, bool> identityMatches,
        string resourceDescription,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(identityMatches);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceDescription);
        cancellationToken.ThrowIfCancellationRequested();

        ItemResponse<T> response;
        try
        {
            response = await container.ReadItemAsync<T>(
                id,
                partitionKey,
                cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (response is null)
            throw new InvalidOperationException($"Cosmos returned an empty {resourceDescription} response.");

        return ReadResource(response, identityMatches, resourceDescription, cancellationToken);
    }

    public static async Task<IReadOnlyList<T>> ReadAllAsync<T>(
        FeedIterator<T> iterator,
        Func<T, bool> identityMatches,
        string resourceDescription,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(iterator);
        ArgumentNullException.ThrowIfNull(identityMatches);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceDescription);
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<T>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!iterator.HasMoreResults) break;

            FeedResponse<T> page;
            try
            {
                page = await iterator.ReadNextAsync(cancellationToken);
            }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (page is null)
                throw new InvalidOperationException($"Cosmos returned an empty {resourceDescription} query page.");

            foreach (var resource in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(RequireResource(resource, identityMatches, resourceDescription));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }
}
