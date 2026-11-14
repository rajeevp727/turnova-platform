using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TurnOva.API;
using TurnOva.API.Observability;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed partial class RideApiIntegrationTests
{
    [Fact]
    public async Task UnionAuditWritesEmitAllowListedLowCardinalityMetricsAndActivities()
    {
        var measurements = new ConcurrentBag<(string EventType, string Outcome, string[] TagKeys)>();
        var activities = new ConcurrentBag<Activity>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                instrument.Name == "turnova.union.audit.write_attempts")
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
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "union-audit-observability-driver";
        const string unionId = "union-audit-observability-union";
        const string nextUnionId = "union-audit-observability-next-union";
        using var driver = factory.CreateClient("Driver", driverId);
        using var ops = factory.CreateClient("Ops", "union-audit-observability-ops");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        SeedTransferTargetUnion(factory, unionId);

        using var assignment = await ops.PostAsJsonAsync(
            $"/api/v1/ops/drivers/{driverId}/union-assignment",
            new { unionId, reason = "Initial Cab membership assignment" });
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);

        using var manager = factory.CreateUnionManagerClient(unionId, "union-audit-observability-manager");
        using var approval = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);

        using var retryManager = factory.CreateUnionManagerClient(unionId, "union-audit-observability-retry");
        using var repeatedApproval = await retryManager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, repeatedApproval.StatusCode);

        SeedTransferTargetUnion(factory, nextUnionId);
        using var transfer = await ops.PostAsJsonAsync(
            $"/api/v1/ops/drivers/{driverId}/union-assignment",
            new { unionId = nextUnionId, reason = "Approved membership transfer" });
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

        using var nextManager = factory.CreateUnionManagerClient(nextUnionId, "union-audit-observability-next-manager");
        using var rejection = await nextManager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = "Vehicle image requires a clearer review photo." });
        Assert.Equal(HttpStatusCode.OK, rejection.StatusCode);

        var persistedAudit = factory.Services
            .GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>()
            .Values.Where(item => item.DriverId == driverId).ToArray();
        Assert.Equal(1, persistedAudit.Count(item => item.EventType == "Assigned"));
        Assert.Equal(1, persistedAudit.Count(item => item.EventType == "Transferred"));
        Assert.Equal(1, persistedAudit.Count(item => item.EventType == "SelfieApprovedByUnion"));
        Assert.Equal(1, persistedAudit.Count(item => item.EventType == "SelfieRejectedByUnion"));

        Assert.Contains(measurements, item => item.EventType == "assigned" && item.Outcome == "success");
        Assert.Contains(measurements, item => item.EventType == "transferred" && item.Outcome == "success");
        Assert.Contains(measurements, item => item.EventType == "selfie_approved" && item.Outcome == "success");
        Assert.Contains(measurements, item => item.EventType == "selfie_approved" && item.Outcome == "conflict");
        Assert.Contains(measurements, item => item.EventType == "selfie_rejected" && item.Outcome == "success");
        Assert.All(measurements, item =>
        {
            Assert.Equal(new[] { "event_type", "outcome" }, item.TagKeys);
            Assert.Contains(item.EventType, new[] { "assigned", "transferred", "selfie_approved", "selfie_rejected", "other" });
            Assert.Contains(item.Outcome, new[] { "success", "conflict", "failure" });
        });

        var unionActivities = activities.Where(item => item.OperationName == "union.audit.write").ToArray();
        Assert.NotEmpty(unionActivities);
        Assert.Contains(unionActivities, item =>
            Equals(item.GetTagItem("turnova.union.audit.event_type"), "assigned") &&
            Equals(item.GetTagItem("turnova.union.audit.outcome"), "success"));
        Assert.Contains(unionActivities, item =>
            Equals(item.GetTagItem("turnova.union.audit.event_type"), "selfie_approved") &&
            Equals(item.GetTagItem("turnova.union.audit.outcome"), "success"));
        Assert.Contains(unionActivities, item =>
            Equals(item.GetTagItem("turnova.union.audit.event_type"), "selfie_approved") &&
            Equals(item.GetTagItem("turnova.union.audit.outcome"), "conflict"));
        Assert.Contains(unionActivities, item =>
            Equals(item.GetTagItem("turnova.union.audit.event_type"), "transferred") &&
            Equals(item.GetTagItem("turnova.union.audit.outcome"), "success"));
        Assert.Contains(unionActivities, item =>
            Equals(item.GetTagItem("turnova.union.audit.event_type"), "selfie_rejected") &&
            Equals(item.GetTagItem("turnova.union.audit.outcome"), "success"));
        Assert.All(unionActivities, item =>
            Assert.Equal(
                new[] { "turnova.union.audit.event_type", "turnova.union.audit.outcome" },
                item.Tags.Select(tag => tag.Key).ToArray()));

        var telemetryValues = string.Join("|",
            measurements.SelectMany(item => new[] { item.EventType, item.Outcome })
                .Concat(unionActivities.SelectMany(item => item.Tags).Select(tag => tag.Value?.ToString() ?? "")));
        Assert.DoesNotContain(driverId, telemetryValues, StringComparison.Ordinal);
        Assert.DoesNotContain(unionId, telemetryValues, StringComparison.Ordinal);
        Assert.DoesNotContain(nextUnionId, telemetryValues, StringComparison.Ordinal);
        Assert.DoesNotContain("Vehicle image requires a clearer review photo.", telemetryValues, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnionAuditCanonicalMismatchEmitsFailureAndErrorActivity()
    {
        var measurements = new ConcurrentBag<(string EventType, string Outcome, string[] TagKeys)>();
        var activities = new ConcurrentBag<Activity>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                instrument.Name == "turnova.union.audit.write_attempts")
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
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "union-audit-failure-driver";
        const string unionId = "union-audit-failure-union";
        const string originalActor = "union-audit-original-actor";
        const string unionName = "Selected Booking Union";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);
        var reviewedAtUtc = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Approved",
            ProfilePhotoApprovedByUnionId = unionId,
            ProfilePhotoReviewedAtUtc = reviewedAtUtc,
            ProfilePhotoReviewReason = null,
            ProfilePhotoUnionReviewActorId = originalActor,
            ProfilePhotoUnionReviewUnionId = unionId,
            ProfilePhotoUnionReviewUnionName = unionName
        };

        var eventId = DriverUnionAuditIdentity.CreateUnionSelfieReviewEventId(
            driverId, unionId, "SelfieApprovedByUnion", reviewedAtUtc);
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>()
            [$"{driverId}|{eventId}"] = new DriverUnionAuditEvent(
                eventId, driverId, unionId, unionName, unionId, unionName,
                "SelfieApprovedByUnion", "Approved by the currently assigned union.",
                "different-safe-actor", reviewedAtUtc);

        using var manager = factory.CreateUnionManagerClient(unionId, "union-audit-failure-manager");
        using var response = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Approved", reason = (string?)null });
        Assert.False(response.IsSuccessStatusCode);

        Assert.Contains(measurements, item => item.EventType == "selfie_approved" && item.Outcome == "failure");
        Assert.All(measurements, item => Assert.Equal(new[] { "event_type", "outcome" }, item.TagKeys));
        var preservedCanonical = factory.Services
            .GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>()
            .Values.Single(item => item.EventId == eventId);
        Assert.Equal("different-safe-actor", preservedCanonical.Actor);
        var failedActivity = Assert.Single(activities, item => item.OperationName == "union.audit.write");
        Assert.Equal(ActivityStatusCode.Error, failedActivity.Status);
        Assert.Equal("selfie_approved", failedActivity.GetTagItem("turnova.union.audit.event_type"));
        Assert.Equal("failure", failedActivity.GetTagItem("turnova.union.audit.outcome"));
        Assert.Equal(
            new[] { "turnova.union.audit.event_type", "turnova.union.audit.outcome" },
            failedActivity.Tags.Select(tag => tag.Key).ToArray());
    }

    [Fact]
    public void UnionAuditTelemetryListenerFailuresDoNotEscapeTheAuditBoundary()
    {
        using (var meterListener = new MeterListener())
        {
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                    instrument.Name == "turnova.union.audit.write_attempts")
                    listener.EnableMeasurementEvents(instrument);
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                throw new InvalidOperationException("listener failure"));
            meterListener.Start();

            Assert.Null(Record.Exception(() => TurnOvaDiagnostics.RecordUnionAuditWrite("Assigned", "success")));
        }

        using (var failingSampleListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Name == "union.audit.write"
                    ? throw new InvalidOperationException("sample listener failure")
                    : ActivitySamplingResult.None
        })
        {
            ActivitySource.AddActivityListener(failingSampleListener);
            Assert.Null(Record.Exception(() => TurnOvaDiagnostics.StartUnionAuditWrite("Assigned")));
        }

        using (var failingStopListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "union.audit.write")
                    throw new InvalidOperationException("stop listener failure");
            }
        })
        {
            ActivitySource.AddActivityListener(failingStopListener);
            var activity = TurnOvaDiagnostics.StartUnionAuditWrite("Assigned");
            Assert.NotNull(activity);
            Assert.Null(Record.Exception(() => TurnOvaDiagnostics.StopUnionAuditWrite(activity)));
        }
    }

    [Fact]
    public void UnionAuditTelemetryCollapsesUnrecognizedEventAndOutcomeValues()
    {
        var measurements = new ConcurrentBag<(string EventType, string Outcome, string[] TagKeys)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                instrument.Name == "turnova.union.audit.write_attempts")
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

        TurnOvaDiagnostics.RecordUnionAuditWrite("private-event-driver-123", "private-outcome");

        var measurement = Assert.Single(measurements);
        Assert.Equal("other", measurement.EventType);
        Assert.Equal("failure", measurement.Outcome);
        Assert.Equal(new[] { "event_type", "outcome" }, measurement.TagKeys);
    }

    [Fact]
    public async Task UnionAuditPersistenceSurvivesThrowingTelemetryListeners()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "union-audit-listener-failure-driver";
        const string firstUnionId = "union-audit-listener-failure-first";
        const string nextUnionId = "union-audit-listener-failure-next";
        using var driver = factory.CreateClient("Driver", driverId);
        using var ops = factory.CreateClient("Ops", "union-audit-listener-failure-ops");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        SeedTransferTargetUnion(factory, firstUnionId);
        SeedTransferTargetUnion(factory, nextUnionId);

        using (var meterListener = new MeterListener())
        {
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                    instrument.Name == "turnova.union.audit.write_attempts")
                    listener.EnableMeasurementEvents(instrument);
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                throw new InvalidOperationException("Union audit metric listener failure"));
            meterListener.Start();

            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                    options.Name == "union.audit.write"
                        ? throw new InvalidOperationException("Union audit activity start listener failure")
                        : ActivitySamplingResult.None
            };
            ActivitySource.AddActivityListener(activityListener);

            using var assignment = await ops.PostAsJsonAsync(
                $"/api/v1/ops/drivers/{driverId}/union-assignment",
                new { unionId = firstUnionId, reason = "Initial Cab membership assignment" });
            Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
        }

        using (var meterListener = new MeterListener())
        {
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                    instrument.Name == "turnova.union.audit.write_attempts")
                    listener.EnableMeasurementEvents(instrument);
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                throw new InvalidOperationException("Union audit metric listener failure"));
            meterListener.Start();

            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (activity.OperationName == "union.audit.write")
                        throw new InvalidOperationException("Union audit activity stop listener failure");
                }
            };
            ActivitySource.AddActivityListener(activityListener);

            using var transfer = await ops.PostAsJsonAsync(
                $"/api/v1/ops/drivers/{driverId}/union-assignment",
                new { unionId = nextUnionId, reason = "Approved membership transfer" });
            Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);
        }

        var persisted = factory.Services
            .GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>()
            .Values.Where(item => item.DriverId == driverId).ToArray();
        Assert.Equal(2, persisted.Length);
        Assert.Contains(persisted, item => item.EventType == "Assigned" && item.NewUnionId == firstUnionId);
        Assert.Contains(persisted, item =>
            item.EventType == "Transferred" &&
            item.PreviousUnionId == firstUnionId &&
            item.NewUnionId == nextUnionId);
    }

}
