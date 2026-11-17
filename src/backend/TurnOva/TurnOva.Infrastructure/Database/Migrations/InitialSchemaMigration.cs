namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class InitialSchemaMigration : ICosmosMigration
{
    public string Id => "202611050001_InitialSchema";
    public string Description => "Create the initial TurnOva MVP containers.";

    public async Task UpAsync(ICosmosMigrationDatabase database, CancellationToken cancellationToken)
    {
        var containers = new (string Name, string PartitionKeyPath)[]
        {
            ("Drivers", "/driverId"),
            ("Riders", "/riderId"),
            ("Zones", "/zoneId"),
            ("QueueEntries", "/zoneServiceKey"),
            ("Rides", "/rideId"),
            ("RideIdempotency", "/riderId")
        };

        foreach (var container in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await database.CreateContainerIfNotExistsAsync(
                container.Name, container.PartitionKeyPath, cancellationToken);
        }
    }
}
