using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class CosmosRideOfferStore(Container container) : IRideOfferStore
{
    public async Task<RideOfferState?> GetByRideAsync(string rideId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<RideOfferState>(rideId, new PartitionKey(rideId), cancellationToken: cancellationToken);
            return response.Resource with { ETag = response.ETag };
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public async Task<RideOfferState?> GetPendingForDriverAsync(string driverId, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.driverId = @driverId AND c.status = @status")
            .WithParameter("@driverId", driverId).WithParameter("@status", RideOfferStatuses.Pending);
        using var iterator = container.GetItemQueryIterator<RideOfferState>(query);
        while (iterator.HasMoreResults)
        {
            var state = (await iterator.ReadNextAsync(cancellationToken)).FirstOrDefault();
            if (state is not null) return state;
        }
        return null;
    }

    public async Task<IReadOnlyList<RideOfferState>> GetExpiredAsync(DateTimeOffset nowUtc, int maxItems, CancellationToken cancellationToken = default)
    {
        if (maxItems < 1) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var query = new QueryDefinition("SELECT TOP @limit * FROM c WHERE c.status = @status AND c.expiresAtUtc <= @now ORDER BY c.expiresAtUtc")
            .WithParameter("@limit", maxItems).WithParameter("@status", RideOfferStatuses.Pending).WithParameter("@now", nowUtc);
        using var iterator = container.GetItemQueryIterator<RideOfferState>(query);
        var results = new List<RideOfferState>();
        while (iterator.HasMoreResults && results.Count < maxItems)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page)
            {
                var response = await container.ReadItemAsync<RideOfferState>(item.RideId, new PartitionKey(item.RideId), cancellationToken: cancellationToken);
                results.Add(response.Resource with { ETag = response.ETag });
                if (results.Count >= maxItems) break;
            }
        }
        return results;
    }

    public async Task<IReadOnlyList<RideOfferState>> GetPendingNotificationsAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        if (maxItems < 1) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var query = new QueryDefinition("SELECT TOP @limit * FROM c WHERE c.status = @status AND c.notificationStatus = @notificationStatus ORDER BY c.offeredAtUtc")
            .WithParameter("@limit", maxItems).WithParameter("@status", RideOfferStatuses.Pending)
            .WithParameter("@notificationStatus", "Pending");
        using var iterator = container.GetItemQueryIterator<RideOfferState>(query);
        var results = new List<RideOfferState>();
        while (iterator.HasMoreResults && results.Count < maxItems)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page)
            {
                var response = await container.ReadItemAsync<RideOfferState>(item.RideId, new PartitionKey(item.RideId), cancellationToken: cancellationToken);
                results.Add(response.Resource with { ETag = response.ETag });
                if (results.Count >= maxItems) break;
            }
        }
        return results;
    }

    public async Task<bool> TryCreateAsync(RideOfferState state, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.CreateItemAsync(state with { ETag = null }, new PartitionKey(state.RideId), cancellationToken: cancellationToken);
            return response.StatusCode == System.Net.HttpStatusCode.Created;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { return false; }
    }

    public async Task<bool> TryReplaceAsync(RideOfferState state, string expectedETag, CancellationToken cancellationToken = default)
    {
        try
        {
            await container.ReplaceItemAsync(state with { ETag = null }, state.RideId, new PartitionKey(state.RideId),
                new ItemRequestOptions { IfMatchEtag = expectedETag }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.PreconditionFailed or System.Net.HttpStatusCode.NotFound) { return false; }
    }
}