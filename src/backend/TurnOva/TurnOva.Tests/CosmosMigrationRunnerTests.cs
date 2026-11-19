using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Tests;

public sealed class CosmosMigrationRunnerTests
{
    [Fact]
    public async Task LeaseRenewalLossCancelsMigrationBeforeHistoryMarkerIsWritten()
    {
        var migrationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var migration = new BlockingMigration(migrationStarted);
        var leaseStore = new LeaseLostStore();
        var delay = new ControlledDelay();
        var coordinator = new CosmosMigrationLeaseCoordinator(
            leaseStore,
            TimeProvider.System,
            delay,
            new CosmosMigrationLeaseOptions(
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMilliseconds(1)),
            NullLogger<CosmosMigrationLeaseCoordinator>.Instance);
        var (client, historyContainer) = CreateCosmosClient();
        var runner = new CosmosMigrationRunner(
            client,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [migration],
            coordinator,
            NullLogger<CosmosMigrationRunner>.Instance);

        var run = runner.RunAsync();

        await migrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await delay.WaitUntilRequestedAsync().WaitAsync(TimeSpan.FromSeconds(5));
        delay.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run);

        historyContainer.Verify(container => container.CreateItemAsync<It.IsAnyType>(
            It.IsAny<It.IsAnyType>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(leaseStore.ReleasedLease);
        Assert.Equal("schema-migrations", leaseStore.ReleasedLease!.Id);
        Assert.Equal("lease-etag-1", leaseStore.ReleasedLease.ETag);
    }

    private static (CosmosClient Client, Mock<Container> HistoryContainer) CreateCosmosClient()
    {
        var client = new Mock<CosmosClient>();
        var database = new Mock<Database>();
        var databaseResponse = new Mock<DatabaseResponse>();
        var historyContainer = new Mock<Container>();
        var containerResponse = new Mock<ContainerResponse>();

        databaseResponse.SetupGet(response => response.Database).Returns(database.Object);
        containerResponse.SetupGet(response => response.Container).Returns(historyContainer.Object);

        client.Setup(value => value.CreateDatabaseIfNotExistsAsync(
                "TurnOvaMigrationTest",
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(databaseResponse.Object);

        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerResponse.Object);

        historyContainer.Setup(value => value.ReadItemAsync<It.IsAnyType>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "migration history item not found",
                HttpStatusCode.NotFound,
                0,
                "migration-history-test",
                0));

        return (client.Object, historyContainer);
    }

    private sealed class BlockingMigration(TaskCompletionSource started) : ICosmosMigration
    {
        public string Id => "test-blocking-migration";
        public string Description => "Blocks until the lease is lost.";

        public async Task UpAsync(
            ICosmosMigrationDatabase database,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class LeaseLostStore : ICosmosMigrationLeaseStore
    {
        public CosmosMigrationLease? ReleasedLease { get; private set; }

        public Task<CosmosMigrationLease?> TryCreateAsync(
            string owner,
            DateTimeOffset expiresAtUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<CosmosMigrationLease?>(
                new CosmosMigrationLease("schema-migrations", owner, "lease-etag-1", expiresAtUtc));

        public Task<CosmosMigrationLease?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CosmosMigrationLease?>(null);

        public Task<CosmosMigrationLease?> TryReplaceAsync(
            CosmosMigrationLease expected,
            string newOwner,
            DateTimeOffset expiresAtUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<CosmosMigrationLease?>(null);

        public Task<bool> TryReleaseAsync(
            CosmosMigrationLease expected,
            CancellationToken cancellationToken)
        {
            ReleasedLease = expected;
            return Task.FromResult(true);
        }
    }

    private sealed class ControlledDelay : ICosmosMigrationDelay
    {
        private readonly TaskCompletionSource requested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilRequestedAsync() => requested.Task;

        public void Release() => released.TrySetResult();

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            requested.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }
    }
}
