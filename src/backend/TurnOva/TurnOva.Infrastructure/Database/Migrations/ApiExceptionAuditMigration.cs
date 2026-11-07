using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class ApiExceptionAuditMigration : ICosmosMigration
{
    public string Id => "202611070001_ApiExceptionAudit";
    public string Description => "Create actor-partitioned audit records for authenticated unhandled API exceptions.";

    public async Task UpAsync(Microsoft.Azure.Cosmos.Database database, CancellationToken cancellationToken)
    {
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("ApiExceptionAudit", "/actorReference"),
            cancellationToken: cancellationToken);
    }
}
