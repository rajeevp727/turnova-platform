namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class RideIdempotencyMigration : ICosmosMigration
{
    public string Id => "202611050002_RideIdempotency";
    public string Description => "Create the rider-partitioned ride idempotency container.";

    public Task UpAsync(ICosmosMigrationDatabase database, CancellationToken cancellationToken) =>
        database.CreateContainerIfNotExistsAsync("RideIdempotency", "/riderId", cancellationToken);
}
