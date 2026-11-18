namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class RideOffersMigration : ICosmosMigration
{
    public string Id => "202611060001_RideOffers";
    public string Description => "Create durable ride offer state storage.";

    public Task UpAsync(ICosmosMigrationDatabase database, CancellationToken cancellationToken) =>
        database.CreateContainerIfNotExistsAsync("RideOffers", "/rideId", cancellationToken);
}
