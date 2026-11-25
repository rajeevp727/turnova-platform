using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Tests;

public sealed class CosmosMigrationDatabaseTests
{
    private const string ContainerName = "SupportRequests";
    private const string PartitionKeyPath = "/reporterId";

    [Fact]
    public async Task CreateRejectsMismatchedCreatedContainerIdentity()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var response = new Mock<ContainerResponse>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties("DifferentContainer", PartitionKeyPath));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.Is<ContainerProperties>(properties =>
                    properties.Id == ContainerName &&
                    properties.PartitionKeyPath == PartitionKeyPath),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                ContainerName, PartitionKeyPath, CancellationToken.None));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        response.VerifyGet(value => value.Resource, Times.Once);
        response.VerifyNoOtherCalls();
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateRejectsMismatchedCreatedPartitionKeyPath()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var response = new Mock<ContainerResponse>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, "/wrongPartition"));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                ContainerName, PartitionKeyPath, CancellationToken.None));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        response.VerifyGet(value => value.Resource, Times.Once);
        response.VerifyNoOtherCalls();
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateDoesNotRetroactivelyCancelACommittedContainerCreate()
    {
        using var cancellation = new CancellationTokenSource();
        var database = new Mock<Database>(MockBehavior.Strict);
        var response = new Mock<ContainerResponse>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(response.Object);
            });

        await new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
            ContainerName, PartitionKeyPath, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        response.VerifyGet(value => value.Resource, Times.Once);
        response.VerifyNoOtherCalls();
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TtlUpdateValidatesCreatedAndCurrentContainerThenReplacesOnlyTtl()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns(ContainerName);
        var createdResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createdResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath));
        createdResponse.SetupGet(value => value.Container).Returns(container.Object);
        var currentProperties = new ContainerProperties(ContainerName, PartitionKeyPath);
        var currentResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        currentResponse.SetupGet(value => value.Resource).Returns(currentProperties);
        currentResponse.SetupGet(value => value.ETag).Returns("container-etag");
        var replacementResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.Is<ContainerProperties>(properties =>
                    properties.Id == ContainerName &&
                    properties.PartitionKeyPath == PartitionKeyPath &&
                    properties.DefaultTimeToLive == -1),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdResponse.Object);
        container.Setup(value => value.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentResponse.Object);
        container.Setup(value => value.ReplaceContainerAsync(
                It.Is<ContainerProperties>(properties =>
                    properties.Id == ContainerName &&
                    properties.PartitionKeyPath == PartitionKeyPath &&
                    properties.DefaultTimeToLive == -1),
                It.Is<ContainerRequestOptions?>(options =>
                    options != null && options.IfMatchEtag == "container-etag"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(replacementResponse.Object);

        await new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
            ContainerName, PartitionKeyPath, CancellationToken.None, defaultTimeToLive: -1);

        Assert.Equal(-1, currentProperties.DefaultTimeToLive);
        createdResponse.VerifyGet(value => value.Resource, Times.Once);
        createdResponse.VerifyGet(value => value.Container, Times.Once);
        currentResponse.VerifyGet(value => value.Resource, Times.Once);
        currentResponse.VerifyGet(value => value.ETag, Times.Once);
        currentResponse.VerifyNoOtherCalls();
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(),
            It.Is<ContainerRequestOptions?>(options =>
                options != null && options.IfMatchEtag == "container-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TtlUpdateDoesNotReplaceWhenExistingTtlAlreadyMatches()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns(ContainerName);
        var createdResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createdResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath));
        createdResponse.SetupGet(value => value.Container).Returns(container.Object);
        var currentResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        currentResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath) { DefaultTimeToLive = -1 });
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdResponse.Object);
        container.Setup(value => value.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentResponse.Object);

        await new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
            ContainerName, PartitionKeyPath, CancellationToken.None, defaultTimeToLive: -1);

        createdResponse.VerifyGet(value => value.Resource, Times.Once);
        createdResponse.VerifyGet(value => value.Container, Times.Once);
        currentResponse.VerifyGet(value => value.Resource, Times.Once);
        currentResponse.VerifyGet(value => value.ETag, Times.Once);
        currentResponse.VerifyNoOtherCalls();
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TtlUpdateRejectsMismatchedContainerHandleBeforeReadingIt()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns("DifferentContainer");
        var createdResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createdResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath));
        createdResponse.SetupGet(value => value.Container).Returns(container.Object);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdResponse.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                ContainerName, PartitionKeyPath, CancellationToken.None, defaultTimeToLive: -1));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        createdResponse.VerifyGet(value => value.Resource, Times.Once);
        createdResponse.VerifyGet(value => value.Container, Times.Once);
        container.VerifyGet(value => value.Id, Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TtlUpdateRejectsMismatchedCurrentContainerBeforeReplacingIt()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns(ContainerName);
        var createdResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createdResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath));
        createdResponse.SetupGet(value => value.Container).Returns(container.Object);
        var currentResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        currentResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, "/wrongPartition"));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdResponse.Object);
        container.Setup(value => value.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentResponse.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                ContainerName, PartitionKeyPath, CancellationToken.None, defaultTimeToLive: -1));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        createdResponse.VerifyGet(value => value.Resource, Times.Once);
        createdResponse.VerifyGet(value => value.Container, Times.Once);
        currentResponse.VerifyGet(value => value.Resource, Times.Once);
        currentResponse.VerifyNoOtherCalls();
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TtlUpdateRequiresObservedEtagBeforeReplacingContainer()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns(ContainerName);
        var createdResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createdResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath));
        createdResponse.SetupGet(value => value.Container).Returns(container.Object);
        var currentResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        currentResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath));
        currentResponse.SetupGet(value => value.ETag).Returns(string.Empty);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdResponse.Object);
        container.Setup(value => value.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentResponse.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                ContainerName, PartitionKeyPath, CancellationToken.None, defaultTimeToLive: -1));

        Assert.Contains("ETag", exception.Message, StringComparison.Ordinal);
        currentResponse.VerifyGet(value => value.Resource, Times.Once);
        currentResponse.VerifyGet(value => value.ETag, Times.Once);
        currentResponse.VerifyNoOtherCalls();
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TtlReplaceRemainsAuthoritativeWhenCancellationArrivesAfterCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns(ContainerName);
        var createdResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createdResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath));
        createdResponse.SetupGet(value => value.Container).Returns(container.Object);
        var currentResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        currentResponse.SetupGet(value => value.Resource)
            .Returns(new ContainerProperties(ContainerName, PartitionKeyPath));
        currentResponse.SetupGet(value => value.ETag).Returns("container-etag");
        var replacementResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdResponse.Object);
        container.Setup(value => value.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions?>(), cancellation.Token))
            .ReturnsAsync(currentResponse.Object);
        container.Setup(value => value.ReplaceContainerAsync(
                It.Is<ContainerProperties>(properties => properties.DefaultTimeToLive == -1),
                It.Is<ContainerRequestOptions?>(options =>
                    options != null && options.IfMatchEtag == "container-etag"),
                cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(replacementResponse.Object);
            });

        await new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
            ContainerName, PartitionKeyPath, cancellation.Token, defaultTimeToLive: -1);

        Assert.True(cancellation.IsCancellationRequested);
        createdResponse.VerifyGet(value => value.Resource, Times.Once);
        createdResponse.VerifyGet(value => value.Container, Times.Once);
        currentResponse.VerifyGet(value => value.Resource, Times.Once);
        currentResponse.VerifyNoOtherCalls();
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), cancellation.Token), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(),
            It.Is<ContainerRequestOptions?>(options =>
                options != null && options.IfMatchEtag == "container-etag"),
            cancellation.Token), Times.Once);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }
}
