using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.API;

public sealed class SelectedDriverBookingExpiryWorker(
    ConcurrentDictionary<string, DemoRide> rides,
    IServiceProvider services,
    IDriverRideReservationStore reservations,
    TimeProvider timeProvider,
    IConfiguration configuration,
    IHubContext<RideTrackingHub> hub,
    ILogger<SelectedDriverBookingExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Clamp(
            configuration.GetValue<int?>("Dispatch:SelectedDriverBookingExpiryPollIntervalSeconds") ?? 5, 1, 300);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = await ExpireDueBookingsAsync(50, stoppingToken);
                if (expired > 0)
                    logger.LogInformation("Expired {Count} selected-driver negotiations and released matching reservations.", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Selected-driver booking expiry cycle failed; due bookings will be retried.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<int> ExpireDueBookingsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cosmos = services.GetService<CosmosClient>();
        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "TurnOvaDb";
        var container = cosmos?.GetContainer(databaseName, "Rides");
        IReadOnlyList<DemoRide> due;

        if (container is null)
        {
            due = rides.Values
                .Where(ride => ride.Status == RideLifecycle.Negotiating &&
                    ride.NegotiationExpiresAtUtc is not null && ride.NegotiationExpiresAtUtc <= now)
                .OrderBy(ride => ride.NegotiationExpiresAtUtc)
                .Take(batchSize)
                .ToArray();
        }
        else
        {
            var query = new QueryDefinition(
                "SELECT TOP @batchSize * FROM c WHERE c.status = @status AND c.negotiationExpiresAtUtc <= @now")
                .WithParameter("@batchSize", batchSize)
                .WithParameter("@status", RideLifecycle.Negotiating)
                .WithParameter("@now", now);
            var results = new List<DemoRide>();
            using var iterator = container.GetItemQueryIterator<DemoRide>(query);
            while (iterator.HasMoreResults && results.Count < batchSize)
                results.AddRange(await iterator.ReadNextAsync(cancellationToken));
            due = results.Take(batchSize).ToArray();
        }

        var expiredCount = 0;
        foreach (var candidate in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.NegotiationExpiresAtUtc is null || candidate.NegotiationExpiresAtUtc > now)
                continue;

            var expired = candidate with { Status = RideLifecycle.Expired, EndedAtUtc = now, EndedBy = "System" };
            var updated = false;

            if (container is null)
            {
                updated = rides.TryUpdate(candidate.RideId, expired, candidate);
            }
            else
            {
                try
                {
                    var current = await container.ReadItemAsync<DemoRide>(
                        candidate.RideId, new PartitionKey(candidate.RideId), cancellationToken: cancellationToken);
                    if (current.Resource.Status != RideLifecycle.Negotiating ||
                        current.Resource.NegotiationExpiresAtUtc is null ||
                        current.Resource.NegotiationExpiresAtUtc > now)
                        continue;

                    expired = current.Resource with
                    {
                        Status = RideLifecycle.Expired,
                        EndedAtUtc = now,
                        EndedBy = "System"
                    };
                    await container.ReplaceItemAsync(expired, candidate.RideId, new PartitionKey(candidate.RideId),
                        new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
                    updated = true;
                }
                catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
                {
                    // Cancellation or a competing expiry worker won the conditional update.
                }
            }

            if (!updated) continue;
            if (!string.IsNullOrWhiteSpace(expired.DriverId))
            {
                await reservations.TryReleaseAsync(expired.DriverId, expired.RideId, cancellationToken);
                await hub.Clients.Group($"driver:{expired.DriverId}")
                    .SendAsync("SelectedRideExpired", new { rideId = expired.RideId }, cancellationToken);
            }
            await hub.Clients.Group(expired.RideId)
                .SendAsync("RideStatusChanged", new { rideId = expired.RideId, status = RideLifecycle.Expired }, cancellationToken);
            expiredCount++;
        }

        return expiredCount;
    }
}
