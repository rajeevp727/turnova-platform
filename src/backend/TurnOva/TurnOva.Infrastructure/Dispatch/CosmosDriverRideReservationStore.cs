using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
namespace TurnOva.Infrastructure.Dispatch;
public sealed class CosmosDriverRideReservationStore(Container container) : IDriverRideReservationStore
{
    public async Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        try
        {
            await container.CreateItemAsync(new DriverRideReservation(driverId, rideId, DateTimeOffset.UtcNow),
                new PartitionKey(driverId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { return false; }
    }
    public async Task<bool> TryReleaseAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            if (current.Resource.RideId != rideId) return false;
            await container.DeleteItemAsync<DriverRideReservation>(driverId, new PartitionKey(driverId),
                new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed) { return false; }
    }
}