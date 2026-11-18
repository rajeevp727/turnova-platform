using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurnOva.Application.Dispatch;

namespace TurnOva.API;

public sealed class DispatchOfferExpiryOptions
{
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
}

public sealed class RideOfferExpiryWorker(
    IRideOfferExpiryProcessor processor,
    IOptions<DispatchOfferExpiryOptions> options,
    ILogger<RideOfferExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 1, 300));
        var batchSize = Math.Clamp(settings.BatchSize, 1, 500);
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await processor.ProcessBatchAsync(batchSize, stoppingToken);
                if (count > 0) logger.LogInformation("Expired {Count} ride offers and applied FIFO recovery.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Ride-offer expiry cycle failed; unresolved offers will be retried."); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}