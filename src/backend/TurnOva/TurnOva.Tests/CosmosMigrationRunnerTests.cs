using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Tests;

public sealed class CosmosMigrationRunnerTests
{
    [Fact]
    public async Task PreCancelledTokenStopsBeforeDatabaseBootstrap()
    {
        var client = new Mock<CosmosClient>(MockBehavior.Strict);
        var leaseStore = new Mock<ICosmosMigrationLeaseStore>(MockBehavior.Strict);
        var runner = new CosmosMigrationRunner(
            client.Object,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [],
            CreateCoordinator(leaseStore.Object, new ControlledDelay()),
            NullLogger<CosmosMigrationRunner>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        client.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationAfterDatabaseCreateStopsBeforeHistoryContainerCreation()
    {
        var client = new Mock<CosmosClient>(MockBehavior.Strict);
        var database = new Mock<Database>(MockBehavior.Strict);
        var databaseResponse = new Mock<DatabaseResponse>();
        databaseResponse.SetupGet(value => value.Database).Returns(database.Object);
        using var cancellation = new CancellationTokenSource();
        client.Setup(value => value.CreateDatabaseIfNotExistsAsync(
                "TurnOvaMigrationTest", It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(databaseResponse.Object);
            });
        var leaseStore = new Mock<ICosmosMigrationLeaseStore>(MockBehavior.Strict);
        var runner = new CosmosMigrationRunner(
            client.Object,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [],
            CreateCoordinator(leaseStore.Object, new ControlledDelay()),
            NullLogger<CosmosMigrationRunner>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        client.Verify(value => value.CreateDatabaseIfNotExistsAsync(
            "TurnOvaMigrationTest", It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationAfterHistoryContainerCreateStopsBeforeLockContainerCreation()
    {
        var client = new Mock<CosmosClient>(MockBehavior.Strict);
        var database = new Mock<Database>(MockBehavior.Strict);
        var databaseResponse = new Mock<DatabaseResponse>();
        databaseResponse.SetupGet(value => value.Database).Returns(database.Object);
        var containerResponse = new Mock<ContainerResponse>();
        containerResponse.SetupGet(value => value.Container).Returns(new Mock<Container>().Object);
        using var cancellation = new CancellationTokenSource();
        client.Setup(value => value.CreateDatabaseIfNotExistsAsync(
                "TurnOvaMigrationTest", It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(databaseResponse.Object);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(containerResponse.Object);
            });
        var leaseStore = new Mock<ICosmosMigrationLeaseStore>(MockBehavior.Strict);
        var runner = new CosmosMigrationRunner(
            client.Object,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [],
            CreateCoordinator(leaseStore.Object, new ControlledDelay()),
            NullLogger<CosmosMigrationRunner>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationAfterLockContainerCreateStopsBeforeLeaseAcquisition()
    {
        var client = new Mock<CosmosClient>(MockBehavior.Strict);
        var database = new Mock<Database>(MockBehavior.Strict);
        var databaseResponse = new Mock<DatabaseResponse>();
        databaseResponse.SetupGet(value => value.Database).Returns(database.Object);
        var containerResponse = new Mock<ContainerResponse>();
        containerResponse.SetupGet(value => value.Container).Returns(new Mock<Container>().Object);
        using var cancellation = new CancellationTokenSource();
        var containerCreateCount = 0;
        client.Setup(value => value.CreateDatabaseIfNotExistsAsync(
                "TurnOvaMigrationTest", It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(databaseResponse.Object);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                containerCreateCount++;
                if (containerCreateCount == 2)
                    cancellation.Cancel();
                return Task.FromResult(containerResponse.Object);
            });
        var leaseStore = new Mock<ICosmosMigrationLeaseStore>(MockBehavior.Strict);
        var runner = new CosmosMigrationRunner(
            client.Object,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [],
            CreateCoordinator(leaseStore.Object, new ControlledDelay()),
            NullLogger<CosmosMigrationRunner>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        Assert.Equal(2, containerCreateCount);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationAfterLeaseAcquisitionStillReleasesWhenThereAreNoMigrations()
    {
        var (client, _) = CreateCosmosClient();
        using var cancellation = new CancellationTokenSource();
        var leaseStore = new CancelAfterCreateLeaseStore(cancellation);
        var runner = new CosmosMigrationRunner(
            client,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [],
            CreateCoordinator(leaseStore, new ControlledDelay()),
            NullLogger<CosmosMigrationRunner>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.NotNull(leaseStore.ReleasedLease);
        Assert.Equal("schema-migrations", leaseStore.ReleasedLease!.Id);
    }

    [Fact]
    public async Task CancellationAfterSuccessfulHistoryReadDoesNotSkipAsApplied()
    {
        var (client, historyContainer) = CreateCosmosClient();
        using var cancellation = new CancellationTokenSource();
        historyContainer.Setup(container => container.ReadItemAsync<CosmosMigrationRunner.MigrationHistoryDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(Mock.Of<ItemResponse<CosmosMigrationRunner.MigrationHistoryDocument>>());
            });
        var leaseStore = new LeaseLostStore();
        var migration = new CountingMigration();
        var runner = new CosmosMigrationRunner(
            client,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [migration],
            CreateCoordinator(leaseStore, new ControlledDelay()),
            NullLogger<CosmosMigrationRunner>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(0, migration.CallCount);
        Assert.NotNull(leaseStore.ReleasedLease);
    }

    [Fact]
    public async Task MissingHistoryReadDoesNotHideCancellationOrStartMigration()
    {
        var (client, historyContainer) = CreateCosmosClient();
        using var cancellation = new CancellationTokenSource();
        var missing = new CosmosException(
            "migration history item disappeared", HttpStatusCode.NotFound, 0, "migration-history-test", 0);
        historyContainer.Setup(container => container.ReadItemAsync<CosmosMigrationRunner.MigrationHistoryDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<CosmosMigrationRunner.MigrationHistoryDocument>>(missing);
            });
        var leaseStore = new LeaseLostStore();
        var migration = new CountingMigration();
        var runner = new CosmosMigrationRunner(
            client,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [migration],
            CreateCoordinator(leaseStore, new ControlledDelay()),
            NullLogger<CosmosMigrationRunner>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(0, migration.CallCount);
        Assert.NotNull(leaseStore.ReleasedLease);
    }

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

        historyContainer.Verify(container => container.CreateItemAsync<CosmosMigrationRunner.MigrationHistoryDocument>(
            It.IsAny<CosmosMigrationRunner.MigrationHistoryDocument>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(leaseStore.ReleasedLease);
        Assert.Equal("schema-migrations", leaseStore.ReleasedLease!.Id);
        Assert.Equal("lease-etag-1", leaseStore.ReleasedLease.ETag);
    }

    [Fact]
    public async Task LeaseReleaseStillRunsWhenMigrationCancellationCallbackThrowsDuringShutdown()
    {
        var migrationStarted = NewSignal();
        var cancellationObserved = NewSignal();
        var completeMigration = NewSignal();
        var migration = new CancellationCallbackThrowingMigration(
            migrationStarted, cancellationObserved, completeMigration);
        var leaseStore = new LeaseLostStore();
        var delay = new ShutdownControlledDelay();
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
        historyContainer.Setup(container => container.CreateItemAsync(
                It.IsAny<CosmosMigrationRunner.MigrationHistoryDocument>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<ItemResponse<CosmosMigrationRunner.MigrationHistoryDocument>>(null!));

        var runner = new CosmosMigrationRunner(
            client,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [migration],
            coordinator,
            NullLogger<CosmosMigrationRunner>.Instance);

        var run = runner.RunAsync();
        await migrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await delay.WaitUntilRequestedAsync().WaitAsync(TimeSpan.FromSeconds(5));
        migration.Complete();

        var exceptionTask = Record.ExceptionAsync(async () =>
            await run.WaitAsync(TimeSpan.FromSeconds(5)));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        delay.Release();
        var exception = await exceptionTask;

        Assert.NotNull(exception);
        Assert.Contains("test cancellation callback failure", exception.ToString());
        historyContainer.Verify(container => container.CreateItemAsync<CosmosMigrationRunner.MigrationHistoryDocument>(
            It.IsAny<CosmosMigrationRunner.MigrationHistoryDocument>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(leaseStore.ReleasedLease);
        Assert.Equal("schema-migrations", leaseStore.ReleasedLease!.Id);
        Assert.Equal("lease-etag-1", leaseStore.ReleasedLease.ETag);
    }

    private static CosmosMigrationLeaseCoordinator CreateCoordinator(
        ICosmosMigrationLeaseStore store,
        ICosmosMigrationDelay delay) =>
        new(
            store,
            TimeProvider.System,
            delay,
            new CosmosMigrationLeaseOptions(
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMilliseconds(1)),
            NullLogger<CosmosMigrationLeaseCoordinator>.Instance);

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

        historyContainer.Setup(value => value.ReadItemAsync<CosmosMigrationRunner.MigrationHistoryDocument>(
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

    private sealed class CancellationCallbackThrowingMigration(
        TaskCompletionSource started,
        TaskCompletionSource cancellationObserved,
        TaskCompletionSource completed) : ICosmosMigration
    {
        public string Id => "test-cancellation-callback";
        public string Description => "Throws from a cancellation callback during shutdown.";

        public async Task UpAsync(
            ICosmosMigrationDatabase database,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken.Register(() =>
            {
                cancellationObserved.TrySetResult();
                throw new InvalidOperationException("test cancellation callback failure");
            });
            started.TrySetResult();
            await completed.Task;
        }

        public void Complete() => completed.TrySetResult();
    }

    private sealed class CountingMigration : ICosmosMigration
    {
        public int CallCount { get; private set; }
        public string Id => "test-counting-migration";
        public string Description => "Counts whether migration work started.";

        public Task UpAsync(ICosmosMigrationDatabase database, CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CancelAfterCreateLeaseStore(CancellationTokenSource cancellation)
        : ICosmosMigrationLeaseStore
    {
        public CosmosMigrationLease? ReleasedLease { get; private set; }

        public Task<CosmosMigrationLease?> TryCreateAsync(
            string owner, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
        {
            var lease = new CosmosMigrationLease("schema-migrations", owner, "lease-etag-created", expiresAtUtc);
            cancellation.Cancel();
            return Task.FromResult<CosmosMigrationLease?>(lease);
        }

        public Task<CosmosMigrationLease?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CosmosMigrationLease?>(null);

        public Task<CosmosMigrationLease?> TryReplaceAsync(
            CosmosMigrationLease expected, string newOwner, DateTimeOffset expiresAtUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<CosmosMigrationLease?>(null);

        public Task<bool> TryReleaseAsync(
            CosmosMigrationLease expected, CancellationToken cancellationToken)
        {
            ReleasedLease = expected;
            return Task.FromResult(true);
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

    private sealed class ShutdownControlledDelay : ICosmosMigrationDelay
    {
        private readonly TaskCompletionSource requested = NewSignal();
        private readonly TaskCompletionSource released = NewSignal();

        public Task WaitUntilRequestedAsync() => requested.Task;

        public void Release() => released.TrySetResult();

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            _ = cancellationToken; // Intentionally wait for the test to release after cancellation begins.
            requested.TrySetResult();
            await released.Task;
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
