namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class DriverPayslipsMigration : ICosmosMigration
{
    public string Id => "202611050003_DriverPayslips";
    public string Description => "Create driver work activity ledger and immutable monthly payslip containers.";

    public async Task UpAsync(ICosmosMigrationDatabase database, CancellationToken cancellationToken)
    {
        await database.CreateContainerIfNotExistsAsync(
            "DriverWorkActivities", "/driverId", cancellationToken);
        await database.CreateContainerIfNotExistsAsync(
            "DriverPayslips", "/driverId", cancellationToken);
    }
}
