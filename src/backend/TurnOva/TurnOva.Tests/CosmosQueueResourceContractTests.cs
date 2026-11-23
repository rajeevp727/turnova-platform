using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosQueueResourceContractTests
{
    [Fact]
    public async Task QueueRemovalRejectsNullResourceBeforeStartingDeleteBatch()
    {
        var (container, response) = CreateNullResourceRead();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).RemoveAsync(
                "driver-1", "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue entry resource.", exception.Message);
        VerifyReadOnly(container, response);
    }

    [Fact]
    public async Task QueueRotationRejectsNullResourceBeforeStartingReplaceBatch()
    {
        var (container, response) = CreateNullResourceRead();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackAsync(
                "driver-1", "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue entry resource.", exception.Message);
        VerifyReadOnly(container, response);
    }

    [Fact]
    public async Task OfferScopedQueueRotationRejectsNullResourceBeforeStartingReplaceBatch()
    {
        var (container, response) = CreateNullResourceRead();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackOnceAsync(
                "driver-1", "hyderabad-central", "cab", "offer-1", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue entry resource.", exception.Message);
        VerifyReadOnly(container, response);
    }

    private static (Mock<Container> Container, Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>> Response)
        CreateNullResourceRead()
    {
        var response = new Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource)
            .Returns((CosmosQueueStore.QueueEntryDocument)null!);
        response.SetupGet(value => value.ETag).Returns("observed-queue-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        return (container, response);
    }

    private static void VerifyReadOnly(
        Mock<Container> container,
        Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>> response)
    {
        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        response.VerifyGet(value => value.Resource, Times.Once);
        container.VerifyNoOtherCalls();
        response.VerifyNoOtherCalls();
    }
}
