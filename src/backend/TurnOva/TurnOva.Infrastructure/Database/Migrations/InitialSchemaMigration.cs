using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class InitialSchemaMigration : ICosmosMigration
{
    public string Id => "202611050001_InitialSchema";
    public string Description => "Create the initial TurnOva MVP containers.";

    public async Task UpAsync(Database database, CancellationToken cancellationToken)
    {
        var containers = new[]
        {
            new ContainerProperties("Drivers", "/driverId"),
            new ContainerProperties("Riders", "/riderId"),
            new ContainerProperties("Zones", "/zoneId"),
            new ContainerProperties("QueueEntries", "/zoneServiceKey"),
            new ContainerProperties("Rides", "/rideId"),
            new ContainerProperties("RideIdempotency", "/riderId")
        };

        foreach (var properties in containers)
        {
            await database.CreateContainerIfNotExistsAsync(
                properties, cancellationToken: cancellationToken);
        }
    }
}
