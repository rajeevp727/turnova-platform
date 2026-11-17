using TurnOva.Infrastructure.Database;
using TurnOva.Infrastructure.Database.Migrations;

namespace TurnOva.Tests;

public sealed class CosmosMigrationContractTests
{
    [Fact]
    public void MigrationIdsAreUnique()
    {
        ICosmosMigration[] migrations =
        [
            new InitialSchemaMigration(),
            new RideIdempotencyMigration(),
            new UnionManagementMigration(),
            new DriverPayslipsMigration(),
            new RideOffersMigration(),
            new DriverRideReservationsMigration(),
            new SupportRequestsMigration()
        ];

        Assert.Equal(migrations.Length, migrations.Select(migration => migration.Id)
            .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task InitialSchemaDeclaresExpectedContainersAndPartitionKeys()
    {
        await AssertContainers(new InitialSchemaMigration(),
        [
            ("Drivers", "/driverId"),
            ("Riders", "/riderId"),
            ("Zones", "/zoneId"),
            ("QueueEntries", "/zoneServiceKey"),
            ("Rides", "/rideId"),
            ("RideIdempotency", "/riderId")
        ]);
    }

    [Fact]
    public async Task UnionMigrationDeclaresExpectedContainersAndPartitionKeys()
    {
        await AssertContainers(new UnionManagementMigration(),
        [
            ("Unions", "/unionId"),
            ("DriverUnionAssignments", "/driverId"),
            ("DriverUnionAudit", "/driverId")
        ]);
    }

    [Fact]
    public async Task PayslipMigrationDeclaresExpectedContainersAndPartitionKeys()
    {
        await AssertContainers(new DriverPayslipsMigration(),
        [
            ("DriverWorkActivities", "/driverId"),
            ("DriverPayslips", "/driverId")
        ]);
    }

    [Fact]
    public async Task RideOfferAndReservationMigrationsDeclareExpectedContainers()
    {
        await AssertContainers(new RideOffersMigration(), [("RideOffers", "/rideId")]);
        await AssertContainers(new DriverRideReservationsMigration(), [("DriverRideReservations", "/driverId")]);
        await AssertContainers(new RideIdempotencyMigration(), [("RideIdempotency", "/riderId")]);
    }

    [Fact]
    public async Task SupportRequestsMigrationIsReporterPartitioned()
    {
        await AssertContainers(new SupportRequestsMigration(), [("SupportRequests", "/reporterId")]);
    }

    [Fact]
    public async Task MigrationsHonorCancellationBeforeCreatingFurtherContainers()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var database = new RecordingMigrationDatabase();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new InitialSchemaMigration().UpAsync(database, cancellation.Token));

        Assert.Empty(database.Containers);
    }

    private static async Task AssertContainers(
        ICosmosMigration migration,
        IReadOnlyList<(string Name, string PartitionKeyPath)> expected)
    {
        var database = new RecordingMigrationDatabase();
        await migration.UpAsync(database, CancellationToken.None);
        Assert.Equal(expected, database.Containers);
    }

    private sealed class RecordingMigrationDatabase : ICosmosMigrationDatabase
    {
        public List<(string Name, string PartitionKeyPath)> Containers { get; } = [];

        public Task CreateContainerIfNotExistsAsync(
            string containerName,
            string partitionKeyPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Containers.Add((containerName, partitionKeyPath));
            return Task.CompletedTask;
        }
    }
}
