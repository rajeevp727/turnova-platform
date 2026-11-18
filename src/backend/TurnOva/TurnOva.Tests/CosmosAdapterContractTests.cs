using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosAdapterContractTests
{
    [Fact]
    public async Task RideOfferReplacementUsesExpectedEtag()
    {
        var container = new Mock<Container>();
        var response = new Mock<ItemResponse<RideOfferState>>();
        var state = new RideOfferState(
            "ride-1", "zone-1", "Cab", "offer-1", "driver-1",
            DateTimeOffset.Parse("2026-11-17T12:00:00Z"),
            DateTimeOffset.Parse("2026-11-17T12:10:00Z"),
            RideOfferStatuses.Pending, []);

        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<RideOfferState>(),
                It.IsAny<string>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var result = await new CosmosRideOfferStore(container.Object).TryReplaceAsync(state, "etag-expected");

        Assert.True(result);
        container.Verify(value => value.ReplaceItemAsync(
            It.Is<RideOfferState>(item => item.RideId == "ride-1" && item.ETag == null),
            "ride-1",
            It.Is<PartitionKey?>(partitionKey => partitionKey.HasValue && partitionKey.Value.Equals(new PartitionKey("ride-1"))),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "etag-expected"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DriverReservationAndAuditUseOneSamePartitionBatch()
    {
        var (container, batch, getPartitionKey) = CreateSuccessfulBatch();
        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);

        var result = await new CosmosDriverRideReservationStore(container.Object)
            .TryAcquireAsync("driver-1", "ride-1");

        Assert.True(result);
        Assert.Equal(new PartitionKey("driver-1"), getPartitionKey());
        batch.Verify(value => value.CreateItem(
            It.Is<DriverRideReservation>(item => item.DriverId == "driver-1" && item.RideId == "ride-1"),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<DriverRideReservationAuditEvent>(item =>
                item.DriverId == "driver-1" &&
                item.ReservationId == "ride-1" &&
                item.EventType == DriverRideReservationAuditEventTypes.Acquired),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RiderReservationAndAuditUseOneSamePartitionBatch()
    {
        var (container, batch, getPartitionKey) = CreateSuccessfulBatch();
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);

        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", "request-hash",
            DateTimeOffset.Parse("2026-11-17T12:00:00Z"), null);
        var result = await new CosmosRiderActiveBookingReservationStore(container.Object)
            .TryAcquireAsync(reservation);

        Assert.Equal(RiderBookingReservationAcquireResult.Acquired, result);
        Assert.Equal(new PartitionKey("rider-1"), getPartitionKey());
        batch.Verify(value => value.CreateItem(
            It.Is<RiderActiveBookingReservation>(item => item.RiderId == "rider-1" && item.RideId == "ride-1"),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<RiderActiveBookingReservationAuditEvent>(item =>
                item.RiderId == "rider-1" &&
                item.RideId == "ride-1" &&
                item.EventType == RiderActiveBookingReservationAuditEventTypes.Acquired),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (
        Mock<Container> Container,
        Mock<TransactionalBatch> Batch,
        Func<PartitionKey?> GetPartitionKey) CreateSuccessfulBatch()
    {
        var container = new Mock<Container>();
        var batch = new Mock<TransactionalBatch>();
        var response = new Mock<TransactionalBatchResponse>();
        response.SetupGet(value => value.IsSuccessStatusCode).Returns(true);
        batch.Setup(value => value.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        PartitionKey? captured = null;
        container.Setup(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Callback<PartitionKey>(partitionKey => captured = partitionKey)
            .Returns(batch.Object);

        return (container, batch, () => captured);
    }
}
