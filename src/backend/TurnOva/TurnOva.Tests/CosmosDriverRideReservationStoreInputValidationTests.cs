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
}
