using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class DriverPayslipsMigration : ICosmosMigration
{
    public string Id => "202611050003_DriverPayslips";
    public string Description => "Create driver work activity ledger and immutable monthly payslip containers.";

    public async Task UpAsync(Database database, CancellationToken cancellationToken)
    {
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("DriverWorkActivities", "/driverId"),
            cancellationToken: cancellationToken);
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("DriverPayslips", "/driverId"),
            cancellationToken: cancellationToken);
    }
}