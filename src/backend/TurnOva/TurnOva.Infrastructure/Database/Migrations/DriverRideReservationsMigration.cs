namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class DriverRideReservationsMigration : ICosmosMigration
{
    public string Id => "202611060002_DriverRideReservations";
    public string Description => "Create per-driver active ride reservation storage.";

    public Task UpAsync(ICosmosMigrationDatabase database, CancellationToken cancellationToken) =>
        database.CreateContainerIfNotExistsAsync("DriverRideReservations", "/driverId", cancellationToken);
}
