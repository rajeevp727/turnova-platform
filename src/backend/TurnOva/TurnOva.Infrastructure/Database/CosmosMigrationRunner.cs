using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace TurnOva.Infrastructure.Database;

public sealed class CosmosMigrationRunner(
    CosmosClient client,
    CosmosDbOptions options,
    IEnumerable<ICosmosMigration> migrations,
    CosmosMigrationLeaseCoordinator leaseCoordinator,
    ILogger<CosmosMigrationRunner> logger)
{
    public const string HistoryContainerName = "MigrationHistory";
    public const string LockContainerName = "MigrationLocks";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var databaseResponse = await client.CreateDatabaseIfNotExistsAsync(
            options.DatabaseName, throughput: 400, cancellationToken: cancellationToken);
        var database = databaseResponse.Database;

        var historyResponse = await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(HistoryContainerName, "/id"),
            cancellationToken: cancellationToken);
        var history = historyResponse.Container;

        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(LockContainerName, "/id"),
            cancellationToken: cancellationToken);

        var migrationDatabase = new CosmosMigrationDatabase(database);
        var lease = await leaseCoordinator.AcquireAsync(cancellationToken);
        var leaseState = new CosmosMigrationLeaseState(lease);
        using var migrationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewalTask = leaseCoordinator.RunRenewalLoopAsync(leaseState, migrationCancellation);

        try
        {
            foreach (var migration in migrations.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var migrationToken = migrationCancellation.Token;
                migrationToken.ThrowIfCancellationRequested();
                var migrationId = migration.Id;
                if (await IsAppliedAsync(history, migrationId, migrationToken))
                {
                    logger.LogInformation("Cosmos migration {MigrationId} already applied; skipping.", migrationId);
                    continue;
                }

                logger.LogInformation("Applying Cosmos migration {MigrationId}: {Description}",
                    migration.Id, migration.Description);
                await migration.UpAsync(migrationDatabase, migrationToken);

                // A migration may finish after its lease was fenced. Never publish its history
                // marker unless this runner still owns the renewed lease.
                migrationToken.ThrowIfCancellationRequested();

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
                        cancellationToken: migrationToken);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // A previously applied migration record is authoritative; verify it exists.
                    if (!await IsAppliedAsync(history, migrationId, migrationToken))
                        throw;
                }

                logger.LogInformation("Cosmos migration {MigrationId} applied.", migration.Id);
            }
        }
        finally
        {
            // Stop and await the heartbeat before releasing with its latest ETag.
            migrationCancellation.Cancel();
            await renewalTask;
            await leaseCoordinator.ReleaseAsync(leaseState);
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

    internal sealed class MigrationHistoryDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTimeOffset AppliedAtUtc { get; set; }
        public string AppliedBy { get; set; } = string.Empty;
    }
}
