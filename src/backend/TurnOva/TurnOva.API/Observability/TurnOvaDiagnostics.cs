using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TurnOva.API.Observability;

/// <summary>
/// Emits low-cardinality .NET diagnostic signals for host-provided metrics and trace listeners.
/// No ride, user, union, route, contact, or credential identifiers are recorded.
/// Diagnostic listeners are optional: callback failures must never alter authoritative API work.
/// </summary>
public static class TurnOvaDiagnostics
{
    public const string MeterName = "TurnOva.API";
    public const string ActivitySourceName = "TurnOva.API";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    private static readonly Counter<long> RideAuditWriteAttempts =
        Meter.CreateCounter<long>("turnova.ride.audit.write_attempts", "{attempt}");

    private static readonly Counter<long> UnionAuditWriteAttempts =
        Meter.CreateCounter<long>("turnova.union.audit.write_attempts", "{attempt}");

    private static readonly Counter<long> StorageReadinessChecks =
        Meter.CreateCounter<long>("turnova.storage.readiness.checks", "{check}");

    private static readonly Histogram<double> StorageReadinessDuration =
        Meter.CreateHistogram<double>("turnova.storage.readiness.duration", "ms");

    public static Activity? StartRideAuditWrite(string? eventType)
    {
        try
        {
            var activity = ActivitySource.StartActivity("ride.audit.write");
            activity?.SetTag("turnova.audit.event_type", NormalizeEventType(eventType));
            return activity;
        }
        catch
        {
            // Optional tracing must never block authoritative ride persistence.
            return null;
        }
    }

    public static void SetRideAuditOutcome(Activity? activity, string outcome)
    {
        try
        {
            var safeOutcome = NormalizeWriteOutcome(outcome);
            activity?.SetTag("turnova.audit.outcome", safeOutcome);
            activity?.SetStatus(safeOutcome == "success" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }
        catch
        {
            // A listener or disposed activity must not change the ride write result.
        }
    }

    public static void StopRideAuditWrite(Activity? activity)
    {
        try
        {
            activity?.Dispose();
        }
        catch
        {
            // ActivityStopped callbacks are optional and must not escape a ride write.
        }
    }

    public static Activity? StartUnionAuditWrite(string? eventType)
    {
        try
        {
            var activity = ActivitySource.StartActivity("union.audit.write");
            activity?.SetTag("turnova.union.audit.event_type", NormalizeUnionAuditEventType(eventType));
            return activity;
        }
        catch
        {
            // Optional telemetry must never block the authoritative Union audit write.
            return null;
        }
    }

    public static void StopUnionAuditWrite(Activity? activity)
    {
        try
        {
            activity?.Dispose();
        }
        catch
        {
            // A listener failure must not turn a persisted audit event into an API failure.
        }
    }

    public static Activity? StartStorageReadinessCheck()
    {
        try
        {
            return ActivitySource.StartActivity("storage.readiness.check");
        }
        catch
        {
            // A tracing listener is not part of the storage dependency being probed.
            return null;
        }
    }

    public static void SetStorageReadinessOutcome(Activity? activity, string outcome)
    {
        try
        {
            var safeOutcome = NormalizeStorageOutcome(outcome);
            activity?.SetTag("turnova.storage.outcome", safeOutcome);
            if (safeOutcome is "unavailable" or "timeout")
                activity?.SetStatus(ActivityStatusCode.Error);
        }
        catch
        {
            // Optional tracing must not replace the original readiness result.
        }
    }

    public static void StopStorageReadinessCheck(Activity? activity)
    {
        try
        {
            activity?.Dispose();
        }
        catch
        {
            // ActivityStopped callbacks must not replace a readiness result or exception.
        }
    }

    public static void RecordRideAuditWrite(string? eventType, string outcome)
    {
        try
        {
            RideAuditWriteAttempts.Add(
                1,
                new KeyValuePair<string, object?>("event_type", NormalizeEventType(eventType)),
                new KeyValuePair<string, object?>("outcome", NormalizeWriteOutcome(outcome)));
        }
        catch
        {
            // Metrics are best-effort; persistence and its original failure remain authoritative.
        }
    }

    public static void RecordUnionAuditWrite(string? eventType, string outcome)
    {
        try
        {
            UnionAuditWriteAttempts.Add(
                1,
                new KeyValuePair<string, object?>("event_type", NormalizeUnionAuditEventType(eventType)),
                new KeyValuePair<string, object?>("outcome", NormalizeWriteOutcome(outcome)));
        }
        catch
        {
            // Metrics are best-effort and must not alter audit persistence semantics.
        }
    }

    public static void RecordStorageReadiness(string outcome, TimeSpan elapsed)
    {
        var safeOutcome = NormalizeStorageOutcome(outcome);
        var tags = new KeyValuePair<string, object?>("outcome", safeOutcome);

        try
        {
            StorageReadinessChecks.Add(1, tags);
        }
        catch
        {
            // A failing counter listener must not suppress the readiness result.
        }

        try
        {
            StorageReadinessDuration.Record(Math.Max(0, elapsed.TotalMilliseconds), tags);
        }
        catch
        {
            // A failing histogram listener must not suppress the readiness result.
        }
    }

    private static string NormalizeWriteOutcome(string outcome) => outcome switch
    {
        "success" => "success",
        "conflict" => "conflict",
        _ => "failure"
    };

    private static string NormalizeStorageOutcome(string outcome) => outcome switch
    {
        "success" => "success",
        "timeout" => "timeout",
        "cancelled" => "cancelled",
        _ => "unavailable"
    };

    private static string NormalizeEventType(string? eventType) => eventType switch
    {
        "RideCreated" or "RideRequested" => "ride_created",
        "AgreementProposed" => "agreement_proposed",
        "AgreementPartyConfirmed" => "agreement_party_confirmed",
        "RideStatusChanged" => "ride_status_changed",
        "PaymentReportedUnverified" => "payment_reported_unverified",
        _ => "other"
    };

    private static string NormalizeUnionAuditEventType(string? eventType) => eventType switch
    {
        "Assigned" => "assigned",
        "Transferred" => "transferred",
        "SelfieApprovedByUnion" => "selfie_approved",
        "SelfieRejectedByUnion" or "SelfieRejectedByOperations" => "selfie_rejected",
        _ => "other"
    };
}
