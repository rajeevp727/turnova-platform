using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class RideAgreementsMigration : ICosmosMigration
{
    public string Id => "202611060003_RideAgreements";
    public string Description => "Create append-only ride agreement version and confirmation containers.";

    public async Task UpAsync(Database database, CancellationToken cancellationToken)
    {
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("RideAgreementVersions", "/rideId"), cancellationToken: cancellationToken);
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("RideAgreementConfirmations", "/rideId"), cancellationToken: cancellationToken);
    }
}
