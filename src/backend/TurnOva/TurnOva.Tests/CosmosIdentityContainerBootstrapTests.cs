using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Moq;
using TurnOva.API;

namespace TurnOva.Tests;

public sealed class CosmosIdentityContainerBootstrapTests
{
    private const string SigningKey = "turnova-auth-integration-test-signing-key-32-bytes";

    [Theory]
    [InlineData(null, "/role")]
    [InlineData("UnexpectedUsers", "/role")]
    [InlineData("TurnOvaUsers", "/unexpected")]
    public async Task RegisterRejectsNullOrMismatchedContainerMetadataBeforeWriting(
        string? returnedId,
        string returnedPartitionKeyPath)
    {
        var (service, database, container) = CreateService(returnedId, returnedPartitionKeyPath, includeContainer: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            CancellationToken.None));

        Assert.Equal(
            returnedId is null
                ? "Cosmos returned an empty identity container resource after bootstrap."
                : "Cosmos returned an identity container resource that does not match the requested name and partition-key path after bootstrap.",
            exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "TurnOvaUsers" && properties.PartitionKeyPath == "/role"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterRejectsBlankConfiguredDatabaseNameBeforeResolvingHandle(string configuredDatabaseName)
    {
        var client = new Mock<CosmosClient>(MockBehavior.Strict);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CosmosDb:DatabaseName"] = configuredDatabaseName,
            ["Auth:Jwt:Issuer"] = "turnova-auth-tests",
            ["Auth:Jwt:Audience"] = "turnova-api",
            ["Auth:Jwt:SigningKey"] = SigningKey
        }).Build();
        var service = new CosmosIdentityService(client.Object, configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            CancellationToken.None));

        Assert.Equal("Cosmos database name must be configured.", exception.Message);
        client.Verify(value => value.GetDatabase(It.IsAny<string>()), Times.Never);
        client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UnexpectedDb")]
    public async Task RegisterRejectsNullOrMismatchedDatabaseHandleBeforeContainerBootstrap(string? returnedDatabaseId)
    {
        var (service, database, container) = CreateService(
            "TurnOvaUsers",
            "/role",
            includeContainer: true,
            returnedDatabaseId: returnedDatabaseId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            CancellationToken.None));

        Assert.Equal(
            "Cosmos returned a database handle that does not match the configured database name.",
            exception.Message);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RegisterRejectsMissingContainerHandleBeforeWriting()
    {
        var (service, database, container) = CreateService("TurnOvaUsers", "/role", includeContainer: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            CancellationToken.None));

        Assert.Equal("Cosmos returned an empty identity container handle after bootstrap.", exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "TurnOvaUsers"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RegisterUsesValidatedBootstrapResponseContainer()
    {
        var (service, database, container) = CreateService("TurnOvaUsers", "/role", includeContainer: true, successfulBatch: true);

        var result = await service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "TurnOvaUsers" && properties.PartitionKeyPath == "/role"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("Rider")))), Times.Once);
    }

    [Fact]
    public async Task RegisterPreservesCancellationWhenContainerBootstrapReturnsCosmosFailure()
    {
        using var cts = new CancellationTokenSource();
        var database = new Mock<Database>(MockBehavior.Strict);
        database.SetupGet(value => value.Id).Returns("TurnOvaDb");
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<ContainerResponse>(
                    new CosmosException("request cancelled", HttpStatusCode.RequestTimeout, 0, "auth-bootstrap-test", 0));
            });

        var client = new Mock<CosmosClient>(MockBehavior.Strict);
        client.Setup(value => value.GetDatabase("TurnOvaDb")).Returns(database.Object);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Jwt:Issuer"] = "turnova-auth-tests",
            ["Auth:Jwt:Audience"] = "turnova-api",
            ["Auth:Jwt:SigningKey"] = SigningKey
        }).Build();
        var service = new CosmosIdentityService(client.Object, configuration);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            cts.Token));
    }

    [Fact]
    public async Task RegisterStopsBeforeIdentityBatchWhenContainerBootstrapCommitsAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        var (service, database, container) = CreateService(
            "TurnOvaUsers",
            "/role",
            includeContainer: true,
            onContainerBootstrap: cts.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            cts.Token));

        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "TurnOvaUsers" && properties.PartitionKeyPath == "/role"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RegisterRejectsMismatchedContainerHandleIdentityBeforeWriting()
    {
        var (service, _, container) = CreateService(
            "TurnOvaUsers", "/role", includeContainer: true, returnedContainerId: "UnexpectedUsers");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            CancellationToken.None));

        Assert.Equal(
            "Cosmos returned an identity container handle that does not match the requested name after bootstrap.",
            exception.Message);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RegisterPreservesCancellationWhenDatabaseHandleIdentityHydrationCompletesAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        var (service, database, container) = CreateService(
            "TurnOvaUsers",
            "/role",
            includeContainer: true,
            onDatabaseIdRead: cts.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            cts.Token));

        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RegisterCancellationTakesPrecedenceOverDatabaseHandleIdentityHydrationFailure()
    {
        using var cts = new CancellationTokenSource();
        var (service, database, container) = CreateService(
            "TurnOvaUsers",
            "/role",
            includeContainer: true,
            onDatabaseIdRead: () =>
            {
                cts.Cancel();
                throw new InvalidOperationException("database identity hydration failed");
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            cts.Token));

        database.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RegisterPreservesCancellationWhenContainerResourceHydrationCompletesAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        var (service, _, container) = CreateService(
            "TurnOvaUsers", "/role", includeContainer: true, onResourceRead: cts.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            cts.Token));

        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RegisterPreservesCancellationWhenContainerHandleHydrationCompletesAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        var (service, _, container) = CreateService(
            "TurnOvaUsers", "/role", includeContainer: true, onContainerRead: cts.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            cts.Token));

        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RegisterPreservesCancellationWhenContainerHandleIdentityHydrationCompletesAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        var (service, _, container) = CreateService(
            "TurnOvaUsers", "/role", includeContainer: true, onContainerIdRead: cts.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            cts.Token));

        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RegisterCancellationTakesPrecedenceOverContainerHandleIdentityHydrationFailure()
    {
        using var cts = new CancellationTokenSource();
        var (service, _, container) = CreateService(
            "TurnOvaUsers",
            "/role",
            includeContainer: true,
            onContainerIdRead: () =>
            {
                cts.Cancel();
                throw new InvalidOperationException("container identity hydration failed");
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            cts.Token));

        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    private static (CosmosIdentityService Service, Mock<Database> Database, Mock<Container> Container) CreateService(
        string? returnedId,
        string returnedPartitionKeyPath,
        bool includeContainer,
        bool successfulBatch = false,
        string returnedContainerId = "TurnOvaUsers",
        Action? onResourceRead = null,
        Action? onContainerRead = null,
        Action? onContainerIdRead = null,
        string? returnedDatabaseId = "TurnOvaDb",
        Action? onDatabaseIdRead = null,
        Action? onContainerBootstrap = null)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns(() =>
        {
            onContainerIdRead?.Invoke();
            return returnedContainerId;
        });
        var response = new Mock<ContainerResponse>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(() =>
        {
            onResourceRead?.Invoke();
            return returnedId is null ? null! : new ContainerProperties(returnedId, returnedPartitionKeyPath);
        });
        response.SetupGet(value => value.Container).Returns(() =>
        {
            onContainerRead?.Invoke();
            return includeContainer ? container.Object : null!;
        });
        if (successfulBatch)
        {
            var batch = new Mock<TransactionalBatch>(MockBehavior.Strict);
            var batchResponse = new Mock<TransactionalBatchResponse>();
            batchResponse.SetupGet(value => value.StatusCode).Returns(HttpStatusCode.OK);
            batchResponse.SetupGet(value => value.IsSuccessStatusCode).Returns(true);
            batch.Setup(value => value.CreateItem(
                    It.IsAny<CosmosIdentityService.EmailReservation>(),
                    It.IsAny<TransactionalBatchItemRequestOptions?>()))
                .Returns(batch.Object);
            batch.Setup(value => value.CreateItem(
                    It.IsAny<CosmosIdentityService.IdentityDocument>(),
                    It.IsAny<TransactionalBatchItemRequestOptions?>()))
                .Returns(batch.Object);
            batch.Setup(value => value.ExecuteAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(batchResponse.Object);
            container.Setup(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
                .Returns(batch.Object);
        }

        var database = new Mock<Database>(MockBehavior.Strict);
        database.SetupGet(value => value.Id).Returns(() =>
        {
            onDatabaseIdRead?.Invoke();
            return returnedDatabaseId!;
        });
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                onContainerBootstrap?.Invoke();
                return Task.FromResult(response.Object);
            });

        var client = new Mock<CosmosClient>(MockBehavior.Strict);
        client.Setup(value => value.GetDatabase("TurnOvaDb")).Returns(database.Object);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Jwt:Issuer"] = "turnova-auth-tests",
            ["Auth:Jwt:Audience"] = "turnova-api",
            ["Auth:Jwt:SigningKey"] = SigningKey
        }).Build();

        return (new CosmosIdentityService(client.Object, configuration), database, container);
    }

}
