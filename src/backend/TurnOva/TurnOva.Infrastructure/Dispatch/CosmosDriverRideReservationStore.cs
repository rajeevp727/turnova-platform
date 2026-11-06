using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
namespace TurnOva.Infrastructure.Dispatch;
public sealed class CosmosDriverRideReservationStore(Container container) : IDriverRideReservationStore
{
    public Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default) =>
        TryAcquireCoreAsync(driverId, rideId, null, cancellationToken);

    public Task<bool> TryAcquireUntilAsync(string driverId, string rideId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        if (expiresAtUtc <= DateTimeOffset.UtcNow) throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        return TryAcquireCoreAsync(driverId, rideId, expiresAtUtc, cancellationToken);
    }

    private async Task<bool> TryAcquireCoreAsync(string driverId, string rideId, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var reservation = new DriverRideReservation(driverId, rideId, now, expiresAtUtc);
        try
        {
            await container.CreateItemAsync(reservation, new PartitionKey(driverId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            ItemResponse<DriverRideReservation> current;
            try
            {
                current = await container.ReadItemAsync<DriverRideReservation>(
                    driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            }
            catch (CosmosException readEx) when (readEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                try
                {
                    await container.CreateItemAsync(reservation, new PartitionKey(driverId), cancellationToken: cancellationToken);
                    return true;
                }
                catch (CosmosException retryEx) when (retryEx.StatusCode == System.Net.HttpStatusCode.Conflict) { return false; }
            }

            if (current.Resource.ExpiresAtUtc is null || current.Resource.ExpiresAtUtc > now) return false;
            try
            {
                await container.DeleteItemAsync<DriverRideReservation>(driverId, new PartitionKey(driverId),
                    new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
            }
            catch (CosmosException deleteEx) when (deleteEx.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
            {
                return false;
            }

            try
            {
                await container.CreateItemAsync(reservation, new PartitionKey(driverId), cancellationToken: cancellationToken);
                return true;
            }
            catch (CosmosException retryEx) when (retryEx.StatusCode == System.Net.HttpStatusCode.Conflict) { return false; }
        }
    }

    public async Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            if (current.Resource.ExpiresAtUtc is null || current.Resource.ExpiresAtUtc > DateTimeOffset.UtcNow)
                return true;
            try
            {
                await container.DeleteItemAsync<DriverRideReservation>(driverId, new PartitionKey(driverId),
                    new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
                return false;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return false; }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed) { return true; }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
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