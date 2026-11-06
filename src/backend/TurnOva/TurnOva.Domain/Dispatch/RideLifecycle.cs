namespace TurnOva.Domain.Dispatch;

public static class RideLifecycle
{
    public const string Requested = "Requested";
    public const string DriverSelected = "DriverSelected";
    public const string Negotiating = "Negotiating";
    public const string Accepted = "Accepted";
    public const string Arrived = "Arrived";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Declined = "Declined";
    public const string Expired = "Expired";

    public static bool CanTransition(string current, string next) =>
        (current, next) switch
        {
            (Requested, Accepted) => true,
            (DriverSelected, Negotiating) => true,
            (Accepted, Arrived) => true,
            (Arrived, InProgress) => true,
            (InProgress, Completed) => true,
            (Requested or Accepted or Arrived or DriverSelected or Negotiating, Cancelled) => true,
            (DriverSelected or Negotiating, Declined) => true,
            (DriverSelected or Negotiating, Expired) => true,
            _ => false
        };

    public static bool IsLocationSharingActive(string status) =>
        status is Accepted or Arrived or InProgress;
}
