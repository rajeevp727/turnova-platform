using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosReservationCancellationTests
{
    [Fact]
    public async Task DriverAcquireHonorsCancellationBeforeExpiredReservationNoOp()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryAcquireTemporaryAsync(
                "driver-1", "ride-1", DateTimeOffset.UtcNow.AddMinutes(-1), cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DriverAcquireRemainsSuccessfulWhenCancellationArrivesAfterAtomicCommit()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var batch = new Mock<TransactionalBatch>(MockBehavior.Strict);
        var response = new Mock<TransactionalBatchResponse>();
        response.SetupGet(value => value.IsSuccessStatusCode).Returns(true);
        using var cancellation = new CancellationTokenSource();
        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.ExecuteAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(response.Object);
            });
        container.Setup(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Returns(batch.Object);

        var result = await new CosmosDriverRideReservationStore(container.Object)
            .TryAcquireTemporaryAsync(
                "driver-1", "ride-1", DateTimeOffset.UtcNow.AddMinutes(5), cancellation.Token);

        Assert.True(result);
        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("driver-1")))), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<DriverRideReservation>(item => item.DriverId == "driver-1" && item.RideId == "ride-1"),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<DriverRideReservationAuditEvent>(item =>
                item.DriverId == "driver-1" && item.ReservationId == "ride-1" &&
                item.EventType == DriverRideReservationAuditEventTypes.Acquired),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(cancellation.Token), Times.Once);
        container.VerifyNoOtherCalls();
        batch.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DriverPromotionHonorsCancellationAfterReadBeforeAlreadyPermanentNoOp()
    {
        var read = new Mock<ItemResponse<DriverRideReservation>>();
        read.SetupGet(value => value.Resource)
            .Returns(new DriverRideReservation("driver-1", "ride-1", DateTimeOffset.UtcNow));
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryMakePermanentAsync(
                "driver-1", "ride-1", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DriverExpiryUpdateHonorsCancellationAfterReadBeforeUnchangedNoOp()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
        var read = new Mock<ItemResponse<DriverRideReservation>>();
        read.SetupGet(value => value.Resource)
            .Returns(new DriverRideReservation("driver-1", "ride-1", DateTimeOffset.UtcNow, expiry));
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TrySetExpiryAsync(
                "driver-1", "ride-1", expiry, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DriverReleaseHonorsCancellationAfterReadBeforeMismatchedRideNoOp()
    {
        var read = new Mock<ItemResponse<DriverRideReservation>>();
        read.SetupGet(value => value.Resource)
            .Returns(new DriverRideReservation("driver-1", "another-ride", DateTimeOffset.UtcNow));
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryReleaseAsync(
                "driver-1", "ride-1", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverRideReservation>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DriverReadHonorsPreCancelledTokenWithoutCallingCosmos()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object).GetActiveAsync("driver-1", cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RiderAcquireHonorsCancellationBeforeExpiredReservationNoOp()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", "request-hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryAcquireAsync(
                reservation, cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RiderAcquireRemainsSuccessfulWhenCancellationArrivesAfterAtomicCommit()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var batch = new Mock<TransactionalBatch>(MockBehavior.Strict);
        var response = new Mock<TransactionalBatchResponse>();
        response.SetupGet(value => value.IsSuccessStatusCode).Returns(true);
        using var cancellation = new CancellationTokenSource();
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", "request-hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.ExecuteAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(response.Object);
            });
        container.Setup(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Returns(batch.Object);

        var result = await new CosmosRiderActiveBookingReservationStore(container.Object)
            .TryAcquireAsync(reservation, cancellation.Token);

        Assert.Equal(RiderBookingReservationAcquireResult.Acquired, result);
        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("rider-1")))), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<RiderActiveBookingReservation>(item =>
                item.RiderId == "rider-1" && item.RideId == "ride-1"),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<RiderActiveBookingReservationAuditEvent>(item =>
                item.RiderId == "rider-1" && item.RideId == "ride-1" &&
                item.EventType == RiderActiveBookingReservationAuditEventTypes.Acquired),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(cancellation.Token), Times.Once);
        container.VerifyNoOtherCalls();
        batch.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RiderPromotionHonorsCancellationAfterReadBeforeAlreadyPermanentNoOp()
    {
        var read = new Mock<ItemResponse<RiderActiveBookingReservation>>();
        read.SetupGet(value => value.Resource)
            .Returns(new RiderActiveBookingReservation(
                "rider-1", "ride-1", "request-hash", DateTimeOffset.UtcNow, null));
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryMakePermanentAsync(
                "rider-1", "ride-1", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RiderReleaseHonorsCancellationAfterReadBeforeMismatchedRideNoOp()
    {
        var read = new Mock<ItemResponse<RiderActiveBookingReservation>>();
        read.SetupGet(value => value.Resource)
            .Returns(new RiderActiveBookingReservation(
                "rider-1", "another-ride", "request-hash", DateTimeOffset.UtcNow, null));
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryReleaseAsync(
                "rider-1", "ride-1", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RiderReadHonorsPreCancelledTokenWithoutCallingCosmos()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).GetActiveAsync(
                "rider-1", cancellation.Token));

        container.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task DriverBatchReadStopsBeforeCheckingNextPageWhenCancellationArrivesAfterFirstPage()
    {
        using var cancellation = new CancellationTokenSource();
        var page = CreateDriverReservationPage(new DriverRideReservation(
            "driver-1", "ride-1", DateTimeOffset.UtcNow));
        var iterator = new Mock<FeedIterator<DriverRideReservation>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(true);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(page.Object);
            });
        var container = CreateDriverReservationQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object).GetActiveForDriversAsync(
                ["driver-1"], cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<DriverRideReservation>(
            It.IsAny<QueryDefinition>(),
            It.IsAny<string?>(),
            It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task DriverBatchReadHonorsCancellationAfterFinalPageBeforeReturningReservations()
    {
        using var cancellation = new CancellationTokenSource();
        var page = CreateDriverReservationPage(new DriverRideReservation(
            "driver-1", "ride-1", DateTimeOffset.UtcNow));
        var iterator = new Mock<FeedIterator<DriverRideReservation>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(page.Object);
            });
        var container = CreateDriverReservationQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object).GetActiveForDriversAsync(
                ["driver-1"], cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<DriverRideReservation>(
            It.IsAny<QueryDefinition>(),
            It.IsAny<string?>(),
            It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    private static Mock<Container> CreateDriverReservationQueryContainer(
        Mock<FeedIterator<DriverRideReservation>> iterator)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<DriverRideReservation>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string?>(),
                It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return container;
    }

    private static Mock<FeedResponse<DriverRideReservation>> CreateDriverReservationPage(
        DriverRideReservation reservation)
    {
        var page = new Mock<FeedResponse<DriverRideReservation>>(MockBehavior.Strict);
        page.As<IEnumerable<DriverRideReservation>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => new[] { reservation }.AsEnumerable().GetEnumerator());
        return page;
    }

}
