using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class UnionManagementMigration : ICosmosMigration
{
    public string Id => "202611050002_UnionManagement";
    public string Description => "Create union, driver assignment and assignment audit containers.";

    public async Task UpAsync(Microsoft.Azure.Cosmos.Database database, CancellationToken cancellationToken)
    {
        var containers = new[]
        {
            new ContainerProperties("Unions", "/unionId"),
            new ContainerProperties("DriverUnionAssignments", "/driverId"),
            new ContainerProperties("DriverUnionAudit", "/driverId")
        };

        foreach (var properties in containers)
        {
            await database.CreateContainerIfNotExistsAsync(
                properties, cancellationToken: cancellationToken);
        }
    }
}