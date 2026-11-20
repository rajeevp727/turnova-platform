using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.API;
using Xunit;

namespace TurnOva.Tests;

public sealed class CosmosCreateConflictReadTests
{
    private sealed record TestDocument(string Id);

    [Fact]
    public async Task ReadAfterConflictReturnsResourceWhenReservationStillExists()
    {
        var document = new TestDocument("idempotency-key");
        var response = new Mock<ItemResponse<TestDocument>>();
        response.SetupGet(value => value.Resource).Returns(document);
        var container = new Mock<Container>(MockBehavior.Strict);
        PartitionKey? observedPartitionKey = null;
        CancellationToken observedCancellationToken = default;
        container.Setup(value => value.ReadItemAsync<TestDocument>(
                "idempotency-key",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PartitionKey, ItemRequestOptions?, CancellationToken>(
                (_, partitionKey, _, cancellationToken) =>
                {
                    observedPartitionKey = partitionKey;
                    observedCancellationToken = cancellationToken;
                })
            .ReturnsAsync(response.Object);
        using var cancellation = new CancellationTokenSource();

        var result = await CosmosCreateConflictRead.ReadAfterCreateConflictAsync<TestDocument>(
            container.Object,
            "idempotency-key",
            new PartitionKey("rider-1"),
            cancellation.Token);

        Assert.Same(document, result);
        Assert.True(observedPartitionKey.HasValue);
        Assert.Equal(new PartitionKey("rider-1"), observedPartitionKey.Value);
        Assert.Equal(cancellation.Token, observedCancellationToken);
        container.Verify(value => value.ReadItemAsync<TestDocument>(
            "idempotency-key",
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadAfterConflictReturnsNullWhenReservationWasDeleted()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<TestDocument>(
                "idempotency-key",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "reservation was concurrently deleted",
                HttpStatusCode.NotFound,
                0,
                "idempotency-conflict-test",
                0));

        var result = await CosmosCreateConflictRead.ReadAfterCreateConflictAsync<TestDocument>(
            container.Object,
            "idempotency-key",
            new PartitionKey("rider-1"),
            CancellationToken.None);

        Assert.Null(result);
        container.Verify(value => value.ReadItemAsync<TestDocument>(
            "idempotency-key",
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadAfterConflictPropagatesNonNotFoundProviderFailures()
    {
        var failure = new CosmosException(
            "cosmos unavailable",
            HttpStatusCode.ServiceUnavailable,
            0,
            "idempotency-conflict-test",
            0);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<TestDocument>(
                "idempotency-key",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var thrown = await Assert.ThrowsAsync<CosmosException>(() =>
            CosmosCreateConflictRead.ReadAfterCreateConflictAsync<TestDocument>(
                container.Object,
                "idempotency-key",
                new PartitionKey("rider-1"),
                CancellationToken.None));

        Assert.Same(failure, thrown);
    }
}
