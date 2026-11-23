using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
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

    [Fact]
    public async Task QueueEligibilityReturnsValidatedCabQueueEntry()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "Hyderabad-Central",
            ServiceClass = "Cab",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.Parse("2026-11-23T00:00:00Z"),
            Sequence = 1,
            IsEligible = true
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var entries = await new CosmosQueueStore(container.Object).GetEligibleAsync(
            "hyderabad-central", "cab", CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal("driver-1", entry.DriverId);
        Assert.Equal("Hyderabad-Central", entry.ZoneId);
        Assert.Equal("Cab", entry.ServiceClass);
        Assert.True(entry.IsEligible);
        iterator.Verify(value => value.HasMoreResults, Times.Exactly(2));
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsNullQueryItemInsteadOfExposingDriver()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage<CosmosQueueStore.QueueEntryDocument>(new CosmosQueueStore.QueueEntryDocument[] { null! });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsQueryItemWhoseIdentityDoesNotMatchDocumentId()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "different-driver",
            DriverId = "driver-1",
            ZoneId = "hyderabad-central",
            ServiceClass = "cab",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.UtcNow,
            Sequence = 1,
            IsEligible = true
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsQueryItemForAnotherZoneOrServiceClass()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "hyderabad-central",
            ServiceClass = "Bike",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.UtcNow,
            Sequence = 1,
            IsEligible = true
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsQueryItemThatIsNoLongerEligible()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "hyderabad-central",
            ServiceClass = "cab",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.UtcNow,
            Sequence = 1,
            IsEligible = false
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueAuditReadRejectsNullQueryItemInsteadOfReturningMalformedAuditHistory()
    {
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage<QueueAuditEvent>(new QueueAuditEvent[] { null! });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue audit event query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Theory]
    [InlineData("", "hyderabad-central|cab", "driver-1", "Joined")]
    [InlineData("event-1", "another-zone|cab", "driver-1", "Joined")]
    [InlineData("event-1", "hyderabad-central|cab", "", "Joined")]
    [InlineData("event-1", "hyderabad-central|cab", "driver-1", "")]
    public async Task QueueAuditReadRejectsMalformedOrCrossPartitionQueryItem(
        string eventId,
        string zoneServiceKey,
        string driverId,
        string eventType)
    {
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new QueueAuditEvent(
            eventId, zoneServiceKey, driverId, eventType, DateTimeOffset.UtcNow));
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue audit event query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    private static Mock<Container> CreateQueryContainer<T>(Mock<FeedIterator<T>> iterator)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<T>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string?>(),
                It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return container;
    }

    private static Mock<FeedResponse<T>> CreatePage<T>(params T[] items)
    {
        var page = new Mock<FeedResponse<T>>(MockBehavior.Strict);
        page.As<IEnumerable<T>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => items.AsEnumerable().GetEnumerator());
        return page;
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
