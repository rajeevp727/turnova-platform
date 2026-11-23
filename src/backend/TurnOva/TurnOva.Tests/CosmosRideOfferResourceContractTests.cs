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
        iterator.Verify(value => value.Dispose(), Times.Once);
        container.VerifyNoOtherCalls();
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
