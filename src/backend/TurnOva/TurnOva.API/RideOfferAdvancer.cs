using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.API;

public sealed class RideOfferAdvancer(
    IRideOfferStore offers, IQueueStore queues,
    ConcurrentDictionary<string, DriverOnboardingProfile> inMemoryProfiles,
    ConcurrentDictionary<string, global::DemoRide> inMemoryRides,
    Container? driverContainer, Container? rideContainer,
    TimeProvider timeProvider, IOptions<DispatchOfferOptions> offerOptions) : IRideOfferAdvancer
{
    public async Task<bool> AdvanceAsync(string rideId, CancellationToken cancellationToken = default)
    {
        var current = await offers.GetByRideAsync(rideId, cancellationToken);
        if (current is null || current.IsPending ||
            current.Status is not (RideOfferStatuses.Rejected or RideOfferStatuses.Expired) ||
            string.IsNullOrWhiteSpace(current.ETag)) return false;

        var ride = await GetRideAsync(rideId, cancellationToken);
        if (ride is null || ride.Status != "Requested" ||
            !string.Equals(ride.DispatchZoneId, current.ZoneId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ride.ServiceClass, current.ServiceClass, StringComparison.OrdinalIgnoreCase)) return false;

        var queue = await queues.GetEligibleAsync(current.ZoneId, current.ServiceClass, cancellationToken);
        foreach (var candidate in queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = await GetProfileAsync(candidate.DriverId, cancellationToken);
            if (profile is null || profile.Status != "Approved" || profile.AvailabilityStatus != "Online" ||
                profile.PassengerCapacity < ride.Passengers ||
                !string.Equals(profile.ZoneId, current.ZoneId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(profile.ServiceClass, current.ServiceClass, StringComparison.OrdinalIgnoreCase))
            {
                await queues.RemoveAsync(candidate.DriverId, current.ZoneId, current.ServiceClass, cancellationToken);
                continue;
            }

            var now = timeProvider.GetUtcNow();
            var offerId = Guid.NewGuid().ToString("N");
            var expiresAt = now.AddSeconds(offerOptions.Value.LifetimeSeconds);
            var attempt = new RideOfferAttempt(offerId, candidate.DriverId, now, expiresAt, RideOfferStatuses.Pending);
            var next = current with
            {
                CurrentOfferId = offerId, DriverId = candidate.DriverId,
                OfferedAtUtc = now, ExpiresAtUtc = expiresAt,
                Status = RideOfferStatuses.Pending,
                Attempts = current.Attempts.Append(attempt).ToArray(), ETag = null
            };
            if (!await offers.TryReplaceAsync(next, current.ETag, cancellationToken)) return false;
            return true;
        }
        return false;
    }

    private async Task<global::DemoRide?> GetRideAsync(string rideId, CancellationToken ct)
    {
        if (rideContainer is null) return inMemoryRides.TryGetValue(rideId, out var ride) ? ride : null;
        try { return (await rideContainer.ReadItemAsync<global::DemoRide>(rideId, new PartitionKey(rideId), cancellationToken: ct)).Resource; }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    private async Task<DriverOnboardingProfile?> GetProfileAsync(string driverId, CancellationToken ct)
    {
        if (driverContainer is null) return inMemoryProfiles.TryGetValue(driverId, out var profile) ? profile : null;
        try { return (await driverContainer.ReadItemAsync<DriverOnboardingProfile>(driverId, new PartitionKey(driverId), cancellationToken: ct)).Resource; }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }
}
