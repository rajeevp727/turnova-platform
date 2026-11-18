namespace TurnOva.Domain.Dispatch;

/// <summary>
/// Immutable fare and route terms for one negotiation version. Confirmations are stored separately as append-only events.
/// </summary>
public sealed record RideAgreementVersion(
    string RideId,
    int Version,
    string RiderId,
    string DriverId,
    string UnionId,
    decimal Amount,
    string Currency,
    string Pickup,
    string Dropoff,
    IReadOnlyList<string> Stops,
    string ProposedBy,
    string IdempotencyKey,
    DateTimeOffset CreatedAtUtc,
    string? RequestHash = null);

public sealed record RideAgreementConfirmation(
    string RideId,
    int Version,
    string Party,
    string ActorId,
    DateTimeOffset ConfirmedAtUtc);
