using System.Net;
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
    public async Task RideOfferCreateConflictReturnsFalseWithoutOverwritingTheExistingAggregate()
    {
        var state = CreateRideOfferState() with { ETag = "stale-etag" };
        var container = new Mock<Container>();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<RideOfferState>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "ride offer already exists", HttpStatusCode.Conflict, 0, "ride-offer-create-test", 0));

        var result = await new CosmosRideOfferStore(container.Object).TryCreateAsync(state);

        Assert.False(result);
        container.Verify(value => value.CreateItemAsync(
            It.Is<RideOfferState>(item => item.RideId == state.RideId && item.ETag == null),
            It.Is<PartitionKey?>(partitionKey =>
                partitionKey.HasValue && partitionKey.Value.Equals(new PartitionKey(state.RideId))),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RideOfferCreatePropagatesNonConflictCosmosFailures()
    {
        var state = CreateRideOfferState();
        var container = new Mock<Container>();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<RideOfferState>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "cosmos unavailable", HttpStatusCode.ServiceUnavailable, 0, "ride-offer-create-test", 0));

        var exception = await Assert.ThrowsAsync<CosmosException>(() =>
            new CosmosRideOfferStore(container.Object).TryCreateAsync(state));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task RideOfferReplacementReturnsFalseForStaleEtagOrMissingItem(HttpStatusCode statusCode)
    {
        var state = CreateRideOfferState() with { Status = RideOfferStatuses.Rejected };
        var container = new Mock<Container>();
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<RideOfferState>(),
                It.IsAny<string>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "ride offer changed", statusCode, 0, "ride-offer-replace-test", 0));

        var result = await new CosmosRideOfferStore(container.Object)
            .TryReplaceAsync(state, "expected-etag");

        Assert.False(result);
        container.Verify(value => value.ReplaceItemAsync(
            It.Is<RideOfferState>(item => item.RideId == state.RideId && item.ETag == null),
            state.RideId,
            It.Is<PartitionKey?>(partitionKey =>
                partitionKey.HasValue && partitionKey.Value.Equals(new PartitionKey(state.RideId))),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "expected-etag"),
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

    [Fact]
    public async Task DriverReservationPromotionUsesObservedEtagAndAppendsAuditAtomically()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var reservation = new DriverRideReservation("driver-1", "ride-1", DateTimeOffset.UtcNow, expiresAt);
        var read = new Mock<ItemResponse<DriverRideReservation>>();
        read.SetupGet(value => value.Resource).Returns(reservation);
        read.SetupGet(value => value.ETag).Returns("driver-reservation-etag");
        var (container, batch, getPartitionKey) = CreateSuccessfulBatch();
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        batch.Setup(value => value.ReplaceItem(
                "driver-1",
                It.IsAny<DriverRideReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);

        var result = await new CosmosDriverRideReservationStore(container.Object)
            .TryMakePermanentAsync("driver-1", "ride-1");

        Assert.True(result);
        Assert.Equal(new PartitionKey("driver-1"), getPartitionKey());
        batch.Verify(value => value.ReplaceItem(
            "driver-1",
            It.Is<DriverRideReservation>(item =>
                item.DriverId == "driver-1" && item.RideId == "ride-1" && item.ExpiresAtUtc is null),
            It.Is<TransactionalBatchItemRequestOptions>(options =>
                options.IfMatchEtag == "driver-reservation-etag")), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<DriverRideReservationAuditEvent>(item =>
                item.EventType == DriverRideReservationAuditEventTypes.MadePermanent &&
                item.PreviousExpiresAtUtc == expiresAt &&
                item.ExpiresAtUtc is null),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DriverReservationReleaseUsesObservedEtagAndAppendsAuditAtomically()
    {
        var reservation = new DriverRideReservation("driver-1", "ride-1", DateTimeOffset.UtcNow);
        var read = new Mock<ItemResponse<DriverRideReservation>>();
        read.SetupGet(value => value.Resource).Returns(reservation);
        read.SetupGet(value => value.ETag).Returns("driver-release-etag");
        var (container, batch, getPartitionKey) = CreateSuccessfulBatch();
        container.Setup(value => value.ReadItemAsync<DriverRideReservation>(
                "driver-1",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        batch.Setup(value => value.DeleteItem(
                "driver-1",
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<DriverRideReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);

        var result = await new CosmosDriverRideReservationStore(container.Object)
            .TryReleaseAsync("driver-1", "ride-1");

        Assert.True(result);
        Assert.Equal(new PartitionKey("driver-1"), getPartitionKey());
        batch.Verify(value => value.DeleteItem(
            "driver-1",
            It.Is<TransactionalBatchItemRequestOptions>(options =>
                options.IfMatchEtag == "driver-release-etag")), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<DriverRideReservationAuditEvent>(item =>
                item.EventType == DriverRideReservationAuditEventTypes.Released &&
                item.DriverId == "driver-1" && item.ReservationId == "ride-1"),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RiderReservationPromotionUsesObservedEtagAndAppendsAuditAtomically()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", "request-hash", DateTimeOffset.UtcNow, expiresAt);
        var read = new Mock<ItemResponse<RiderActiveBookingReservation>>();
        read.SetupGet(value => value.Resource).Returns(reservation);
        read.SetupGet(value => value.ETag).Returns("rider-reservation-etag");
        var (container, batch, getPartitionKey) = CreateSuccessfulBatch();
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        batch.Setup(value => value.ReplaceItem(
                RiderActiveBookingReservation.DocumentId,
                It.IsAny<RiderActiveBookingReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);

        var result = await new CosmosRiderActiveBookingReservationStore(container.Object)
            .TryMakePermanentAsync("rider-1", "ride-1");

        Assert.True(result);
        Assert.Equal(new PartitionKey("rider-1"), getPartitionKey());
        batch.Verify(value => value.ReplaceItem(
            RiderActiveBookingReservation.DocumentId,
            It.Is<RiderActiveBookingReservation>(item =>
                item.RiderId == "rider-1" && item.RideId == "ride-1" && item.ExpiresAtUtc is null),
            It.Is<TransactionalBatchItemRequestOptions>(options =>
                options.IfMatchEtag == "rider-reservation-etag")), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<RiderActiveBookingReservationAuditEvent>(item =>
                item.EventType == RiderActiveBookingReservationAuditEventTypes.MadePermanent &&
                item.PreviousExpiresAtUtc == expiresAt &&
                item.ExpiresAtUtc is null),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RiderReservationReleaseUsesObservedEtagAndAppendsAuditAtomically()
    {
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", "request-hash", DateTimeOffset.UtcNow, null);
        var read = new Mock<ItemResponse<RiderActiveBookingReservation>>();
        read.SetupGet(value => value.Resource).Returns(reservation);
        read.SetupGet(value => value.ETag).Returns("rider-release-etag");
        var (container, batch, getPartitionKey) = CreateSuccessfulBatch();
        container.Setup(value => value.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        batch.Setup(value => value.DeleteItem(
                RiderActiveBookingReservation.DocumentId,
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<RiderActiveBookingReservationAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);

        var result = await new CosmosRiderActiveBookingReservationStore(container.Object)
            .TryReleaseAsync("rider-1", "ride-1");

        Assert.True(result);
        Assert.Equal(new PartitionKey("rider-1"), getPartitionKey());
        batch.Verify(value => value.DeleteItem(
            RiderActiveBookingReservation.DocumentId,
            It.Is<TransactionalBatchItemRequestOptions>(options =>
                options.IfMatchEtag == "rider-release-etag")), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<RiderActiveBookingReservationAuditEvent>(item =>
                item.EventType == RiderActiveBookingReservationAuditEventTypes.Released &&
                item.RiderId == "rider-1" && item.RideId == "ride-1"),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static RideOfferState CreateRideOfferState()
    {
        var offeredAt = DateTimeOffset.UtcNow;
        var expiresAt = offeredAt.AddMinutes(10);
        return new RideOfferState(
            "ride-cosmos-offer",
            "zone-1",
            "Cab",
            "offer-cosmos-offer",
            "driver-1",
            offeredAt,
            expiresAt,
            RideOfferStatuses.Pending,
            [new RideOfferAttempt("offer-cosmos-offer", "driver-1", offeredAt, expiresAt, RideOfferStatuses.Pending)]);
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
