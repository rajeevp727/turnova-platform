using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class CosmosDriverRideReservationStore(Container container) : IDriverRideReservationStore
{
    public Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default) =>
        TryAcquireAsync(driverId, rideId, null, cancellationToken);

    public async Task<bool> TryAcquireAsync(
        string driverId,
        string rideId,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default)
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
            try
            {
                var current = await container.ReadItemAsync<DriverRideReservation>(
                    driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
                if (current.Resource.ExpiresAtUtc is null || current.Resource.ExpiresAtUtc > now)
                    return false;

                await container.ReplaceItemAsync(
                    reservation,
                    driverId,
                    new PartitionKey(driverId),
                    new ItemRequestOptions { IfMatchEtag = current.ETag },
                    cancellationToken);
                return true;
            }
            catch (CosmosException replaceException) when (
                replaceException.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
            {
                return false;
            }
        }
    }

    public async Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            return current.Resource.ExpiresAtUtc is null || current.Resource.ExpiresAtUtc > DateTimeOffset.UtcNow;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> IsReservedForAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            return current.Resource.RideId == rideId &&
                (current.Resource.ExpiresAtUtc is null || current.Resource.ExpiresAtUtc > DateTimeOffset.UtcNow);
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
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            if (current.Resource.RideId != rideId) return false;
            await container.DeleteItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId),
                new ItemRequestOptions { IfMatchEtag = current.ETag },
                cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (
            ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
    }
}
