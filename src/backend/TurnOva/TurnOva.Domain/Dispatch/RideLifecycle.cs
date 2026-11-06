namespace TurnOva.Domain.Dispatch;

public static class RideLifecycle
{
    public const string Requested = "Requested";
    public const string Negotiating = "Negotiating";
    public const string AgreementConfirmed = "AgreementConfirmed";
    public const string Accepted = "Accepted";
    public const string Arrived = "Arrived";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";

    public static bool AreCoordinatesValid(double latitude, double longitude) =>
        double.IsFinite(latitude) && latitude >= -90 && latitude <= 90 &&
        double.IsFinite(longitude) && longitude >= -180 && longitude <= 180;

    public static bool CanTransition(string current, string next) =>
        (current, next) switch
        {
            (Requested, Accepted) => true, // Legacy compatibility path only.
            (Negotiating, AgreementConfirmed) => true,
            (Negotiating, Cancelled or Expired) => true,
            (AgreementConfirmed, Negotiating or Accepted or Cancelled or Expired) => true,
            (Accepted, Arrived) => true,
            (Arrived, InProgress) => true,
            (InProgress, Completed) => true,
            (Requested or Accepted or Arrived, Cancelled) => true,
            _ => false
        };

    public static bool IsLocationSharingActive(string status) =>
        status is Accepted or Arrived or InProgress;
}


/// <summary>
/// Immutable Driver report of money received directly from the Rider. This is not customer- or bank-verified.
/// Stored inside the ride aggregate so the report and ride share one Cosmos partition and ETag boundary.
/// </summary>
public sealed record RidePaymentReconciliation(
    string RideId,
    int AgreementVersion,
    string RiderId,
    string DriverId,
    string UnionId,
    decimal AgreedAmount,
    decimal AmountReceived,
    decimal Delta,
    string Currency,
    DateTimeOffset ReportedAtUtc,
    string ReportedByDriverId,
    string Status,
    string IdempotencyKeyHash);
