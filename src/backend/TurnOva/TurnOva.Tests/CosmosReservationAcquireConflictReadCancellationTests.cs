using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosReservationAcquireConflictReadCancellationTests
{
    [Fact]
    public async Task DriverAcquireConflictReadCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var (container, batch) = CreateConflictBatch();
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverRideReservation>>(Unavailable());
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryAcquireAsync(
                "driver-1", "ride-1", cancellation.Token));

        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("driver-1")))), Times.Once);
        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(cancellation.Token), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.IsAny<DriverRideReservation>(),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.IsAny<DriverRideReservationAuditEvent>(),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task RiderAcquireConflictReadCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var (container, batch) = CreateConflictBatch();
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", "request-hash",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15));
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<RiderActiveBookingReservation>>(Unavailable());
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryAcquireAsync(
                reservation, cancellation.Token));

        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("rider-1")))), Times.Once);
        container.Verify(value => value.ReadItemAsync<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(cancellation.Token), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.IsAny<RiderActiveBookingReservation>(),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.IsAny<RiderActiveBookingReservationAuditEvent>(),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task DriverAcquireRetriesWhenConflictReadbackIsNotFound()
    {
        var (container, batch) = CreateRetryableConflictBatch();
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<ItemResponse<DriverRideReservation>>(NotFound()));

        var acquired = await new CosmosDriverRideReservationStore(container.Object)
            .TryAcquireAsync("driver-1", "ride-1");

        Assert.True(acquired);
        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("driver-1")))), Times.Exactly(2));
        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        batch.Verify(value => value.CreateItem(
            It.IsAny<DriverRideReservation>(),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Exactly(2));
        batch.Verify(value => value.CreateItem(
            It.IsAny<DriverRideReservationAuditEvent>(),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RiderAcquireRetriesWhenConflictReadbackIsNotFound()
    {
        var (container, batch) = CreateRetryableConflictBatch();
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", "request-hash",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15));
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<ItemResponse<RiderActiveBookingReservation>>(NotFound()));

        var acquired = await new CosmosRiderActiveBookingReservationStore(container.Object)
            .TryAcquireAsync(reservation);

        Assert.Equal(RiderBookingReservationAcquireResult.Acquired, acquired);
        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("rider-1")))), Times.Exactly(2));
        container.Verify(value => value.ReadItemAsync<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        batch.Verify(value => value.CreateItem(
            It.IsAny<RiderActiveBookingReservation>(),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Exactly(2));
        batch.Verify(value => value.CreateItem(
            It.IsAny<RiderActiveBookingReservationAuditEvent>(),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Exactly(2));
    }

    private static (Mock<Container> Container, Mock<TransactionalBatch> Batch) CreateRetryableConflictBatch()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var batch = new Mock<TransactionalBatch>(MockBehavior.Strict);
        var operation = new Mock<TransactionalBatchOperationResult>(MockBehavior.Strict);
        operation.SetupGet(value => value.StatusCode).Returns(HttpStatusCode.Conflict);

        var conflict = new Mock<TransactionalBatchResponse>();
        conflict.SetupGet(value => value.IsSuccessStatusCode).Returns(false);
        conflict.Setup(value => value[0]).Returns(operation.Object);

        var success = new Mock<TransactionalBatchResponse>();
        success.SetupGet(value => value.IsSuccessStatusCode).Returns(true);

        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.SetupSequence(value => value.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(conflict.Object)
            .ReturnsAsync(success.Object);
        container.Setup(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Returns(batch.Object);

        return (container, batch);
    }

    private static CosmosException NotFound() =>
        new("Reservation disappeared after the create conflict.",
            HttpStatusCode.NotFound, 0, "reservation-acquire-conflict-readback-test", 0);

    private static (Mock<Container> Container, Mock<TransactionalBatch> Batch) CreateConflictBatch()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var batch = new Mock<TransactionalBatch>(MockBehavior.Strict);
        var operation = new Mock<TransactionalBatchOperationResult>(MockBehavior.Strict);
        operation.SetupGet(value => value.StatusCode).Returns(HttpStatusCode.Conflict);

        var response = new Mock<TransactionalBatchResponse>();
        response.SetupGet(value => value.IsSuccessStatusCode).Returns(false);
        response.Setup(value => value[0]).Returns(operation.Object);

        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.Setup(value => value.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        container.Setup(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Returns(batch.Object);

        return (container, batch);
    }

    private static CosmosException Unavailable() =>
        new("Cosmos is unavailable.", HttpStatusCode.ServiceUnavailable, 0,
            "reservation-acquire-conflict-cancellation-test", 0);
}
