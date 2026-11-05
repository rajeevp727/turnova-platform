using Microsoft.Azure.Cosmos;
namespace TurnOva.Infrastructure.Database.Migrations;
public sealed class DriverRideReservationsMigration : ICosmosMigration
{
    public string Id => "202611060002_DriverRideReservations";
    public string Description => "Create per-driver active ride reservation storage.";
    public async Task UpAsync(Database database, CancellationToken cancellationToken)
    {
        await database.CreateContainerIfNotExistsAsync(new ContainerProperties("DriverRideReservations", "/driverId"),
            cancellationToken: cancellationToken);
    }
}