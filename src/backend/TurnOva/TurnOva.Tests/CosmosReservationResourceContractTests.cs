using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosReservationResourceContractTests
{
    [Fact]
    public async Task DriverActiveReadRejectsNullResourceInsteadOfReportingNoReservation()
    {
        var response = new Mock<ItemResponse<DriverRideReservation>>();
        response.SetupGet(value => value.Resource).Returns((DriverRideReservation)null!);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosDriverRideReservationStore(container.Object).GetActiveAsync("driver-1"));

        Assert.Equal("Cosmos returned an empty driver reservation resource.", exception.Message);
        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RiderActiveReadRejectsNullResourceInsteadOfReportingNoReservation()
    {
        var response = new Mock<ItemResponse<RiderActiveBookingReservation>>();
        response.SetupGet(value => value.Resource).Returns((RiderActiveBookingReservation)null!);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).GetActiveAsync("rider-1"));

        Assert.Equal("Cosmos returned an empty Rider booking reservation resource.", exception.Message);
        container.Verify(value => value.ReadItemAsync<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DriverBatchReadRejectsNullQueryItemInsteadOfReturningMalformedReservation()
    {
        var page = new Mock<FeedResponse<DriverRideReservation>>(MockBehavior.Strict);
        page.As<IEnumerable<DriverRideReservation>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => new DriverRideReservation[] { null! }.AsEnumerable().GetEnumerator());
        var iterator = new Mock<FeedIterator<DriverRideReservation>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(page.Object);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<DriverRideReservation>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosDriverRideReservationStore(container.Object)
                .GetActiveForDriversAsync(["driver-1"]));

        Assert.Equal("Cosmos returned an empty driver reservation query item.", exception.Message);
        container.Verify(value => value.GetItemQueryIterator<DriverRideReservation>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DriverReleaseRejectsNullResourceBeforeStartingDeleteBatch()
    {
        var response = new Mock<ItemResponse<DriverRideReservation>>();
        response.SetupGet(value => value.Resource).Returns((DriverRideReservation)null!);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryReleaseAsync("driver-1", "ride-1"));

        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RiderReleaseRejectsNullResourceBeforeStartingDeleteBatch()
    {
        var response = new Mock<ItemResponse<RiderActiveBookingReservation>>();
        response.SetupGet(value => value.Resource).Returns((RiderActiveBookingReservation)null!);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryReleaseAsync("rider-1", "ride-1"));

        container.Verify(value => value.ReadItemAsync<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DriverActiveReadRejectsResourceForDifferentDriver()
    {
        var response = new Mock<ItemResponse<DriverRideReservation>>();
        response.SetupGet(value => value.Resource)
            .Returns(new DriverRideReservation("different-driver", "ride-1", DateTimeOffset.UtcNow));
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosDriverRideReservationStore(container.Object).GetActiveAsync("driver-1"));

        Assert.Equal("Cosmos returned a malformed driver reservation resource.", exception.Message);
        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DriverReleaseRejectsResourceForDifferentDriverBeforeStartingDeleteBatch()
    {
        var response = new Mock<ItemResponse<DriverRideReservation>>();
        response.SetupGet(value => value.Resource)
            .Returns(new DriverRideReservation("different-driver", "ride-1", DateTimeOffset.UtcNow));
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryReleaseAsync("driver-1", "ride-1"));

        Assert.Equal("Cosmos returned a malformed driver reservation resource.", exception.Message);
        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DriverBatchReadRejectsQueryItemOutsideRequestedDriverBatch()
    {
        var page = new Mock<FeedResponse<DriverRideReservation>>(MockBehavior.Strict);
        page.As<IEnumerable<DriverRideReservation>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => new[] { new DriverRideReservation("different-driver", "ride-1", DateTimeOffset.UtcNow) }
                .AsEnumerable().GetEnumerator());
        var iterator = new Mock<FeedIterator<DriverRideReservation>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(page.Object);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<DriverRideReservation>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosDriverRideReservationStore(container.Object).GetActiveForDriversAsync(["driver-1"]));

        Assert.Equal("Cosmos returned a malformed driver reservation query item.", exception.Message);
        container.Verify(value => value.GetItemQueryIterator<DriverRideReservation>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
    }

    [Fact]
    public async Task RiderActiveReadRejectsResourceForDifferentRider()
    {
        var response = new Mock<ItemResponse<RiderActiveBookingReservation>>();
        response.SetupGet(value => value.Resource)
            .Returns(new RiderActiveBookingReservation(
                "different-rider", "ride-1", "request-hash", DateTimeOffset.UtcNow, null));
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).GetActiveAsync("rider-1"));

        Assert.Equal("Cosmos returned a malformed Rider booking reservation resource.", exception.Message);
        container.Verify(value => value.ReadItemAsync<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RiderReleaseRejectsResourceForDifferentRiderBeforeStartingDeleteBatch()
    {
        var response = new Mock<ItemResponse<RiderActiveBookingReservation>>();
        response.SetupGet(value => value.Resource)
            .Returns(new RiderActiveBookingReservation(
                "different-rider", "ride-1", "request-hash", DateTimeOffset.UtcNow, null));
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryReleaseAsync("rider-1", "ride-1"));

        Assert.Equal("Cosmos returned a malformed Rider booking reservation resource.", exception.Message);
        container.Verify(value => value.ReadItemAsync<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

}
