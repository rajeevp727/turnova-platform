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
        var migrationStarted = NewSignal();
        var migrationCancellationObserved = NewSignal();
        var completeMigration = NewSignal();
        var migration = new BlockingMigration(
            migrationStarted,
            migrationCancellationObserved,
            completeMigration);
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
        var first = await Task.WhenAny(migrationStarted.Task, run)
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (first == run)
        {
            await run;
            Assert.Fail("The migration runner completed before starting the test migration.");
        }

        await delay.WaitUntilRequestedAsync().WaitAsync(TimeSpan.FromSeconds(5));
        delay.Release();
        await migrationCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Model an in-flight migration operation that cannot stop immediately when its
        // token is cancelled. The runner must re-check ownership after it returns and
        // before it writes the MigrationHistory marker.
        migration.Complete();

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

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    private sealed class BlockingMigration(
        TaskCompletionSource started,
        TaskCompletionSource cancellationObserved,
        TaskCompletionSource completed) : ICosmosMigration
    {
        public string Id => "test-blocking-migration";
        public string Description => "Completes after its lease is lost.";

        public async Task UpAsync(
            ICosmosMigrationDatabase database,
            CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(
                () => cancellationObserved.TrySetResult());
            started.TrySetResult();

            // Deliberately ignore cancellation until the test releases this in-flight
            // operation so the runner's post-migration ownership check is exercised.
            await completed.Task;
        }

        public void Complete() => completed.TrySetResult();
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
        private readonly TaskCompletionSource requested = NewSignal();
        private readonly TaskCompletionSource released = NewSignal();

        public Task WaitUntilRequestedAsync() => requested.Task;

        public void Release() => released.TrySetResult();

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            requested.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }
    }
}
