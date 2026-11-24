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

    [Fact]
    public async Task CancellationDuringHistoryContainerResourceHydrationStopsBeforeReadingHandleOrCreatingLockContainer()
    {
        using var cancellation = new CancellationTokenSource();
        var historyResponse = CreateResponse(
            "MigrationHistory", "/id", includeContainer: true,
            onResourceRead: cancellation.Cancel);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        historyResponse.VerifyGet(value => value.Resource, Times.Once);
        historyResponse.VerifyGet(value => value.Container, Times.Never);
        lockResponse.VerifyGet(value => value.Resource, Times.Never);
        lockResponse.VerifyGet(value => value.Container, Times.Never);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationTakesPrecedenceWhenHistoryContainerResourceHydrationThrows()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException("history container resource hydration failed");
        var historyResponse = CreateResponse(
            "MigrationHistory", "/id", includeContainer: true,
            onResourceRead: () =>
            {
                cancellation.Cancel();
                throw expected;
            });
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        historyResponse.VerifyGet(value => value.Resource, Times.Once);
        historyResponse.VerifyGet(value => value.Container, Times.Never);
        lockResponse.VerifyGet(value => value.Resource, Times.Never);
        lockResponse.VerifyGet(value => value.Container, Times.Never);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HistoryContainerResourceHydrationFailurePropagatesWithoutCancellation()
    {
        var expected = new InvalidOperationException("history container resource hydration failed");
        var historyResponse = CreateResponse(
            "MigrationHistory", "/id", includeContainer: true,
            onResourceRead: () => throw expected);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Same(expected, actual);
        historyResponse.VerifyGet(value => value.Resource, Times.Once);
        historyResponse.VerifyGet(value => value.Container, Times.Never);
        lockResponse.VerifyGet(value => value.Resource, Times.Never);
        lockResponse.VerifyGet(value => value.Container, Times.Never);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task CancellationDuringLockContainerResourceHydrationStopsBeforeReadingHandleOrAcquiringLease()
    {
        using var cancellation = new CancellationTokenSource();
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse(
            "MigrationLocks", "/id", includeContainer: true,
            onResourceRead: cancellation.Cancel);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        historyResponse.VerifyGet(value => value.Resource, Times.Once);
        historyResponse.VerifyGet(value => value.Container, Times.Once);
        lockResponse.VerifyGet(value => value.Resource, Times.Once);
        lockResponse.VerifyGet(value => value.Container, Times.Never);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationTakesPrecedenceWhenLockContainerResourceHydrationThrows()
    {
        using var cancellation = new CancellationTokenSource();
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var expected = new InvalidOperationException("lock container resource hydration failed");
        var lockResponse = CreateResponse(
            "MigrationLocks", "/id", includeContainer: true,
            onResourceRead: () =>
            {
                cancellation.Cancel();
                throw expected;
            });
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        historyResponse.VerifyGet(value => value.Resource, Times.Once);
        historyResponse.VerifyGet(value => value.Container, Times.Once);
        lockResponse.VerifyGet(value => value.Resource, Times.Once);
        lockResponse.VerifyGet(value => value.Container, Times.Never);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UnexpectedHistory")]
    public async Task HistoryBootstrapRejectsBlankOrMismatchedContainerHandleIdentity(string? returnedHandleId)
    {
        var historyResponse = CreateResponse(
            "MigrationHistory", "/id", includeContainer: true,
            handleId: returnedHandleId, overrideHandleId: true);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal(
            "Cosmos returned a migration container handle that does not match the requested name after bootstrap.",
            exception.Message);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UnexpectedLocks")]
    public async Task LockBootstrapRejectsBlankOrMismatchedContainerHandleIdentity(string? returnedHandleId)
    {
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse(
            "MigrationLocks", "/id", includeContainer: true,
            handleId: returnedHandleId, overrideHandleId: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal(
            "Cosmos returned a migration container handle that does not match the requested name after bootstrap.",
            exception.Message);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationDuringHistoryContainerHandleIdentityHydrationStopsBeforeLockContainerCreation()
    {
        using var cancellation = new CancellationTokenSource();
        var historyResponse = CreateResponse(
            "MigrationHistory", "/id", includeContainer: true,
            onHandleIdRead: cancellation.Cancel);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationTakesPrecedenceWhenHistoryContainerHandleIdentityHydrationThrows()
    {
        using var cancellation = new CancellationTokenSource();
        var historyResponse = CreateResponse(
            "MigrationHistory", "/id", includeContainer: true,
            onHandleIdRead: () =>
            {
                cancellation.Cancel();
                throw new InvalidOperationException("history container ID hydration failed");
            });
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HistoryContainerHandleIdentityHydrationFailurePropagatesWithoutCancellation()
    {
        var expected = new InvalidOperationException("history container ID hydration failed");
        var historyResponse = CreateResponse(
            "MigrationHistory", "/id", includeContainer: true,
            onHandleIdRead: () => throw expected);
        var lockResponse = CreateResponse("MigrationLocks", "/id", includeContainer: true);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Same(expected, actual);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationDuringLockContainerHandleIdentityHydrationStopsBeforeLeaseAcquisition()
    {
        using var cancellation = new CancellationTokenSource();
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse(
            "MigrationLocks", "/id", includeContainer: true,
            onHandleIdRead: cancellation.Cancel);
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        leaseStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationTakesPrecedenceWhenLockContainerHandleIdentityHydrationThrows()
    {
        using var cancellation = new CancellationTokenSource();
        var historyResponse = CreateResponse("MigrationHistory", "/id", includeContainer: true);
        var lockResponse = CreateResponse(
            "MigrationLocks", "/id", includeContainer: true,
            onHandleIdRead: () =>
            {
                cancellation.Cancel();
                throw new InvalidOperationException("lock container ID hydration failed");
            });
        var (runner, database, leaseStore, _) = CreateRunner(historyResponse, lockResponse);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationHistory"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "MigrationLocks"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        leaseStore.VerifyNoOtherCalls();
    }

    private static Mock<ContainerResponse> CreateResponse(
        string? id,
        string partitionKeyPath,
        bool includeContainer,
        string? handleId = null,
        bool overrideHandleId = false,
        Action? onHandleIdRead = null,
        Action? onResourceRead = null)
    {
        var response = new Mock<ContainerResponse>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(() =>
        {
            onResourceRead?.Invoke();
            return id is null ? null! : new ContainerProperties(id, partitionKeyPath);
        });

        if (includeContainer)
        {
            var container = new Mock<Container>(MockBehavior.Strict);
            container.SetupGet(value => value.Id).Returns(() =>
            {
                onHandleIdRead?.Invoke();
                return overrideHandleId ? handleId! : id!;
            });
            response.SetupGet(value => value.Container).Returns(container.Object);
        }
        else
        {
            response.SetupGet(value => value.Container).Returns((Container)null!);
        }

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
