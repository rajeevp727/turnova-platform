namespace TurnOva.Infrastructure.Database;

public sealed record CosmosMigrationLease(
    string Id,
    string Owner,
    string ETag,
    DateTimeOffset ExpiresAtUtc);

public interface ICosmosMigrationLeaseStore
{
    Task<CosmosMigrationLease?> TryCreateAsync(
        string owner,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken);

    Task<CosmosMigrationLease?> ReadAsync(CancellationToken cancellationToken);

    Task<CosmosMigrationLease?> TryReplaceAsync(
        CosmosMigrationLease expected,
        string newOwner,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TryReleaseAsync(
        CosmosMigrationLease expected,
        CancellationToken cancellationToken);
}
