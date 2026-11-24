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
        where T : class =>
        ReadResponseResource(
            response,
            static itemResponse => itemResponse.Resource,
            identityMatches,
            resourceDescription,
            cancellationToken,
            identityMismatchMessage,
            emptyResourceMessage);

    public static TResource ReadResponseResource<TResponse, TResource>(
        TResponse response,
        Func<TResponse, TResource?> resourceAccessor,
        Func<TResource, bool> identityMatches,
        string resourceDescription,
        CancellationToken cancellationToken,
        string? identityMismatchMessage = null,
        string? emptyResourceMessage = null)
        where TResponse : class
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(resourceAccessor);
        ArgumentNullException.ThrowIfNull(identityMatches);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceDescription);
        cancellationToken.ThrowIfCancellationRequested();

        TResource? resource;
        try
        {
            resource = resourceAccessor(response);
        }
        catch
        {
            // SDK response properties can hydrate lazily after the provider task completes.
            // Preserve caller cancellation if property hydration itself fails.
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

    public static TValue ReadResponseValue<TResponse, TValue>(
        TResponse response,
        Func<TResponse, TValue> valueAccessor,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(valueAccessor);
        cancellationToken.ThrowIfCancellationRequested();

        TValue value;
        try
        {
            // Cosmos SDK response properties can hydrate lazily after the provider task
            // completes. Preserve caller cancellation even when the getter itself fails.
            value = valueAccessor(response);
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    public static string ReadResponseEtag<TResponse>(
        TResponse response,
        Func<TResponse, string> etagAccessor,
        CancellationToken cancellationToken)
        where TResponse : class =>
        ReadResponseValue(response, etagAccessor, cancellationToken);

    public static bool ReadHasMoreResults<T>(
        FeedIterator<T> iterator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(iterator);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var hasMoreResults = iterator.HasMoreResults;
            cancellationToken.ThrowIfCancellationRequested();
            return hasMoreResults;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    public static IReadOnlyList<T> ReadPageItems<T>(
        FeedResponse<T> page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<T>();
        IEnumerator<T>? enumerator = null;
        try
        {
            enumerator = ((IEnumerable<T>)page).GetEnumerator();
            cancellationToken.ThrowIfCancellationRequested();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool hasNext;
                try
                {
                    hasNext = enumerator.MoveNext();
                }
                catch
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!hasNext)
                    break;

                T item;
                try
                {
                    item = enumerator.Current;
                }
                catch
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }

                cancellationToken.ThrowIfCancellationRequested();
                results.Add(item);
            }
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        finally
        {
            if (enumerator is not null)
            {
                try
                {
                    enumerator.Dispose();
                }
                catch
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results;
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
            if (!ReadHasMoreResults(iterator, cancellationToken)) break;

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

            foreach (var resource in ReadPageItems(page, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    results.Add(RequireResource(resource, identityMatches, resourceDescription));
                }
                catch
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }
}
