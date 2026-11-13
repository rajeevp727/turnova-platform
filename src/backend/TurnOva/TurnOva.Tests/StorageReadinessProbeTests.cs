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

        var probe = new StorageReadinessProbe(_ => Task.CompletedTask, TimeSpan.FromSeconds(1));

        await probe.CheckAsync(CancellationToken.None);

        Assert.Contains(measurements, item =>
            item.Instrument == "turnova.storage.readiness.checks" && item.Outcome == "success");
        Assert.Contains(measurements, item =>
            item.Instrument == "turnova.storage.readiness.duration" && item.Outcome == "success");
        Assert.Contains(activities, item =>
            item.OperationName == "storage.readiness.check" &&
            Equals(item.GetTagItem("turnova.storage.outcome"), "success"));
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
    public async Task CheckAsyncPropagatesCallerCancellationDuringProbe()
    {
        var outcomes = new ConcurrentBag<string>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                instrument.Name == "turnova.storage.readiness.checks")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            outcomes.Add(tags.ToArray().FirstOrDefault(tag => tag.Key == "outcome").Value?.ToString() ?? "");
        });
        meterListener.Start();

        var probeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new StorageReadinessProbe(
            async ct =>
            {
                probeStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            },
            TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();

        var check = probe.CheckAsync(cancellation.Token);
        await probeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        Assert.Contains("cancelled", outcomes);
    }

    [Fact]
    public async Task CheckAsyncMapsUnexpectedProviderExceptionToUnavailableReadinessFailure()
    {
        const string failureMessage = "private-storage-provider-detail";
        var probe = new StorageReadinessProbe(
            _ => Task.FromException(new InvalidOperationException(failureMessage)),
            TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<StorageReadinessException>(
            () => probe.CheckAsync(CancellationToken.None));

        Assert.Equal("unavailable", exception.FailureKind);
        Assert.Equal(failureMessage, exception.InnerException?.Message);
    }

    [Fact]
    public async Task CheckAsyncMapsSpontaneousProviderCancellationToUnavailableNotTimeout()
    {
        var probe = new StorageReadinessProbe(
            _ => Task.FromException(new OperationCanceledException("provider-cancelled")),
            TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<StorageReadinessException>(
            () => probe.CheckAsync(CancellationToken.None));

        Assert.Equal("unavailable", exception.FailureKind);
        Assert.IsType<OperationCanceledException>(exception.InnerException);
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
        var activity = activities.FirstOrDefault(item =>
            item.OperationName == "storage.readiness.check" &&
            Equals(item.GetTagItem("turnova.storage.outcome"), "unavailable"));
        Assert.NotNull(activity);
        Assert.DoesNotContain(activity!.Tags, tag =>
            tag.Value?.Contains("private-storage-endpoint", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void RideAuditMetricsNormalizeEventTypeAndOutcomeTags()
    {
        var measurements = new ConcurrentBag<(string EventType, string Outcome, string[] TagKeys)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                instrument.Name == "turnova.ride.audit.write_attempts")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var values = tags.ToArray();
            measurements.Add((
                values.FirstOrDefault(tag => tag.Key == "event_type").Value?.ToString() ?? "",
                values.FirstOrDefault(tag => tag.Key == "outcome").Value?.ToString() ?? "",
                values.Select(tag => tag.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray()));
        });
        meterListener.Start();

        TurnOvaDiagnostics.RecordRideAuditWrite("PaymentReportedUnverified", "failure");
        TurnOvaDiagnostics.RecordRideAuditWrite("untrusted-driver-identifier", "conflict");

        Assert.Contains(measurements, item =>
            item.EventType == "payment_reported_unverified" && item.Outcome == "failure");
        Assert.Contains(measurements, item =>
            item.EventType == "other" && item.Outcome == "conflict");
        Assert.All(measurements, item =>
            Assert.Equal(new[] { "event_type", "outcome" }, item.TagKeys));
    }

}
