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

    public async Task<bool> TryAcquireNegotiationAsync(
        string driverId, string rideId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (expiresAtUtc <= now) throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));

        var replacement = new DriverRideReservation(driverId, rideId, now, expiresAtUtc);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await container.CreateItemAsync(replacement, new PartitionKey(driverId), cancellationToken: cancellationToken);
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
                    continue;
                }

                if (current.Resource.ExpiresAtUtc is null || current.Resource.ExpiresAtUtc > DateTimeOffset.UtcNow)
                    return false;

                try
                {
                    await container.ReplaceItemAsync(replacement, driverId, new PartitionKey(driverId),
                        new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
                    return true;
                }
                catch (CosmosException replaceEx) when (replaceEx.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    // Another request changed the driver's reservation. Re-read before retrying.
                }
            }
        }
        return false;
    }

    public async Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ItemResponse<DriverRideReservation> current;
            try
            {
                current = await container.ReadItemAsync<DriverRideReservation>(
                    driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            if (current.Resource.ExpiresAtUtc is null || current.Resource.ExpiresAtUtc > DateTimeOffset.UtcNow)
                return true;

            try
            {
                await container.DeleteItemAsync<DriverRideReservation>(driverId, new PartitionKey(driverId),
                    new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
                return false;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
            {
                // A newer reservation may have replaced the expired lease; inspect it.
            }
        }
        return true;
    }

    public async Task<bool> TryReleaseAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            if (current.Resource.RideId != rideId) return false;
            await container.DeleteItemAsync<DriverRideReservation>(driverId, new PartitionKey(driverId),
                new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
    }
}
