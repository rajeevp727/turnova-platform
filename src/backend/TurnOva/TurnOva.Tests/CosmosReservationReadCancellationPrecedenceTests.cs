using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosReservationReadCancellationPrecedenceTests
{
    [Fact]
    public async Task DriverActiveReadCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var container = CreateFailingReadContainer<DriverRideReservation>("driver-1", cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object)
                .GetActiveAsync("driver-1", cancellation.Token));

        VerifySingleRead<DriverRideReservation>(container, "driver-1");
    }

    [Fact]
    public async Task DriverPromotionReadCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var container = CreateFailingReadContainer<DriverRideReservation>("driver-1", cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object)
                .TryMakePermanentAsync("driver-1", "ride-1", cancellation.Token));

        VerifySingleRead<DriverRideReservation>(container, "driver-1");
    }

    [Fact]
    public async Task DriverExpiryReadCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var container = CreateFailingReadContainer<DriverRideReservation>("driver-1", cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object)
                .TrySetExpiryAsync("driver-1", "ride-1", DateTimeOffset.UtcNow.AddMinutes(5),
                    cancellation.Token));

        VerifySingleRead<DriverRideReservation>(container, "driver-1");
    }

    [Fact]
    public async Task DriverReleaseReadCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var container = CreateFailingReadContainer<DriverRideReservation>("driver-1", cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object)
                .TryReleaseAsync("driver-1", "ride-1", cancellation.Token));

        VerifySingleRead<DriverRideReservation>(container, "driver-1");
    }

    [Fact]
    public async Task DriverQueryPageCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var iterator = new Mock<FeedIterator<DriverRideReservation>>();
        iterator.Setup(value => value.HasMoreResults).Returns(true);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<FeedResponse<DriverRideReservation>>(Unavailable());
            });
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<DriverRideReservation>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object)
                .GetActiveForDriversAsync(["driver-1"], cancellation.Token));

        container.Verify(value => value.GetItemQueryIterator<DriverRideReservation>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task RiderActiveReadCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var container = CreateFailingReadContainer<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object)
                .GetActiveAsync("rider-1", cancellation.Token));

        VerifySingleRead<RiderActiveBookingReservation>(container, RiderActiveBookingReservation.DocumentId);
    }

    [Fact]
    public async Task RiderPromotionReadCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var container = CreateFailingReadContainer<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object)
                .TryMakePermanentAsync("rider-1", "ride-1", cancellation.Token));

        VerifySingleRead<RiderActiveBookingReservation>(container, RiderActiveBookingReservation.DocumentId);
    }

    [Fact]
    public async Task RiderReleaseReadCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var container = CreateFailingReadContainer<RiderActiveBookingReservation>(
            RiderActiveBookingReservation.DocumentId, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object)
                .TryReleaseAsync("rider-1", "ride-1", cancellation.Token));

        VerifySingleRead<RiderActiveBookingReservation>(container, RiderActiveBookingReservation.DocumentId);
    }

    private static Mock<Container> CreateFailingReadContainer<T>(
        string id,
        CancellationTokenSource cancellation)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<T>(
                id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<T>>(Unavailable());
            });
        return container;
    }

    private static void VerifySingleRead<T>(Mock<Container> container, string id)
    {
        container.Verify(value => value.ReadItemAsync<T>(
            id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    private static CosmosException Unavailable() =>
        new("Cosmos is unavailable.", HttpStatusCode.ServiceUnavailable, 0,
            "reservation-read-cancellation-test", 0);
}
