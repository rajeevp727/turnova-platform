using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TurnOva.API.Observability;

/// <summary>
/// Emits low-cardinality .NET diagnostic signals for host-provided metrics and trace listeners.
/// No ride, user, union, route, contact, or credential identifiers are recorded.
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
        var activity = ActivitySource.StartActivity("ride.audit.write");
        activity?.SetTag("turnova.audit.event_type", NormalizeEventType(eventType));
        return activity;
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

    public static Activity? StartStorageReadinessCheck() =>
        ActivitySource.StartActivity("storage.readiness.check");

    public static void RecordRideAuditWrite(string? eventType, string outcome)
    {
        var safeOutcome = outcome switch
        {
            "success" => "success",
            "conflict" => "conflict",
            _ => "failure"
        };
        RideAuditWriteAttempts.Add(
            1,
            new KeyValuePair<string, object?>("event_type", NormalizeEventType(eventType)),
            new KeyValuePair<string, object?>("outcome", safeOutcome));
    }

    public static void RecordUnionAuditWrite(string? eventType, string outcome)
    {
        var safeOutcome = outcome switch
        {
            "success" => "success",
            "conflict" => "conflict",
            _ => "failure"
        };
        try
        {
            UnionAuditWriteAttempts.Add(
                1,
                new KeyValuePair<string, object?>("event_type", NormalizeUnionAuditEventType(eventType)),
                new KeyValuePair<string, object?>("outcome", safeOutcome));
        }
        catch
        {
            // Metrics are best-effort and must not alter audit persistence semantics.
        }
    }

    public static void RecordStorageReadiness(string outcome, TimeSpan elapsed)
    {
        var safeOutcome = outcome switch
        {
            "success" => "success",
            "timeout" => "timeout",
            "cancelled" => "cancelled",
            _ => "unavailable"
        };
        var tags = new KeyValuePair<string, object?>("outcome", safeOutcome);
        StorageReadinessChecks.Add(1, tags);
        StorageReadinessDuration.Record(Math.Max(0, elapsed.TotalMilliseconds), tags);
    }

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
