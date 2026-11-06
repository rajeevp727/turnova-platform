using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class RideOffersMigration : ICosmosMigration
{
    public string Id => "202611060001_RideOffers";
    public string Description => "Create durable ride offer state storage.";

    public async Task UpAsync(Microsoft.Azure.Cosmos.Database database, CancellationToken cancellationToken)
    {
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("RideOffers", "/rideId"),
            cancellationToken: cancellationToken);
    }
}
