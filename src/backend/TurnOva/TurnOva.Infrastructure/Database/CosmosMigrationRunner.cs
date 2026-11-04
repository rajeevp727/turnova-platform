using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace TurnOva.Infrastructure.Database;

public sealed class CosmosMigrationRunner(
    CosmosClient client,
    CosmosDbOptions options,
    IEnumerable<ICosmosMigration> migrations,
    ILogger<CosmosMigrationRunner> logger)
{
    public const string HistoryContainerName = "MigrationHistory";
    public const string LockContainerName = "MigrationLocks";
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromSeconds(2);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var databaseResponse = await client.CreateDatabaseIfNotExistsAsync(
            options.DatabaseName, throughput: 400, cancellationToken: cancellationToken);
        var database = databaseResponse.Database;

        var historyResponse = await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(HistoryContainerName, "/id"),
            cancellationToken: cancellationToken);
        var history = historyResponse.Container;

        var lockResponse = await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(LockContainerName, "/id"),
            cancellationToken: cancellationToken);
        var locks = lockResponse.Container;

        var lease = await AcquireLockAsync(locks, cancellationToken);
        try
        {
            foreach (var migration in migrations.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var migrationId = migration.Id;
                if (await IsAppliedAsync(history, migrationId, cancellationToken))
                {
                    logger.LogInformation("Cosmos migration {MigrationId} already applied; skipping.", migrationId);
                    continue;
                }

                logger.LogInformation("Applying Cosmos migration {MigrationId}: {Description}",
                    migration.Id, migration.Description);
                await migration.UpAsync(database, cancellationToken);

                var historyDocument = new MigrationHistoryDocument
                {
                    Id = migration.Id,
                    Description = migration.Description,
                    AppliedAtUtc = DateTimeOffset.UtcNow,
                    AppliedBy = "TurnOva.API"
                };

                try
                {
                    await history.CreateItemAsync(
                        historyDocument, new PartitionKey(historyDocument.Id),
                        cancellationToken: cancellationToken);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // A previously applied migration record is authoritative; verify it exists.
                    if (!await IsAppliedAsync(history, migrationId, cancellationToken))
                        throw;
                }

                logger.LogInformation("Cosmos migration {MigrationId} applied.", migration.Id);
            }
        }
        finally
        {
            await ReleaseLockAsync(locks, lease);
        }
    }

    private async Task<MigrationLockLease> AcquireLockAsync(
        Container locks, CancellationToken cancellationToken)
    {
        var owner = Guid.NewGuid().ToString("N");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = new MigrationLockDocument
            {
                Id = "schema-migrations",
                Owner = owner,
                ExpiresAtUtc = DateTimeOffset.UtcNow.Add(LockDuration)
            };

            try
            {
                var created = await locks.CreateItemAsync(
                    lease, new PartitionKey(lease.Id), cancellationToken: cancellationToken);
                logger.LogInformation("Acquired Cosmos migration lock {Owner}.", owner);
                return new MigrationLockLease(lease.Id, owner, created.ETag);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var current = await ReadLockAsync(locks, cancellationToken);
                if (current is null)
                    continue;

                if (current.Document.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                {
                    try
                    {
                        var replacement = lease;
                        var response = await locks.ReplaceItemAsync(
                            replacement, replacement.Id, new PartitionKey(replacement.Id),
                            new ItemRequestOptions { IfMatchEtag = current.ETag },
                            cancellationToken);
                        logger.LogWarning("Recovered expired Cosmos migration lock owned by {PreviousOwner}.", current.Document.Owner);
                        return new MigrationLockLease(replacement.Id, owner, response.ETag);
                    }
                    catch (CosmosException replaceException) when (
                        replaceException.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                    {
                        continue;
                    }
                }

                await Task.Delay(LockRetryDelay, cancellationToken);
            }
        }
    }

    private static async Task<MigrationLockReadResult?> ReadLockAsync(
        Container locks, CancellationToken cancellationToken)
    {
        try
        {
            var response = await locks.ReadItemAsync<MigrationLockDocument>(
                "schema-migrations", new PartitionKey("schema-migrations"),
                cancellationToken: cancellationToken);
            return new MigrationLockReadResult(response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<bool> IsAppliedAsync(
        Container history, string migrationId, CancellationToken cancellationToken)
    {
        try
        {
            await history.ReadItemAsync<MigrationHistoryDocument>(
                migrationId, new PartitionKey(migrationId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static async Task ReleaseLockAsync(
        Container locks, MigrationLockLease lease)
    {
        try
        {
            await locks.DeleteItemAsync<MigrationLockDocument>(
                lease.Id, new PartitionKey(lease.Id),
                new ItemRequestOptions { IfMatchEtag = lease.ETag },
                CancellationToken.None);
        }
        catch (CosmosException ex) when (
            ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        {
            // The lease expired or was replaced; never delete another instance's lock.
        }
    }

    private sealed class MigrationHistoryDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTimeOffset AppliedAtUtc { get; set; }
        public string AppliedBy { get; set; } = string.Empty;
    }

    private sealed record MigrationLockDocument
    {
        public string Id { get; init; } = string.Empty;
        public string Owner { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
        [JsonIgnore] public string? ETag { get; init; }
    }
}
