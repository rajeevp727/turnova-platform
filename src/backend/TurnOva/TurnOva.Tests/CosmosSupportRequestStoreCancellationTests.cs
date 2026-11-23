using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Support;
using TurnOva.Infrastructure.Support;

namespace TurnOva.Tests;

public sealed class CosmosSupportRequestStoreCancellationTests
{
    [Fact]
    public async Task PreCancelledTokenStopsBeforeCreate()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(
                CreateRequest(), cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateConflictChecksCancellationBeforeCanonicalRead()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var conflict = new CosmosException(
            "support request already exists", HttpStatusCode.Conflict, 0, "support-request-test", 0);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<SupportRequest>>(conflict);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(
                CreateRequest(), cancellation.Token));

        container.Verify(value => value.CreateItemAsync(
            It.IsAny<SupportRequest>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<SupportRequest>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CanonicalReadChecksCancellationBeforeReturningReplayOrConflict()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var conflict = new CosmosException(
            "support request already exists", HttpStatusCode.Conflict, 0, "support-request-test", 0);
        var request = CreateRequest();
        var response = new Mock<ItemResponse<SupportRequest>>();
        response.SetupGet(value => value.Resource).Returns(request);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(conflict);
        container.Setup(value => value.ReadItemAsync<SupportRequest>(
                request.Id,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(response.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(request, cancellation.Token));

        container.Verify(value => value.CreateItemAsync(
            It.IsAny<SupportRequest>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<SupportRequest>(
            request.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingCanonicalReadDoesNotHideCancellation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var conflict = new CosmosException(
            "support request create conflict", HttpStatusCode.Conflict, 0, "support-request-test", 0);
        var missing = new CosmosException(
            "canonical support request disappeared", HttpStatusCode.NotFound, 0, "support-request-test", 0);
        var request = CreateRequest();
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(conflict);
        container.Setup(value => value.ReadItemAsync<SupportRequest>(
                request.Id,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<SupportRequest>>(missing);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(request, cancellation.Token));

        container.Verify(value => value.CreateItemAsync(
            It.IsAny<SupportRequest>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<SupportRequest>(
            request.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CanonicalReadProviderFailurePropagatesAfterCreateConflict()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var conflict = new CosmosException(
            "support request already exists", HttpStatusCode.Conflict, 0, "support-request-test", 0);
        var unavailable = new CosmosException(
            "canonical support request read unavailable", HttpStatusCode.ServiceUnavailable, 0, "support-request-test", 0);
        var request = CreateRequest();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(conflict);
        container.Setup(value => value.ReadItemAsync<SupportRequest>(
                request.Id,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(unavailable);

        var exception = await Assert.ThrowsAsync<CosmosException>(() =>
            new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(request));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        container.Verify(value => value.CreateItemAsync(
            It.IsAny<SupportRequest>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<SupportRequest>(
            request.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SuccessfulCreateRemainsAuthoritativeWhenCancellationArrivesAfterCommit()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var request = CreateRequest();
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(Mock.Of<ItemResponse<SupportRequest>>());
            });

        var result = await new CosmosSupportRequestStore(container.Object)
            .CreateOrGetAsync(request, cancellation.Token);

        Assert.Equal(SupportRequestWriteStatus.Created, result.Status);
        Assert.Equal(request, result.Request);
        container.Verify(value => value.CreateItemAsync(
            It.IsAny<SupportRequest>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    private static SupportRequest CreateRequest() =>
        new(
            "request-1",
            "reporter-1",
            "Rider",
            "Trip update issue",
            "The trip status did not refresh after reconnecting.",
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task CanonicalReadRejectsNullResourceAfterCreateConflict()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var conflict = new CosmosException(
            "support request already exists", HttpStatusCode.Conflict, 0, "support-request-test", 0);
        var request = CreateRequest();
        var response = new Mock<ItemResponse<SupportRequest>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns((SupportRequest)null!);
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(conflict);
        container.Setup(value => value.ReadItemAsync<SupportRequest>(
                request.Id,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(request));

        Assert.Equal("Cosmos returned an empty support request resource.", exception.Message);
        container.Verify(value => value.CreateItemAsync(
            It.IsAny<SupportRequest>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<SupportRequest>(
            request.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        response.VerifyGet(value => value.Resource, Times.Once);
        container.VerifyNoOtherCalls();
        response.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("request-other", "reporter-1")]
    [InlineData("request-1", "reporter-2")]
    public async Task CanonicalReadRejectsResourceForDifferentRequestOrReporter(
        string returnedRequestId,
        string returnedReporterId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var conflict = new CosmosException(
            "support request already exists", HttpStatusCode.Conflict, 0, "support-request-test", 0);
        var request = CreateRequest();
        var existing = request with
        {
            Id = returnedRequestId,
            ReporterId = returnedReporterId
        };
        var response = new Mock<ItemResponse<SupportRequest>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(existing);
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(conflict);
        container.Setup(value => value.ReadItemAsync<SupportRequest>(
                request.Id,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(request));

        Assert.Equal("Cosmos returned a malformed support request resource.", exception.Message);
        container.Verify(value => value.CreateItemAsync(
            It.IsAny<SupportRequest>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<SupportRequest>(
            request.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        response.VerifyGet(value => value.Resource, Times.Once);
        container.VerifyNoOtherCalls();
        response.VerifyNoOtherCalls();
    }

}
