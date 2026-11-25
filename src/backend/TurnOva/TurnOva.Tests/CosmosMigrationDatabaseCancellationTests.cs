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
        container.SetupGet(value => value.Id).Returns("SupportRequests");
        var createResponse = new Mock<ContainerResponse>();
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
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
        container.SetupGet(value => value.Id).Returns("SupportRequests");
        var createResponse = new Mock<ContainerResponse>();
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
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
        container.VerifyGet(value => value.Id, Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SuccessfulCreateRemainsAuthoritativeWhenCancellationArrivesAfterCommit()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var createResponse = new Mock<ContainerResponse>();
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("Rides", "/id"));
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
        container.SetupGet(value => value.Id).Returns("SupportRequests");
        var createResponse = new Mock<ContainerResponse>();
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
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
        container.VerifyGet(value => value.Id, Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NullTtlReadResourceFailsClosedBeforeReplace()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns("SupportRequests");
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
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
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        readResponse.VerifyGet(value => value.Resource, Times.Once);
        database.VerifyNoOtherCalls();
        container.VerifyGet(value => value.Id, Times.Once);
        container.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        readResponse.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("OtherContainer", "/reporterId")]
    [InlineData("SupportRequests", "/otherReporterId")]
    public async Task TtlReadRejectsContainerResourceWithMismatchedIdentityBeforeReplace(
        string returnedContainerName,
        string returnedPartitionKeyPath)
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns("SupportRequests");
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        var readResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        readResponse.SetupGet(value => value.Resource).Returns(
            new ContainerProperties(returnedContainerName, returnedPartitionKeyPath));
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

        Assert.Equal(
            "Cosmos returned a container resource that does not match the requested name and partition-key path for TTL configuration.",
            exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "SupportRequests" && properties.PartitionKeyPath == "/reporterId"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        readResponse.VerifyGet(value => value.Resource, Times.Once);
        database.VerifyNoOtherCalls();
        container.VerifyGet(value => value.Id, Times.Once);
        container.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        readResponse.VerifyNoOtherCalls();
    }


    [Fact]
    public async Task MissingContainerHandleFailsClosedBeforeTtlRead()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns((Container)null!);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", CancellationToken.None, defaultTimeToLive: 604800));

        Assert.Equal("Cosmos returned an empty container handle for TTL configuration.", exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "SupportRequests" && properties.PartitionKeyPath == "/reporterId"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("OtherContainer")]
    public async Task TtlContainerHandleRejectsBlankOrMismatchedIdentityBeforeTtlRead(
        string? returnedContainerId)
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns(returnedContainerId!);
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", CancellationToken.None, defaultTimeToLive: 604800));

        Assert.Equal(
            "Cosmos returned a container handle that does not match the requested name for TTL configuration.",
            exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "SupportRequests" && properties.PartitionKeyPath == "/reporterId"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationDuringContainerHandleIdentityHydrationStopsBeforeTtlRead()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.SetupGet(value => value.Id).Returns(() =>
        {
            cancellation.Cancel();
            return "SupportRequests";
        });
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", cancellation.Token, defaultTimeToLive: 604800));

        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationTakesPrecedenceWhenContainerHandleIdentityHydrationThrows()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.SetupGet(value => value.Id).Returns(() =>
        {
            cancellation.Cancel();
            throw new InvalidOperationException("container identity hydration failed");
        });
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", cancellation.Token, defaultTimeToLive: 604800));

        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ContainerHandleIdentityHydrationFailurePropagatesWithoutCancellation()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns(() =>
            throw new InvalidOperationException("container identity hydration failed"));
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", CancellationToken.None, defaultTimeToLive: 604800));

        Assert.Equal("container identity hydration failed", exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("OtherContainer", "/reporterId")]
    [InlineData("SupportRequests", "/otherReporterId")]
    public async Task CreateResponseResourceRejectsMismatchedIdentityEvenWithoutTtl(
        string returnedContainerName,
        string returnedPartitionKeyPath)
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Resource).Returns(
            new ContainerProperties(returnedContainerName, returnedPartitionKeyPath));
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", CancellationToken.None));

        Assert.Equal(
            "Cosmos returned a container resource that does not match the requested name and partition-key path after container creation.",
            exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "SupportRequests" && properties.PartitionKeyPath == "/reporterId"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Never);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NullCreateResponseResourceFailsClosedEvenWithoutTtl()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Resource).Returns((ContainerProperties)null!);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "Rides", "/id", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty container resource after container creation.", exception.Message);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties => properties.Id == "Rides" && properties.PartitionKeyPath == "/id"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Never);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationDuringCommittedCreateResourceHydrationStopsBeforeTtlHandleRead()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Resource).Returns(() =>
        {
            cancellation.Cancel();
            return new ContainerProperties("SupportRequests", "/reporterId");
        });
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", cancellation.Token, defaultTimeToLive: 604800));

        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.Is<ContainerProperties>(properties =>
                properties.Id == "SupportRequests" && properties.PartitionKeyPath == "/reporterId"),
            It.IsAny<int?>(), It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Never);
        container.VerifyGet(value => value.Id, Times.Never);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CommittedCreateResourceGetterExceptionIsNotReplacedByCancellation()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException("created container resource hydration failed");
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Resource).Returns(() =>
        {
            cancellation.Cancel();
            throw expected;
        });
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", cancellation.Token, defaultTimeToLive: 604800));

        Assert.Same(expected, actual);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Never);
        container.VerifyGet(value => value.Id, Times.Never);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CommittedCreateResourceHydrationFailurePropagatesWithoutCancellation()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        var expected = new InvalidOperationException("created container resource hydration failed");
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Resource).Returns(() => throw expected);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        database.Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse.Object);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", CancellationToken.None, defaultTimeToLive: 604800));

        Assert.Same(expected, actual);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Never);
        container.VerifyGet(value => value.Id, Times.Never);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        database.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
    }


    [Fact]
    public async Task CancellationDuringTtlReadResourceHydrationStopsBeforeReplace()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns("SupportRequests");
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        using var cancellation = new CancellationTokenSource();
        var readResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        readResponse.SetupGet(value => value.Resource).Returns(() =>
        {
            cancellation.Cancel();
            return new ContainerProperties("SupportRequests", "/reporterId");
        });
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", cancellation.Token, defaultTimeToLive: 604800));

        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        readResponse.VerifyGet(value => value.Resource, Times.Once);
        database.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        readResponse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationTakesPrecedenceWhenTtlReadResourceHydrationThrows()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns("SupportRequests");
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException("TTL container resource hydration failed");
        var readResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        readResponse.SetupGet(value => value.Resource).Returns(() =>
        {
            cancellation.Cancel();
            throw expected;
        });
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", cancellation.Token, defaultTimeToLive: 604800));

        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        readResponse.VerifyGet(value => value.Resource, Times.Once);
        database.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        readResponse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TtlReadResourceHydrationFailurePropagatesWithoutCancellation()
    {
        var database = new Mock<Database>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.SetupGet(value => value.Id).Returns("SupportRequests");
        var createResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.Container).Returns(container.Object);
        createResponse.SetupGet(value => value.Resource).Returns(new ContainerProperties("SupportRequests", "/reporterId"));
        var expected = new InvalidOperationException("TTL container resource hydration failed");
        var readResponse = new Mock<ContainerResponse>(MockBehavior.Strict);
        readResponse.SetupGet(value => value.Resource).Returns(() => throw expected);
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

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosMigrationDatabase(database.Object).CreateContainerIfNotExistsAsync(
                "SupportRequests", "/reporterId", CancellationToken.None, defaultTimeToLive: 604800));

        Assert.Same(expected, actual);
        database.Verify(value => value.CreateContainerIfNotExistsAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<int?>(),
            It.IsAny<RequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyGet(value => value.Id, Times.Once);
        container.Verify(value => value.ReadContainerAsync(
            It.IsAny<ContainerRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceContainerAsync(
            It.IsAny<ContainerProperties>(), It.IsAny<ContainerRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        createResponse.VerifyGet(value => value.Resource, Times.Once);
        createResponse.VerifyGet(value => value.Container, Times.Once);
        readResponse.VerifyGet(value => value.Resource, Times.Once);
        database.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
        createResponse.VerifyNoOtherCalls();
        readResponse.VerifyNoOtherCalls();
    }

}
