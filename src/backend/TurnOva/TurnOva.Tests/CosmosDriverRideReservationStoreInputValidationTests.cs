using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosDriverRideReservationStoreInputValidationTests
{
    [Theory]
    [InlineData("", "ride-1")]
    [InlineData(" ", "ride-1")]
    [InlineData("driver-1", "")]
    [InlineData("driver-1", " ")]
    public async Task AcquireRejectsBlankDriverOrRideIdBeforeCallingCosmos(string driverId, string rideId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryAcquireAsync(driverId, rideId));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AcquireHonorsPreCancelledTokenBeforeBlankDriverOrRideIdValidation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryAcquireAsync(" ", "ride-1", cancellation.Token));

        container.VerifyNoOtherCalls();
    }


    [Theory]
    [InlineData("", "ride-1")]
    [InlineData(" ", "ride-1")]
    [InlineData("driver-1", "")]
    [InlineData("driver-1", " ")]
    public async Task PromoteRejectsBlankDriverOrRideIdBeforeCallingCosmos(string driverId, string rideId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryMakePermanentAsync(driverId, rideId));

        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "ride-1")]
    [InlineData(" ", "ride-1")]
    [InlineData("driver-1", "")]
    [InlineData("driver-1", " ")]
    public async Task SetExpiryRejectsBlankDriverOrRideIdBeforeCallingCosmos(string driverId, string rideId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TrySetExpiryAsync(
                driverId, rideId, DateTimeOffset.UtcNow.AddMinutes(5)));

        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ActiveReadRejectsBlankDriverIdBeforeCallingCosmos(string driverId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosDriverRideReservationStore(container.Object).GetActiveAsync(driverId));

        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "ride-1")]
    [InlineData(" ", "ride-1")]
    [InlineData("driver-1", "")]
    [InlineData("driver-1", " ")]
    public async Task ReleaseRejectsBlankDriverOrRideIdBeforeCallingCosmos(string driverId, string rideId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosDriverRideReservationStore(container.Object).TryReleaseAsync(driverId, rideId));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NonAcquireOperationsHonorPreCancelledTokenBeforeInputValidation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var container = new Mock<Container>(MockBehavior.Strict);
        var store = new CosmosDriverRideReservationStore(container.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryMakePermanentAsync(" ", "ride-1", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TrySetExpiryAsync("driver-1", " ", DateTimeOffset.UtcNow.AddMinutes(5), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetActiveAsync(" ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryReleaseAsync("driver-1", " ", cancellation.Token));

        container.VerifyNoOtherCalls();
    }
}
