namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class UnionManagementMigration : ICosmosMigration
{
    public string Id => "202611050002_UnionManagement";
    public string Description => "Create union, driver assignment and assignment audit containers.";

    public async Task UpAsync(ICosmosMigrationDatabase database, CancellationToken cancellationToken)
    {
        var containers = new (string Name, string PartitionKeyPath)[]
        {
            ("Unions", "/unionId"),
            ("DriverUnionAssignments", "/driverId"),
            ("DriverUnionAudit", "/driverId")
        };

        foreach (var container in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await database.CreateContainerIfNotExistsAsync(
                container.Name, container.PartitionKeyPath, cancellationToken);
        }
    }
}
