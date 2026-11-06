namespace TurnOva.Domain.Dispatch;

public static class RideLifecycle
{
    public const string Requested = "Requested";
    public const string Negotiating = "Negotiating";
    public const string Expired = "Expired";
    public const string Accepted = "Accepted";
    public const string Arrived = "Arrived";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static bool CanTransition(string current, string next) =>
        (current, next) switch
        {
            (Requested, Accepted) => true,
            (Negotiating, Cancelled or Expired) => true,
            (Accepted, Arrived) => true,
            (Arrived, InProgress) => true,
            (InProgress, Completed) => true,
            (Requested or Accepted or Arrived, Cancelled) => true,
            _ => false
        };

    public static bool IsLocationSharingActive(string status) =>
        status is Accepted or Arrived or InProgress;
}
