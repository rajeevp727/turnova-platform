using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurnOva.Application.Dispatch;

namespace TurnOva.API;

/// <summary>At-least-once SignalR delivery from durable pending offer state.</summary>
public sealed class OfferNotificationOutboxWorker(
    IRideOfferStore offers, IHubContext<RideTrackingHub> hub, TimeProvider timeProvider,
    IOptions<DispatchOfferOptions> options, ILogger<OfferNotificationOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.NotificationPollIntervalSeconds, 1, 60));
        var batchSize = Math.Clamp(options.Value.NotificationBatchSize, 1, 500);
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await offers.GetPendingNotificationsAsync(batchSize, stoppingToken);
                foreach (var item in pending)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    var current = await offers.GetByRideAsync(item.RideId, stoppingToken);
                    if (current is null || !current.IsPending || current.CurrentOfferId != item.CurrentOfferId ||
                        current.NotificationStatus != "Pending" || string.IsNullOrWhiteSpace(current.ETag)) continue;
                    await hub.Clients.Group($"driver:{current.DriverId}").SendAsync(
                        "RideOfferReceived",
                        new { rideId = current.RideId, offerId = current.CurrentOfferId, expiresAtUtc = current.ExpiresAtUtc },
                        stoppingToken);
                    var sent = current with { NotificationStatus = "Sent", NotificationSentAtUtc = timeProvider.GetUtcNow() };
                    if (!await offers.TryReplaceAsync(sent, current.ETag, stoppingToken))
                        logger.LogDebug("Notification delivered but sent marker lost ETag race for {RideId}/{OfferId}.", current.RideId, current.CurrentOfferId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Offer outbox cycle failed; pending notifications will be retried."); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}