using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class RiderActiveBookingReservationStoreInputValidationTests
{
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

        Assert.Throws<ArgumentException>(() => store.TryAcquireAsync(reservation));

        Assert.Null(await store.GetActiveAsync("rider-1"));
        Assert.Empty(store.GetAuditEvents("rider-1"));
    }
}
