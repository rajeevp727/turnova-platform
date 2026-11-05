namespace TurnOva.Domain.Dispatch;

public static class RideOfferStatuses
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";
}

public sealed record RideOfferAttempt(
    string OfferId,
    string DriverId,
    DateTimeOffset OfferedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status,
    DateTimeOffset? ResolvedAtUtc = null);

/// <summary>
/// One current offer aggregate per ride. Attempts are retained in this ride-partitioned
/// document so a new offer cannot overwrite the dispatch history.
/// </summary>
public sealed record RideOfferState(
    string RideId,
    string ZoneId,
    string ServiceClass,
    string CurrentOfferId,
    string DriverId,
    DateTimeOffset OfferedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status,
    IReadOnlyList<RideOfferAttempt> Attempts,
    string? ETag = null)
{
    public bool IsPending => string.Equals(Status, RideOfferStatuses.Pending, StringComparison.Ordinal);

    public bool IsExpiredAt(DateTimeOffset nowUtc) => IsPending && ExpiresAtUtc <= nowUtc;
}
