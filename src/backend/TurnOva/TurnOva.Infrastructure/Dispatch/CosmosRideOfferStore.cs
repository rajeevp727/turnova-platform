using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class CosmosRideOfferStore(Container container) : IRideOfferStore
{
    public async Task<RideOfferState?> GetByRideAsync(string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var response = await container.ReadItemAsync<RideOfferState>(rideId, new PartitionKey(rideId), cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return RequireResource(response, rideId) with { ETag = response.ETag };
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    public async Task<RideOfferState?> GetPendingForDriverAsync(string driverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = new QueryDefinition("SELECT TOP 2 * FROM c WHERE c.driverId = @driverId AND c.status = @status")
            .WithParameter("@driverId", driverId).WithParameter("@status", RideOfferStatuses.Pending);
        using var iterator = container.GetItemQueryIterator<RideOfferState>(query);
        RideOfferState? pendingOffer = null;
        while (iterator.HasMoreResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await iterator.ReadNextAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = RequirePendingForDriverQueryItem(item, driverId);
                if (pendingOffer is not null)
                    throw new InvalidOperationException(
                        "Cosmos returned multiple pending ride offers for the driver.");

                pendingOffer = candidate;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return pendingOffer;
    }

    public async Task<IReadOnlyList<RideOfferState>> GetExpiredAsync(DateTimeOffset nowUtc, int maxItems, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxItems < 1) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var query = new QueryDefinition("SELECT TOP @limit * FROM c WHERE c.status = @status AND c.expiresAtUtc <= @now ORDER BY c.expiresAtUtc")
            .WithParameter("@limit", maxItems).WithParameter("@status", RideOfferStatuses.Pending).WithParameter("@now", nowUtc);
        using var iterator = container.GetItemQueryIterator<RideOfferState>(query);
        var results = new List<RideOfferState>();
        while (iterator.HasMoreResults && results.Count < maxItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await iterator.ReadNextAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = RequireQueryItem(item);
                var response = await container.ReadItemAsync<RideOfferState>(
                    candidate.RideId, new PartitionKey(candidate.RideId), cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(RequireResource(response, candidate.RideId, candidate.DriverId) with { ETag = response.ETag });
                if (results.Count >= maxItems) break;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }

    public async Task<IReadOnlyList<RideOfferState>> GetPendingNotificationsAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxItems < 1) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var query = new QueryDefinition("SELECT TOP @limit * FROM c WHERE c.status = @status AND c.notificationStatus = @notificationStatus ORDER BY c.offeredAtUtc")
            .WithParameter("@limit", maxItems).WithParameter("@status", RideOfferStatuses.Pending)
            .WithParameter("@notificationStatus", "Pending");
        using var iterator = container.GetItemQueryIterator<RideOfferState>(query);
        var results = new List<RideOfferState>();
        while (iterator.HasMoreResults && results.Count < maxItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await iterator.ReadNextAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = RequireQueryItem(item);
                var response = await container.ReadItemAsync<RideOfferState>(
                    candidate.RideId, new PartitionKey(candidate.RideId), cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(RequireResource(response, candidate.RideId, candidate.DriverId) with { ETag = response.ETag });
                if (results.Count >= maxItems) break;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }

    private static RideOfferState RequireQueryItem(RideOfferState? state)
    {
        if (state is null)
            throw new InvalidOperationException("Cosmos returned an empty ride offer query item.");

        if (string.IsNullOrWhiteSpace(state.RideId) || string.IsNullOrWhiteSpace(state.DriverId))
            throw new InvalidOperationException("Cosmos returned a malformed ride offer query item.");

        return state;
    }

    private static RideOfferState RequirePendingForDriverQueryItem(
        RideOfferState? state,
        string expectedDriverId)
    {
        var offer = RequireQueryItem(state);
        if (!string.Equals(offer.DriverId, expectedDriverId, StringComparison.Ordinal) ||
            !string.Equals(offer.Status, RideOfferStatuses.Pending, StringComparison.Ordinal))
            throw new InvalidOperationException("Cosmos returned a malformed ride offer query item.");

        return offer;
    }

    private static RideOfferState RequireResource(
        ItemResponse<RideOfferState> response,
        string expectedRideId,
        string? expectedDriverId = null)
    {
        var offer = response.Resource ?? throw new InvalidOperationException(
            "Cosmos returned an empty ride offer resource.");

        if (string.IsNullOrWhiteSpace(offer.RideId) ||
            !string.Equals(offer.RideId, expectedRideId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(offer.DriverId) ||
            (expectedDriverId is not null &&
             !string.Equals(offer.DriverId, expectedDriverId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Cosmos returned a malformed ride offer resource.");

        return offer;
    }

    public async Task<bool> TryCreateAsync(RideOfferState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var response = await container.CreateItemAsync(state with { ETag = null }, new PartitionKey(state.RideId), cancellationToken: cancellationToken);
            return response.StatusCode == System.Net.HttpStatusCode.Created;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    public async Task<bool> TryReplaceAsync(RideOfferState state, string expectedETag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A missing observed ETag must never degrade into an unconditional Cosmos replacement.
        if (!CosmosEtagPolicy.HasObservedEtag(expectedETag))
            return false;

        try
        {
            await container.ReplaceItemAsync(state with { ETag = null }, state.RideId, new PartitionKey(state.RideId),
                new ItemRequestOptions { IfMatchEtag = expectedETag }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.PreconditionFailed or System.Net.HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }
}