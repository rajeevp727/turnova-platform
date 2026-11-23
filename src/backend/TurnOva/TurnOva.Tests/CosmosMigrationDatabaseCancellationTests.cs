using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Tests;

public sealed class CosmosMigrationDatabaseCancellationTests
{
    [Fact]
    public async Task PreCancelledTokenStopsBeforeDatabaseCall()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "Rides", "/id", cancellation.Token));

        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationAfterCreateStopsBeforeTtlRead()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        var createResponse = new Mock<ContainerResponse>();
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        using var cancellation = new CancellationTokenSource();
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(createResponse.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", cancellation.Token, defaultTimeToLive: 604800));

        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationAfterTtlReadStopsBeforeReplace()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        var createResponse = new Mock<ContainerResponse>();
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        var readResponse = new Mock<ContainerResponse>();
        readResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        using var cancellation = new CancellationTokenSource();
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);
        container.Setup(value => value.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(readResponse.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", cancellation.Token, defaultTimeToLive: 604800));

        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SuccessfulCreateRemainsAuthoritativeWhenCancellationArrivesAfterCommit()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var createResponse = new Mock<ContainerResponse>();
        using var cancellation = new CancellationTokenSource();
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(createResponse.Object);
            });

        await new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
            "Rides", "/id", cancellation.Token);

        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(),
            It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SuccessfulTtlReplaceRemainsAuthoritativeWhenCancellationArrivesAfterCommit()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        var createResponse = new Mock<ContainerResponse>();
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        var readResponse = new Mock<ContainerResponse>();
        readResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        var replaceResponse = new Mock<ContainerResponse>();
        using var cancellation = new CancellationTokenSource();
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);
        container.Setup(value => value.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(readResponse.Object);
        container.Setup(value => value.ReplaceContainerAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<ContainerRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(replaceResponse.Object);
            });

        await new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
            "SupportRequests", "/reporterId", cancellation.Token, defaultTimeToLive: 604800);

        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.Is<ContainerProperties>(properties => properties.DefaultTimeToLive == 604800),
            It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NullTtlReadResourceFailsClosedBeforeReplace()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        var readResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        readResponse.SetupGet(value => value.Resource).Returns((ContainerProperties)null!);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);
        container.Setup(value => value.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(readResponse.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", CancellationToken.None, defaultTimeToLive: 604800));

        Assert.Equal("Cosmos returned an empty container resource for TTL configuration.", exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        readResponse.VerifyGet(value => value.Resource, Times.Once);
        database.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        readResponse.VerifyNoOtherCalls();
    }

}
