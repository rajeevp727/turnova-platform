using TurnOva.Domain.Dispatch;

namespace TurnOva.API;

public sealed record ExpiredNegotiationReconciliationResult(
    global::DemoRide? Ride,
    bool Attempted,
    bool Transitioned);

public static class ExpiredNegotiationReconciler
{
    public static async Task<ExpiredNegotiationReconciliationResult> ReconcileAsync(
        global::DemoRide current,
        DateTimeOffset now,
        Func<global::DemoRide, CancellationToken, Task<global::DemoRide?>> tryExpireAsync,
        Func<string, CancellationToken, Task<global::DemoRide?>> readLatestAsync,
        Func<global::DemoRide, CancellationToken, Task> releaseTerminalReservationAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(tryExpireAsync);
        ArgumentNullException.ThrowIfNull(readLatestAsync);
        ArgumentNullException.ThrowIfNull(releaseTerminalReservationAsync);

        if (current.Status is not (RideLifecycle.Negotiating or RideLifecycle.AgreementConfirmed) ||
            current.NegotiationExpiresAtUtc is not { } expiry ||
            expiry > now)
            return new(current, false, false);

        var transitioned = await tryExpireAsync(current, cancellationToken);
        if (transitioned is not null)
        {
            if (RideLifecycle.IsTerminal(transitioned.Status))
                await releaseTerminalReservationAsync(transitioned, cancellationToken);
            return new(transitioned, true, RideLifecycle.IsTerminal(transitioned.Status));
        }

        // A null compare-and-swap/ETag result is not proof of expiry. Re-read the
        // aggregate and release only if the latest authoritative version is terminal.
        var latest = await readLatestAsync(current.RideId, cancellationToken);
        if (latest is not null && RideLifecycle.IsTerminal(latest.Status))
            await releaseTerminalReservationAsync(latest, cancellationToken);

        return new(latest, true, false);
    }
    /// <summary>
    /// Resolves an already-observed active ride after its expiry reconciliation read.
    /// Only an authoritative terminal aggregate proves that the Driver is no longer blocked.
    /// Missing, mismatched or unrecognised latest state fails closed to the observed Driver.
    /// </summary>
    public static string? ResolveBlockingDriverIdAfterExpiry(
        ActiveDriverRideProjection candidate,
        global::DemoRide? latest,
        string? exceptRideId)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (string.Equals(candidate.RideId, exceptRideId, StringComparison.Ordinal))
            return null;

        if (latest is null ||
            !string.Equals(latest.RideId, candidate.RideId, StringComparison.Ordinal))
            return candidate.DriverId;

        if (RideLifecycle.IsTerminal(latest.Status))
            return null;

        if (RideLifecycle.IsActive(latest.Status) &&
            !string.IsNullOrWhiteSpace(latest.DriverId))
            return latest.DriverId;

        return candidate.DriverId;
    }

}
