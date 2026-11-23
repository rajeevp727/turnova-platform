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
        var (service, database, container) = CreateService(
            CreateResponse(returnedId, returnedPartitionKeyPath, includeContainer: true));

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

    [Fact]
    public async Task RegisterRejectsMissingContainerHandleBeforeWriting()
    {
        var (service, database, container) = CreateService(
            CreateResponse("TurnOvaUsers", "/role", includeContainer: false));

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
        var (service, database, container) = CreateService(
            CreateResponse("TurnOvaUsers", "/role", includeContainer: true),
            successfulBatch: true);

        var result = await service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", "SecurePass123", "Rider"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "TurnOvaUsers" && properties.PartitionKeyPath == "/role"),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("Rider")))), Times.Once);
    }

    private static (CosmosIdentityService Service, Mock<Database> Database, Mock<Container> Container) CreateService(
        Mock<ContainerResponse> response,
        bool successfulBatch = false)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        if (successfulBatch)
        {
            var batch = new Mock<TransactionalBatch>(MockBehavior.Strict);
            var batchResponse = new Mock<TransactionalBatchResponse>(MockBehavior.Strict);
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
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

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

    private static Mock<ContainerResponse> CreateResponse(
        string? id,
        string partitionKeyPath,
        bool includeContainer)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var response = new Mock<ContainerResponse>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(
            id is null ? null! : new ContainerProperties(id, partitionKeyPath));
        response.SetupGet(value => value.Container).Returns(
            includeContainer ? container.Object : null!);
        return response;
    }
}
