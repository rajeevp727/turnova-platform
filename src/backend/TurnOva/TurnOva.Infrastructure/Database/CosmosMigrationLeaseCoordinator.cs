using Microsoft.Extensions.Logging;

namespace TurnOva.Infrastructure.Database;

public sealed record CosmosMigrationLeaseOptions(
    TimeSpan LeaseDuration,
    TimeSpan RenewalInterval,
    TimeSpan RetryDelay)
{
    public static CosmosMigrationLeaseOptions Default { get; } = new(
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromSeconds(2));

    public void Validate()
    {
        if (LeaseDuration <= TimeSpan.Zero ||
            RenewalInterval <= TimeSpan.Zero ||
            RenewalInterval >= LeaseDuration ||
            RetryDelay <= TimeSpan.Zero)
            throw new InvalidOperationException("Cosmos migration lease timing configuration is invalid.");
    }
}

public interface ICosmosMigrationDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemCosmosMigrationDelay : ICosmosMigrationDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class CosmosMigrationLeaseState(CosmosMigrationLease initialLease)
{
    private readonly object gate = new();
    private CosmosMigrationLease current = initialLease;

    public CosmosMigrationLease Current
    {
        get
        {
            lock (gate) return current;
        }
    }

    public void Update(CosmosMigrationLease renewedLease)
    {
        lock (gate)
        {
            if (!string.Equals(current.Id, renewedLease.Id, StringComparison.Ordinal) ||
                !string.Equals(current.Owner, renewedLease.Owner, StringComparison.Ordinal))
                throw new InvalidOperationException("A migration lease renewal cannot change its ID or owner.");

            current = renewedLease;
        }
    }
}

public sealed class CosmosMigrationLeaseLostException(string owner)
    : InvalidOperationException($"Cosmos migration lease ownership was lost for owner '{owner}'.");

public sealed class CosmosMigrationLeaseCoordinator(
    ICosmosMigrationLeaseStore store,
    TimeProvider timeProvider,
    ICosmosMigrationDelay delay,
    CosmosMigrationLeaseOptions options,
    ILogger<CosmosMigrationLeaseCoordinator> logger)
{
    public async Task<CosmosMigrationLease> AcquireAsync(CancellationToken cancellationToken)
    {
        options.Validate();
        var owner = Guid.NewGuid().ToString("N");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = timeProvider.GetUtcNow();
            var expiresAtUtc = now.Add(options.LeaseDuration);
            var created = await store.TryCreateAsync(owner, expiresAtUtc, cancellationToken);
            if (created is not null)
            {
                logger.LogInformation("Acquired Cosmos migration lease for owner {Owner}.", owner);
                return created;
            }

            var current = await store.ReadAsync(cancellationToken);
            if (current is null)
            {
                // The conflicting lease may have been released between Create and Read.
                // Back off before retrying instead of spinning on a disappearing lease.
                await delay.DelayAsync(options.RetryDelay, cancellationToken);
                continue;
            }

            if (current.ExpiresAtUtc <= now)
            {
                var recovered = await store.TryReplaceAsync(
                    current, owner, expiresAtUtc, cancellationToken);
                if (recovered is not null)
                {
                    logger.LogWarning(
                        "Recovered expired Cosmos migration lease from owner {PreviousOwner}.",
                        current.Owner);
                    return recovered;
                }

                continue;
            }

            await delay.DelayAsync(options.RetryDelay, cancellationToken);
        }
    }

    public async Task<CosmosMigrationLease> RenewOnceAsync(
        CosmosMigrationLease current,
        CancellationToken cancellationToken)
    {
        var renewed = await store.TryReplaceAsync(
            current,
            current.Owner,
            timeProvider.GetUtcNow().Add(options.LeaseDuration),
            cancellationToken);

        return renewed ?? throw new CosmosMigrationLeaseLostException(current.Owner);
    }

    public async Task RunRenewalLoopAsync(
        CosmosMigrationLeaseState state,
        CancellationTokenSource migrationCancellation)
    {
        var cancellationToken = migrationCancellation.Token;
        try
        {
            while (true)
            {
                await delay.DelayAsync(options.RenewalInterval, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var renewed = await RenewOnceAsync(state.Current, cancellationToken);
                state.Update(renewed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown after migrations finish or the host is stopping.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cosmos migration lease renewal failed; cancelling migration execution.");
            if (!migrationCancellation.IsCancellationRequested)
                migrationCancellation.Cancel();
        }
    }

    public async Task ReleaseAsync(CosmosMigrationLeaseState state)
    {
        var current = state.Current;
        try
        {
            if (!await store.TryReleaseAsync(current, CancellationToken.None))
                logger.LogWarning(
                    "Cosmos migration lease {Owner} was already expired or replaced; it was not deleted.",
                    current.Owner);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to release Cosmos migration lease {Owner}.", current.Owner);
        }
    }
}
