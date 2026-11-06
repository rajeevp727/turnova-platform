using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
namespace TurnOva.Infrastructure.Dispatch;
public sealed class CosmosDriverRideReservationStore(Container container) : IDriverRideReservationStore
{
    public Task<bool> TryAcquireAsync(string driverId, string rideId, CancellationToken cancellationToken = default) =>
        TryAcquireCoreAsync(driverId, rideId, null, cancellationToken);

    public Task<bool> TryAcquireTemporaryAsync(string driverId, string rideId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default) =>
        TryAcquireCoreAsync(driverId, rideId, expiresAtUtc, cancellationToken);

    private async Task<bool> TryAcquireCoreAsync(string driverId, string rideId, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken)
    {
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow) return false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await container.CreateItemAsync(new DriverRideReservation(driverId, rideId, DateTimeOffset.UtcNow, expiresAtUtc),
                    new PartitionKey(driverId), cancellationToken: cancellationToken);
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
                catch (CosmosException missing) when (missing.StatusCode == System.Net.HttpStatusCode.NotFound) { continue; }

                if (current.Resource.ExpiresAtUtc is null || current.Resource.ExpiresAtUtc > DateTimeOffset.UtcNow)
                    return current.Resource.RideId == rideId;
                try
                {
                    await container.DeleteItemAsync<DriverRideReservation>(driverId, new PartitionKey(driverId),
                        new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
                }
                catch (CosmosException race) when (race.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed) { }
            }
        }
        return false;
    }

    public async Task<bool> TryMakePermanentAsync(string driverId, string rideId, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            if (current.Resource.RideId != rideId) return false;
            if (current.Resource.ExpiresAtUtc is null) return true;
            await container.ReplaceItemAsync(current.Resource with { ExpiresAtUtc = null }, driverId,
                new PartitionKey(driverId), new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed) { return false; }
    }

    public async Task<bool> TrySetExpiryAsync(string driverId, string rideId, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken = default)
    {
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow) return false;
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            if (current.Resource.RideId != rideId) return false;
            await container.ReplaceItemAsync(current.Resource with { ExpiresAtUtc = expiresAtUtc }, driverId,
                new PartitionKey(driverId), new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed) { return false; }
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
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
            {
                return true; // A concurrent renewal wins; fail closed for discovery.
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return false; }
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
