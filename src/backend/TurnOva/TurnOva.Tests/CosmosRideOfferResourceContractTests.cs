using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosRideOfferResourceContractTests
{
    [Fact]
    public async Task GetByRideRejectsNullResourceInsteadOfReturningAnEmptyOffer()
    {
        var (container, response) = CreateNullResourceRead("ride-1");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetByRideAsync("ride-1"));

        Assert.Equal("Cosmos returned an empty ride offer resource.", exception.Message);
        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            "ride-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        response.VerifyGet(value => value.Resource, Times.Once);
        container.VerifyNoOtherCalls();
        response.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetExpiredRejectsNullHydratedPointReadResource()
    {
        var state = CreateState();
        var (container, iterator, response) = CreateQueryWithNullRead(state);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetExpiredAsync(DateTimeOffset.UtcNow, 1));

        Assert.Equal("Cosmos returned an empty ride offer resource.", exception.Message);
        VerifyHydratedRead(container, iterator, response, state.RideId);
    }

    [Fact]
    public async Task GetPendingNotificationsRejectsNullHydratedPointReadResource()
    {
        var state = CreateState();
        var (container, iterator, response) = CreateQueryWithNullRead(state);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetPendingNotificationsAsync(1));

        Assert.Equal("Cosmos returned an empty ride offer resource.", exception.Message);
        VerifyHydratedRead(container, iterator, response, state.RideId);
    }

    [Fact]
    public async Task GetPendingForDriverRejectsNullQueryItemInsteadOfReportingNoOffer()
    {
        var (container, iterator) = CreateQueryWithNullItem();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetPendingForDriverAsync("driver-1"));

        Assert.Equal("Cosmos returned an empty ride offer query item.", exception.Message);
        VerifyNullQueryOnly(container, iterator);
    }

    [Fact]
    public async Task GetPendingForDriverReturnsTheSinglePendingOffer()
    {
        var state = CreateState();
        var (container, iterator) = CreateQueryWithItem(state);

        var result = await new CosmosRideOfferStore(container.Object).GetPendingForDriverAsync("driver-1");

        Assert.Equal(state, result);
        container.Verify(value => value.GetItemQueryIterator<RideOfferState>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        iterator.Verify(value => value.HasMoreResults, Times.Exactly(2));
        iterator.Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
        iterator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPendingForDriverRejectsMultiplePendingOffersAcrossPages()
    {
        var first = CreateState() with { RideId = "ride-first" };
        var second = CreateState() with { RideId = "ride-second" };
        var (container, iterator) = CreateQueryWithTwoPages(first, second);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetPendingForDriverAsync("driver-1"));

        Assert.Equal("Cosmos returned multiple pending ride offers for the driver.", exception.Message);
        container.Verify(value => value.GetItemQueryIterator<RideOfferState>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        iterator.Verify(value => value.HasMoreResults, Times.Exactly(2));
        iterator.Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        container.VerifyNoOtherCalls();
        iterator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetExpiredRejectsNullQueryItemBeforeHydratingRideOffer()
    {
        var (container, iterator) = CreateQueryWithNullItem();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetExpiredAsync(DateTimeOffset.UtcNow, 1));

        Assert.Equal("Cosmos returned an empty ride offer query item.", exception.Message);
        VerifyNullQueryOnly(container, iterator);
    }

    [Fact]
    public async Task GetPendingNotificationsRejectsNullQueryItemBeforeHydratingRideOffer()
    {
        var (container, iterator) = CreateQueryWithNullItem();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetPendingNotificationsAsync(1));

        Assert.Equal("Cosmos returned an empty ride offer query item.", exception.Message);
        VerifyNullQueryOnly(container, iterator);
    }

    [Fact]
    public async Task GetByRideRejectsResourceForDifferentRideId()
    {
        const string requestedRideId = "ride-requested";
        var returnedState = CreateState() with { RideId = "ride-other" };
        var (container, response) = CreateResourceRead(requestedRideId, returnedState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetByRideAsync(requestedRideId));

        Assert.Equal("Cosmos returned a malformed ride offer resource.", exception.Message);
        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            requestedRideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        response.VerifyGet(value => value.Resource, Times.Once);
        container.VerifyNoOtherCalls();
        response.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPendingForDriverRejectsQueryItemForDifferentDriver()
    {
        var state = CreateState() with { DriverId = "driver-2" };
        var (container, iterator) = CreateQueryWithItem(state);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetPendingForDriverAsync("driver-1"));

        Assert.Equal("Cosmos returned a malformed ride offer query item.", exception.Message);
        VerifyQueryOnly(container, iterator);
    }

    [Fact]
    public async Task GetExpiredRejectsQueryItemWithBlankRideIdBeforeHydrating()
    {
        var state = CreateState() with { RideId = " " };
        var (container, iterator) = CreateQueryWithItem(state);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetExpiredAsync(DateTimeOffset.UtcNow, 1));

        Assert.Equal("Cosmos returned a malformed ride offer query item.", exception.Message);
        VerifyQueryOnly(container, iterator);
    }

    [Fact]
    public async Task GetExpiredRejectsHydratedResourceForDifferentRideId()
    {
        var candidate = CreateState();
        var returnedState = candidate with { RideId = "ride-other" };
        var (container, iterator, response) = CreateQueryWithResource(candidate, returnedState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetExpiredAsync(DateTimeOffset.UtcNow, 1));

        Assert.Equal("Cosmos returned a malformed ride offer resource.", exception.Message);
        VerifyHydratedRead(container, iterator, response, candidate.RideId);
    }

    [Fact]
    public async Task GetPendingNotificationsRejectsHydratedResourceForDifferentDriver()
    {
        var candidate = CreateState();
        var returnedState = candidate with { DriverId = "driver-2" };
        var (container, iterator, response) = CreateQueryWithResource(candidate, returnedState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRideOfferStore(container.Object).GetPendingNotificationsAsync(1));

        Assert.Equal("Cosmos returned a malformed ride offer resource.", exception.Message);
        VerifyHydratedRead(container, iterator, response, candidate.RideId);
    }

    private static (Mock<Container> Container, Mock<FeedIterator<RideOfferState>> Iterator)
        CreateQueryWithItem(RideOfferState item)
    {
        var page = new Mock<FeedResponse<RideOfferState>>(MockBehavior.Strict);
        page.As<IEnumerable<RideOfferState>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => new[] { item }.AsEnumerable().GetEnumerator());
        var iterator = new Mock<FeedIterator<RideOfferState>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(page.Object);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<RideOfferState>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return (container, iterator);
    }

    private static (Mock<Container> Container, Mock<FeedIterator<RideOfferState>> Iterator)
        CreateQueryWithTwoPages(RideOfferState first, RideOfferState second)
    {
        static Mock<FeedResponse<RideOfferState>> CreatePage(RideOfferState item)
        {
            var page = new Mock<FeedResponse<RideOfferState>>(MockBehavior.Strict);
            page.As<IEnumerable<RideOfferState>>()
                .Setup(value => value.GetEnumerator())
                .Returns(() => new[] { item }.AsEnumerable().GetEnumerator());
            return page;
        }

        var firstPage = CreatePage(first);
        var secondPage = CreatePage(second);
        var iterator = new Mock<FeedIterator<RideOfferState>>();
        iterator.SetupSequence(value => value.HasMoreResults)
            .Returns(true)
            .Returns(true)
            .Returns(false);
        iterator.SetupSequence(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPage.Object)
            .ReturnsAsync(secondPage.Object);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<RideOfferState>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return (container, iterator);
    }

    private static (Mock<Container> Container, Mock<ItemResponse<RideOfferState>> Response)
        CreateResourceRead(string requestedRideId, RideOfferState resource)
    {
        var response = new Mock<ItemResponse<RideOfferState>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(resource);
        response.SetupGet(value => value.ETag).Returns("observed-ride-offer-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RideOfferState>(
                requestedRideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        return (container, response);
    }

    private static (Mock<Container> Container, Mock<FeedIterator<RideOfferState>> Iterator,
        Mock<ItemResponse<RideOfferState>> Response) CreateQueryWithResource(
            RideOfferState candidate,
            RideOfferState resource)
    {
        var (container, iterator) = CreateQueryWithItem(candidate);
        var response = new Mock<ItemResponse<RideOfferState>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(resource);
        response.SetupGet(value => value.ETag).Returns("observed-ride-offer-etag");
        container.Setup(value => value.ReadItemAsync<RideOfferState>(
                candidate.RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        return (container, iterator, response);
    }

    private static void VerifyQueryOnly(
        Mock<Container> container,
        Mock<FeedIterator<RideOfferState>> iterator)
    {
        container.Verify(value => value.GetItemQueryIterator<RideOfferState>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (Mock<Container> Container, Mock<FeedIterator<RideOfferState>> Iterator)
        CreateQueryWithNullItem()
    {
        var page = new Mock<FeedResponse<RideOfferState>>(MockBehavior.Strict);
        page.As<IEnumerable<RideOfferState>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => new RideOfferState[] { null! }.AsEnumerable().GetEnumerator());
        var iterator = new Mock<FeedIterator<RideOfferState>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(page.Object);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<RideOfferState>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return (container, iterator);
    }

    private static void VerifyNullQueryOnly(
        Mock<Container> container,
        Mock<FeedIterator<RideOfferState>> iterator)
    {
        container.Verify(value => value.GetItemQueryIterator<RideOfferState>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (Mock<Container> Container, Mock<ItemResponse<RideOfferState>> Response)
        CreateNullResourceRead(string rideId)
    {
        var response = new Mock<ItemResponse<RideOfferState>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns((RideOfferState)null!);
        response.SetupGet(value => value.ETag).Returns("observed-ride-offer-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RideOfferState>(
                rideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        return (container, response);
    }

    private static (Mock<Container> Container, Mock<FeedIterator<RideOfferState>> Iterator,
        Mock<ItemResponse<RideOfferState>> Response) CreateQueryWithNullRead(RideOfferState state)
    {
        var page = new Mock<FeedResponse<RideOfferState>>(MockBehavior.Strict);
        page.As<IEnumerable<RideOfferState>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => new[] { state }.AsEnumerable().GetEnumerator());
        var iterator = new Mock<FeedIterator<RideOfferState>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(page.Object);
        var (container, response) = CreateNullResourceRead(state.RideId);
        container.Setup(value => value.GetItemQueryIterator<RideOfferState>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return (container, iterator, response);
    }

    private static void VerifyHydratedRead(
        Mock<Container> container,
        Mock<FeedIterator<RideOfferState>> iterator,
        Mock<ItemResponse<RideOfferState>> response,
        string rideId)
    {
        container.Verify(value => value.GetItemQueryIterator<RideOfferState>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            rideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        response.VerifyGet(value => value.Resource, Times.Once);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Once);
        response.VerifyNoOtherCalls();
    }

    private static RideOfferState CreateState()
    {
        var offeredAt = DateTimeOffset.Parse("2026-11-23T03:00:00Z");
        var expiresAt = offeredAt.AddMinutes(10);
        return new RideOfferState(
            "ride-resource-contract",
            "zone-a",
            "Cab",
            "offer-resource-contract",
            "driver-1",
            offeredAt,
            expiresAt,
            RideOfferStatuses.Pending,
            [new RideOfferAttempt("offer-resource-contract", "driver-1", offeredAt, expiresAt, RideOfferStatuses.Pending)]);
    }
}
