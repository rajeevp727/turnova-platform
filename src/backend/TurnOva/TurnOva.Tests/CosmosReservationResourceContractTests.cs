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
}
