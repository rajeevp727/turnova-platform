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

    public static Activity? StartStorageReadinessCheck() =>
        ActivitySource.StartActivity("storage.readiness.check");

    public static void RecordRideAuditWrite(string? eventType, bool succeeded)
    {
        RideAuditWriteAttempts.Add(
            1,
            new KeyValuePair<string, object?>("event_type", NormalizeEventType(eventType)),
            new KeyValuePair<string, object?>("outcome", succeeded ? "success" : "conflict"));
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
}
