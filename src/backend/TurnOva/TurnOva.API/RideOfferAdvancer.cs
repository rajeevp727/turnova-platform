using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;

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
        cancellationToken.ThrowIfCancellationRequested();
        var current = await offers.GetByRideAsync(rideId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (current is null || current.IsPending ||
            current.Status is not (RideOfferStatuses.Rejected or RideOfferStatuses.Expired) ||
            string.IsNullOrWhiteSpace(current.ETag)) return false;

        var ride = await GetRideAsync(rideId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (ride is null || ride.Status != "Requested" ||
            !string.Equals(ride.DispatchZoneId, current.ZoneId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ride.ServiceClass, current.ServiceClass, StringComparison.OrdinalIgnoreCase)) return false;

        var queue = await queues.GetEligibleAsync(current.ZoneId, current.ServiceClass, cancellationToken);
        foreach (var candidate in queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = await GetProfileAsync(candidate.DriverId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (profile is null || profile.Status != "Approved" || profile.AvailabilityStatus != "Online" ||
                profile.PassengerCapacity < ride.Passengers ||
                !string.Equals(profile.ZoneId, current.ZoneId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(profile.ServiceClass, current.ServiceClass, StringComparison.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            if (!await offers.TryReplaceAsync(next, current.ETag, cancellationToken)) return false;
            return true;
        }
        return false;
    }

    private async Task<global::DemoRide?> GetRideAsync(string rideId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (rideContainer is null)
        {
            if (!inMemoryRides.TryGetValue(rideId, out var ride)) return null;
            if (!string.Equals(ride.RideId, rideId, StringComparison.Ordinal))
                throw new InvalidOperationException("The Ride snapshot identity does not match the requested Ride.");
            return ride;
        }

        try
        {
            var response = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => rideContainer!.ReadItemAsync<global::DemoRide>(
                    rideId, new PartitionKey(rideId), cancellationToken: ct),
                ct);
            return CosmosReadResponseGuard.ReadResource(
                response,
                item => string.Equals(item.RideId, rideId, StringComparison.Ordinal),
                "Ride",
                ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    private async Task<DriverOnboardingProfile?> GetProfileAsync(string driverId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (driverContainer is null)
        {
            if (!inMemoryProfiles.TryGetValue(driverId, out var profile)) return null;
            if (!string.Equals(profile.Id, driverId, StringComparison.Ordinal) ||
                !string.Equals(profile.DriverId, driverId, StringComparison.Ordinal))
                throw new InvalidOperationException("The Driver profile identity does not match the requested Driver.");
            return profile;
        }

        try
        {
            var response = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => driverContainer!.ReadItemAsync<DriverOnboardingProfile>(
                    driverId, new PartitionKey(driverId), cancellationToken: ct),
                ct);
            return CosmosReadResponseGuard.ReadResource(
                response,
                item => string.Equals(item.Id, driverId, StringComparison.Ordinal) &&
                    string.Equals(item.DriverId, driverId, StringComparison.Ordinal),
                "Driver profile",
                ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }
}
