using TurnOva.API;
using TurnOva.API.Observability;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TurnOva.Tests;

public sealed class StorageReadinessProbeTests
{
    [Fact]
    public async Task CheckAsyncCompletesWhenStorageProbeSucceeds()
    {
        var probe = new StorageReadinessProbe(_ => Task.CompletedTask, TimeSpan.FromSeconds(1));

        await probe.CheckAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CheckAsyncMapsProbeTimeoutToReadinessFailure()
    {
        var probe = new StorageReadinessProbe(
            ct => Task.Delay(Timeout.InfiniteTimeSpan, ct),
            TimeSpan.FromMilliseconds(20));

        var exception = await Assert.ThrowsAsync<StorageReadinessException>(
            () => probe.CheckAsync(CancellationToken.None));

        Assert.Equal("timeout", exception.FailureKind);
    }

    [Fact]
    public async Task CheckAsyncPropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probe = new StorageReadinessProbe(
            ct => Task.FromCanceled(ct),
            TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.CheckAsync(cancellation.Token));
    }
    [Fact]
    public async Task ReadinessProbeEmitsBoundedMetricsAndTraceWithoutDependencyDetails()
    {
        var measurements = new ConcurrentBag<(string Instrument, string Outcome)>();
        var activities = new ConcurrentBag<Activity>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                instrument.Name.StartsWith("turnova.storage.readiness.", StringComparison.Ordinal))
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var outcome = tags.ToArray().FirstOrDefault(tag => tag.Key == "outcome").Value?.ToString() ?? "";
            measurements.Add((instrument.Name, outcome));
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            var outcome = tags.ToArray().FirstOrDefault(tag => tag.Key == "outcome").Value?.ToString() ?? "";
            measurements.Add((instrument.Name, outcome));
        });
        meterListener.Start();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        var probe = new StorageReadinessProbe(
            _ => Task.FromException(new HttpRequestException("private-storage-endpoint")),
            TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<StorageReadinessException>(
            () => probe.CheckAsync(CancellationToken.None));

        Assert.Equal("unavailable", exception.FailureKind);
        Assert.Contains(measurements, item =>
            item.Instrument == "turnova.storage.readiness.checks" && item.Outcome == "unavailable");
        Assert.Contains(measurements, item =>
            item.Instrument == "turnova.storage.readiness.duration" && item.Outcome == "unavailable");
        var activity = Assert.Single(activities.Where(item => item.OperationName == "storage.readiness.check"));
        Assert.Equal("unavailable", activity.GetTagItem("turnova.storage.outcome"));
        Assert.DoesNotContain(activity.Tags, tag =>
            tag.Value?.Contains("private-storage-endpoint", StringComparison.OrdinalIgnoreCase) == true);
    }

}
