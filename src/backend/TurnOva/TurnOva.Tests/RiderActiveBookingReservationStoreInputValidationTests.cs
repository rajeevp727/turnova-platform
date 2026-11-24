using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class RiderActiveBookingReservationStoreInputValidationTests
{
    [Fact]
    public async Task CosmosAcquireHonorsPreCancelledTokenBeforeNullReservationValidation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object)
                .TryAcquireAsync(null!, cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InMemoryAcquireHonorsPreCancelledTokenBeforeNullReservationValidation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new InMemoryRiderActiveBookingReservationStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryAcquireAsync(null!, cancellation.Token));

        Assert.Null(await store.GetActiveAsync("rider-1"));
        Assert.Empty(store.GetAuditEvents("rider-1"));
    }

    [Fact]
    public async Task CosmosAcquireRejectsNullReservationBeforeCallingCosmos()
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryAcquireAsync(null!));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InMemoryAcquireRejectsNullReservationBeforeStoringReservation()
    {
        var store = new InMemoryRiderActiveBookingReservationStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.TryAcquireAsync(null!));

        Assert.Null(await store.GetActiveAsync("rider-1"));
        Assert.Empty(store.GetAuditEvents("rider-1"));
    }

    [Fact]
    public async Task CosmosAcquireHonorsPreCancelledTokenBeforeBlankRequestHashValidation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", " ", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object)
                .TryAcquireAsync(reservation, cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InMemoryAcquireHonorsPreCancelledTokenBeforeBlankRequestHashValidation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new InMemoryRiderActiveBookingReservationStore();
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", " ", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryAcquireAsync(reservation, cancellation.Token));

        Assert.Null(await store.GetActiveAsync("rider-1"));
        Assert.Empty(store.GetAuditEvents("rider-1"));
    }

    [Fact]
    public async Task CosmosAcquireRejectsBlankRequestHashBeforeCallingCosmos()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", " ", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryAcquireAsync(reservation));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InMemoryAcquireRejectsBlankRequestHashBeforeStoringReservation()
    {
        var store = new InMemoryRiderActiveBookingReservationStore();
        var reservation = new RiderActiveBookingReservation(
            "rider-1", "ride-1", " ", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

        await Assert.ThrowsAsync<ArgumentException>(() => store.TryAcquireAsync(reservation));

        Assert.Null(await store.GetActiveAsync("rider-1"));
        Assert.Empty(store.GetAuditEvents("rider-1"));
    }
    [Theory]
    [InlineData("", "ride-1")]
    [InlineData(" ", "ride-1")]
    [InlineData("rider-1", "")]
    [InlineData("rider-1", " ")]
    public async Task CosmosAcquireRejectsBlankRiderOrRideIdBeforeCallingCosmos(string riderId, string rideId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var reservation = new RiderActiveBookingReservation(
            riderId, rideId, "request-hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryAcquireAsync(reservation));

        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "ride-1")]
    [InlineData(" ", "ride-1")]
    [InlineData("rider-1", "")]
    [InlineData("rider-1", " ")]
    public async Task InMemoryAcquireRejectsBlankRiderOrRideIdBeforeStoringReservation(string riderId, string rideId)
    {
        var store = new InMemoryRiderActiveBookingReservationStore();
        var reservation = new RiderActiveBookingReservation(
            riderId, rideId, "request-hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

        await Assert.ThrowsAsync<ArgumentException>(() => store.TryAcquireAsync(reservation));

        Assert.Null(await store.GetActiveAsync("rider-1"));
        Assert.Empty(store.GetAuditEvents("rider-1"));
    }



    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ActiveReadRejectsBlankRiderIdBeforeCallingCosmos(string riderId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).GetActiveAsync(riderId));

        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "ride-1")]
    [InlineData(" ", "ride-1")]
    [InlineData("rider-1", "")]
    [InlineData("rider-1", " ")]
    public async Task PromoteRejectsBlankRiderOrRideIdBeforeCallingCosmos(string riderId, string rideId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryMakePermanentAsync(riderId, rideId));

        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "ride-1")]
    [InlineData(" ", "ride-1")]
    [InlineData("rider-1", "")]
    [InlineData("rider-1", " ")]
    public async Task ReleaseRejectsBlankRiderOrRideIdBeforeCallingCosmos(string riderId, string rideId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosRiderActiveBookingReservationStore(container.Object).TryReleaseAsync(riderId, rideId));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NonAcquireOperationsHonorPreCancelledTokenBeforeInputValidation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var container = new Mock<Container>(MockBehavior.Strict);
        var store = new CosmosRiderActiveBookingReservationStore(container.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetActiveAsync(" ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryMakePermanentAsync(" ", "ride-1", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryReleaseAsync("rider-1", " ", cancellation.Token));

        container.VerifyNoOtherCalls();
    }
}
