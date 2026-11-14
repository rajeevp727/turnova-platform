using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using TurnOva.API.Observability;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed partial class RideApiIntegrationTests
{
    [Fact]
    public async Task RideCreationSucceedsWhenRideAuditActivityStartAndMetricListenersThrow()
    {
        var startedActivities = 0;
        var measurementCallbacks = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                instrument.Name == "turnova.ride.audit.write_attempts")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) =>
        {
            Interlocked.Increment(ref measurementCallbacks);
            throw new InvalidOperationException("optional-ride-metric-listener-failure");
        });
        meterListener.Start();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (activity.OperationName == "ride.audit.write")
                {
                    Interlocked.Increment(ref startedActivities);
                    throw new InvalidOperationException("optional-ride-activity-start-listener-failure");
                }
            }
        };
        ActivitySource.AddActivityListener(activityListener);

        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "ride-audit-start-listener-driver";
        const string unionId = "ride-audit-start-listener-union";
        using var rider = factory.CreateClient("Rider", "rider-ride-audit-start-listener");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var bookingRequest = CreateSelectedBookingRequest(
            "ride-audit-start-listener-failure", driverId, unionId, "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(bookingRequest);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var ride = await ReadRideAsync(response);

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        Assert.True(rides.TryGetValue(ride.RideId, out var persisted));
        Assert.Single(persisted!.AuditEvents ?? []);
        Assert.True(Volatile.Read(ref startedActivities) >= 1);
        Assert.True(Volatile.Read(ref measurementCallbacks) >= 1);
    }

    [Fact]
    public async Task RideCreationAndCancellationSucceedWhenRideAuditStopAndMetricListenersThrow()
    {
        var stoppedActivities = 0;
        var measurementCallbacks = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                instrument.Name == "turnova.ride.audit.write_attempts")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) =>
        {
            Interlocked.Increment(ref measurementCallbacks);
            throw new InvalidOperationException("optional-ride-metric-listener-failure");
        });
        meterListener.Start();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "ride.audit.write")
                {
                    Interlocked.Increment(ref stoppedActivities);
                    throw new InvalidOperationException("optional-ride-activity-stop-listener-failure");
                }
            }
        };
        ActivitySource.AddActivityListener(activityListener);

        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "ride-audit-stop-listener-driver";
        const string unionId = "ride-audit-stop-listener-union";
        using var rider = factory.CreateClient("Rider", "rider-ride-audit-stop-listener");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var bookingRequest = CreateSelectedBookingRequest(
            "ride-audit-stop-listener-failure", driverId, unionId, "KPHB", "HITEC City", []);
        using var created = await rider.SendAsync(bookingRequest);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var ride = await ReadRideAsync(created);

        using var cancelled = await rider.PostAsync($"/api/v1/rides/{ride.RideId}/cancel", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, cancelled.StatusCode);

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var persisted = rides[ride.RideId];
        Assert.Equal("Cancelled", persisted.Status);
        Assert.Equal(2, (persisted.AuditEvents ?? []).Count);
        Assert.Equal(2, Volatile.Read(ref stoppedActivities));
        Assert.True(Volatile.Read(ref measurementCallbacks) >= 2);
    }
}
