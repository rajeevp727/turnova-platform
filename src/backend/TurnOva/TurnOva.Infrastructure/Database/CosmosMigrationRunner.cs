using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using CosmosDatabase = Microsoft.Azure.Cosmos.Database;

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
        cancellationToken.ThrowIfCancellationRequested();
        var databaseResponse = await CosmosReadResponseGuard.AwaitCommittedWriteWithCancellationPrecedenceAsync(
            () => client.CreateDatabaseIfNotExistsAsync(
                options.DatabaseName, throughput: 400, cancellationToken: cancellationToken),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var database = CosmosReadResponseGuard.ReadResponseResource(
            databaseResponse,
            static response => response.Database,
            value => !string.IsNullOrWhiteSpace(options.DatabaseName) &&
                string.Equals(value.Id, options.DatabaseName, StringComparison.Ordinal),
            "migration database handle",
            cancellationToken,
            "Cosmos returned a migration database handle that does not match the configured database name after bootstrap.",
            "Cosmos returned an empty migration database handle after bootstrap.");

        var history = await CreateValidatedContainerAsync(
            database, HistoryContainerName, cancellationToken);

        // The lease store is constructed from this database during host setup. Validate
        // the bootstrap response here as well so a malformed lock-container response
        // cannot let migrations proceed against an unknown container identity/handle.
        _ = await CreateValidatedContainerAsync(
            database, LockContainerName, cancellationToken);

        var migrationDatabase = new CosmosMigrationDatabase(database);
        var lease = await leaseCoordinator.AcquireAsync(cancellationToken);
        var leaseState = new CosmosMigrationLeaseState(lease);
        using var migrationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewalTask = leaseCoordinator.RunRenewalLoopAsync(leaseState, migrationCancellation);

        try
        {
            migrationCancellation.Token.ThrowIfCancellationRequested();
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
                    _ = await CosmosReadResponseGuard.AwaitCommittedWriteWithCancellationPrecedenceAsync(
                        () => history.CreateItemAsync(
                            historyDocument, new PartitionKey(historyDocument.Id),
                            cancellationToken: migrationToken),
                        migrationToken);
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
            // Stop the heartbeat before releasing with its latest ETag. A migration
            // cancellation callback or heartbeat failure must not skip lease release.
            try
            {
                migrationCancellation.Cancel();
            }
            finally
            {
                try
                {
                    await renewalTask;
                }
                finally
                {
                    await leaseCoordinator.ReleaseAsync(leaseState);
                }
            }
        }
    }

    private static async Task<Container> CreateValidatedContainerAsync(
        CosmosDatabase database,
        string containerName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string partitionKeyPath = "/id";
        var response = await CosmosReadResponseGuard.AwaitCommittedWriteWithCancellationPrecedenceAsync(
            () => database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(containerName, partitionKeyPath),
                cancellationToken: cancellationToken),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var properties = CosmosReadResponseGuard.ReadResponseResource(
            response,
            static containerResponse => containerResponse.Resource,
            value => string.Equals(value.Id, containerName, StringComparison.Ordinal) &&
                string.Equals(value.PartitionKeyPath, partitionKeyPath, StringComparison.Ordinal),
            "migration container",
            cancellationToken,
            "Cosmos returned a migration container resource that does not match the requested name and partition-key path after bootstrap.",
            "Cosmos returned an empty migration container resource after bootstrap.");

        return CosmosReadResponseGuard.ReadResponseResource(
            response,
            static containerResponse => containerResponse.Container,
            value => string.Equals(value.Id, containerName, StringComparison.Ordinal),
            "migration container handle",
            cancellationToken,
            "Cosmos returned a migration container handle that does not match the requested name after bootstrap.",
            "Cosmos returned an empty migration container handle after bootstrap.");
    }

    private static async Task<bool> IsAppliedAsync(
        Container history, string migrationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var response = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => history.ReadItemAsync<MigrationHistoryDocument>(
                    migrationId, new PartitionKey(migrationId), cancellationToken: cancellationToken),
                cancellationToken);
            _ = CosmosReadResponseGuard.ReadResource(
                response,
                marker => string.Equals(marker.Id, migrationId, StringComparison.Ordinal),
                "migration history marker",
                cancellationToken,
                "Cosmos returned a migration history marker that does not match the requested ID.",
                "Cosmos returned a migration history marker that does not match the requested ID.");

            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
