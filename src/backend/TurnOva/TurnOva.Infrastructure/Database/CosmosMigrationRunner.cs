using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database;

public sealed class CosmosMigrationRunner(
    CosmosClient client,
    CosmosDbOptions options,
    IEnumerable<ICosmosMigration> migrations,
    ILogger<CosmosMigrationRunner> logger)
{
    public const string HistoryContainerName = "MigrationHistory";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var databaseResponse = await client.CreateDatabaseIfNotExistsAsync(
            options.DatabaseName, cancellationToken: cancellationToken);
        var database = databaseResponse.Database;

        var historyResponse = await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(HistoryContainerName, "/id"),
            cancellationToken: cancellationToken);
        var history = historyResponse.Container;

        foreach (var migration in migrations.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var migrationId = migration.Id;
            try
            {
                await history.ReadItemAsync<MigrationHistoryDocument>(
                    migrationId, new PartitionKey(migrationId), cancellationToken: cancellationToken);
                logger.LogInformation("Cosmos migration {MigrationId} already applied; skipping.", migrationId);
                continue;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Migration has not been recorded; execute it below.
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
                // Another API instance completed the same idempotent migration concurrently.
            }

            logger.LogInformation("Cosmos migration {MigrationId} applied.", migration.Id);
        }
    }

    private sealed class MigrationHistoryDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTimeOffset AppliedAtUtc { get; set; }
        public string AppliedBy { get; set; } = string.Empty;
    }
}
