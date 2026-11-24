using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Tests;

public sealed class CosmosMigrationBootstrapResourceContractTests
{
    [Fact]
    public async Task DatabaseBootstrapRejectsMissingDatabaseHandle()
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, databaseResponse) = CreateRunner(
            historyResponse, lockResponse, includeDatabase: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal("Cosmos returned an empty migration database handle after bootstrap.", exception.Message);
        databaseResponse.VerifyGet(value => value.Database, Times.Once);
        database.VerifyGet(value => value.Id, Times.Never);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UnexpectedDatabase")]
    public async Task DatabaseBootstrapRejectsBlankOrMismatchedDatabaseHandleIdentity(string? returnedId)
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, databaseResponse) = CreateRunner(
            historyResponse, lockResponse, databaseId: returnedId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal(
            "Cosmos returned a migration database handle that does not match the configured database name after bootstrap.",
            exception.Message);
        databaseResponse.VerifyGet(value => value.Database, Times.Once);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationDuringDatabaseHandleHydrationStopsBeforeHistoryContainerCreation()
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, databaseResponse) = CreateRunner(historyResponse, lockResponse);
        using var cancellation = new CancellationTokenSource();
        databaseResponse.SetupGet(value => value.Database).Returns(() =>
        {
            cancellation.Cancel();
            return database.Object;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        databaseResponse.VerifyGet(value => value.Database, Times.Once);
        database.VerifyGet(value => value.Id, Times.Never);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationTakesPrecedenceWhenDatabaseHandleHydrationThrows()
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, databaseResponse) = CreateRunner(historyResponse, lockResponse);
        using var cancellation = new CancellationTokenSource();
        databaseResponse.SetupGet(value => value.Database).Returns(() =>
        {
            cancellation.Cancel();
            throw new InvalidOperationException("database handle hydration failed");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        databaseResponse.VerifyGet(value => value.Database, Times.Once);
        database.VerifyGet(value => value.Id, Times.Never);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationDuringDatabaseIdentityHydrationStopsBeforeHistoryContainerCreation()
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, databaseResponse) = CreateRunner(historyResponse, lockResponse);
        using var cancellation = new CancellationTokenSource();
        database.SetupGet(value => value.Id).Returns(() =>
        {
            cancellation.Cancel();
            return "TurnOvaMigrationTest";
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        databaseResponse.VerifyGet(value => value.Database, Times.Once);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationTakesPrecedenceWhenDatabaseIdentityHydrationThrows()
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, databaseResponse) = CreateRunner(historyResponse, lockResponse);
        using var cancellation = new CancellationTokenSource();
        database.SetupGet(value => value.Id).Returns(() =>
        {
            cancellation.Cancel();
            throw new InvalidOperationException("database identity hydration failed");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        databaseResponse.VerifyGet(value => value.Database, Times.Once);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, "/id")]
    [InlineData("UnexpectedHistory", "/id")]
    [InlineData("MigrationHistory", "/unexpected")]
    public async Task HistoryBootstrapRejectsNullOrMismatchedResource(
        string? returnedId,
        string returnedPartitionKeyPath)
    {
        var historyResponse = CreateResponse(returnedId, returnedPartitionKeyPath, includeContainer: true);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal(
            returnedId is null
                ? "Cosmos returned an empty migration container resource after bootstrap."
                : "Cosmos returned a migration container resource that does not match the requested name and partition-key path after bootstrap.",
            exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "MigrationHistory" && properties.PartitionKeyPath == "/id"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HistoryBootstrapRejectsMissingContainerHandle()
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: false);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal("Cosmos returned an empty migration container handle after bootstrap.", exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        leaseStore.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, "/id")]
    [InlineData("UnexpectedLocks", "/id")]
    [InlineData("MigrationLocks", "/unexpected")]
    public async Task LockBootstrapRejectsNullOrMismatchedResource(
        string? returnedId,
        string returnedPartitionKeyPath)
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse(returnedId, returnedPartitionKeyPath, includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal(
            returnedId is null
                ? "Cosmos returned an empty migration container resource after bootstrap."
                : "Cosmos returned a migration container resource that does not match the requested name and partition-key path after bootstrap.",
            exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "MigrationLocks" && properties.PartitionKeyPath == "/id"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LockBootstrapRejectsMissingContainerHandle()
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: false);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal("Cosmos returned an empty migration container handle after bootstrap.", exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        leaseStore.VerifyNoOtherCalls();
    }

    private static Mock<ContainerResponse> CreateResponse(
        string? id,
        string partitionKeyPath,
        bool includeContainer)
    {
        var response = new Mock<ContainerResponse>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(
            id is null ? null! : new ContainerProperties(id, partitionKeyPath));
        response.SetupGet(value => value.Container).Returns(
            includeContainer ? new Mock<Container>(MockBehavior.Strict).Object : null!);
        return response;
    }

    private static (
        CosmosMigrationRunner Runner,
        Mock<Database> Database,
        Mock<ICosmosMigrationLeaseStore> LeaseStore,
        Mock<DatabaseResponse> DatabaseResponse)
        CreateRunner(
            Mock<ContainerResponse> historyResponse,
            Mock<ContainerResponse> lockResponse,
            string? databaseId = "TurnOvaMigrationTest",
            bool includeDatabase = true)
    {
        var client = new Mock<CosmosClient>(MockBehavior.Strict);
        var database = new Mock<Database>(MockBehavior.Strict);
        database.SetupGet(value => value.Id).Returns(databaseId!);
        var databaseResponse = new Mock<DatabaseResponse>();
        databaseResponse.SetupGet(value => value.Database).Returns(includeDatabase ? database.Object : null!);
        client.Setup(value => value.CreateDatabaseIfNotExistsAsync(
                "TurnOvaMigrationTest",
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(databaseResponse.Object);
        database.SetupSequence(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(historyResponse.Object)
            .ReturnsAsync(lockResponse.Object);

        var leaseStore = new Mock<ICosmosMigrationLeaseStore>(MockBehavior.Strict);
        var coordinator = new CosmosMigrationLeaseCoordinator(
            leaseStore.Object,
            TimeProvider.System,
            new SystemCosmosMigrationDelay(),
            CosmosMigrationLeaseOptions.Default,
            NullLogger<CosmosMigrationLeaseCoordinator>.Instance);
        var runner = new CosmosMigrationRunner(
            client.Object,
            new CosmosDbOptions { DatabaseName = "TurnOvaMigrationTest" },
            [],
            coordinator,
            NullLogger<CosmosMigrationRunner>.Instance);

        return (runner, database, leaseStore, databaseResponse);
    }
}
