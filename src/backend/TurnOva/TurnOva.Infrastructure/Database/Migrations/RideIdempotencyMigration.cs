using Microsoft.Azure.Cosmos;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class RideIdempotencyMigration : ICosmosMigration
{
    public string Id => "202611050002_RideIdempotency";
    public string Description => "Create the rider-partitioned ride idempotency container.";

    public async Task UpAsync(Database database, CancellationToken cancellationToken)
    {
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("RideIdempotency", "/riderId"),
            cancellationToken: cancellationToken);
    }
}
