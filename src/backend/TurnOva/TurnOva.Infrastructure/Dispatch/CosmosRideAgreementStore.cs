using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

/// <summary>
/// Agreement terms and confirmations are append-only documents in a ride-scoped Cosmos partition.
/// </summary>
public sealed class CosmosRideAgreementStore(Container versions, Container confirmations) : IRideAgreementStore
{
    public async Task<IReadOnlyList<RideAgreementVersion>> GetVersionsAsync(string rideId, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.rideId = @rideId ORDER BY c.version")
            .WithParameter("@rideId", rideId);
        var results = new List<RideAgreementVersion>();
        using var iterator = versions.GetItemQueryIterator<RideAgreementVersion>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(rideId) });
        while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        return results;
    }

    public async Task<bool> TryCreateVersionAsync(RideAgreementVersion version, CancellationToken cancellationToken = default)
    {
        try
        {
            await versions.CreateItemAsync(version, new PartitionKey(version.RideId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { return false; }
    }

    public async Task<IReadOnlyList<RideAgreementConfirmation>> GetConfirmationsAsync(string rideId, int version, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.rideId = @rideId AND c.version = @version")
            .WithParameter("@rideId", rideId).WithParameter("@version", version);
        var results = new List<RideAgreementConfirmation>();
        using var iterator = confirmations.GetItemQueryIterator<RideAgreementConfirmation>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(rideId) });
        while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        return results;
    }

    public async Task<bool> TryCreateConfirmationAsync(RideAgreementConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        try
        {
            await confirmations.CreateItemAsync(confirmation, new PartitionKey(confirmation.RideId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { return false; }
    }
}
