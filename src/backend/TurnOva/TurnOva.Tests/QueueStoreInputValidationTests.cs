using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class QueueStoreInputValidationTests
{
    [Theory]
    [InlineData("", "Cab")]
    [InlineData(" ", "Cab")]
    [InlineData("zone-a", "")]
    [InlineData("zone-a", " ")]
    public async Task CosmosQueueReadsRejectBlankScopeBeforeCallingCosmos(string zoneId, string serviceClass)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var store = new CosmosQueueStore(container.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetEligibleAsync(zoneId, serviceClass, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetAuditEventsAsync(zoneId, serviceClass, CancellationToken.None));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosQueueMutationsRejectBlankDriverOrScopeBeforeCallingCosmos()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var store = new CosmosQueueStore(container.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RemoveAsync(" ", "zone-a", "Cab", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.MoveToBackAsync(" ", "zone-a", "Cab", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.MoveToBackOnceAsync(" ", "zone-a", "Cab", "offer-1", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RemoveAsync("driver-1", " ", "Cab", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.MoveToBackAsync("driver-1", "zone-a", " ", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.MoveToBackOnceAsync("driver-1", "zone-a", " ", "offer-1", CancellationToken.None));

        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "Cab")]
    [InlineData(" ", "Cab")]
    [InlineData("zone-a", "")]
    [InlineData("zone-a", " ")]
    public async Task InMemoryQueueReadsRejectBlankScope(string zoneId, string serviceClass)
    {
        var store = new InMemoryQueueStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetEligibleAsync(zoneId, serviceClass, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetAuditEventsAsync(zoneId, serviceClass, CancellationToken.None));
    }

    [Fact]
    public async Task InMemoryQueueMutationsRejectBlankDriverOrScopeBeforeStateMutation()
    {
        var store = new InMemoryQueueStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RemoveAsync(" ", "zone-a", "Cab", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.MoveToBackAsync(" ", "zone-a", "Cab", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.MoveToBackOnceAsync(" ", "zone-a", "Cab", "offer-1", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RemoveAsync("driver-1", " ", "Cab", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.MoveToBackAsync("driver-1", "zone-a", " ", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.MoveToBackOnceAsync("driver-1", "zone-a", " ", "offer-1", CancellationToken.None));

        Assert.Empty(await store.GetEligibleAsync("zone-a", "Cab", CancellationToken.None));
        Assert.Empty(await store.GetAuditEventsAsync("zone-a", "Cab", CancellationToken.None));
    }

    [Fact]
    public async Task CosmosQueueInputsHonorPreCancelledTokenBeforeValidation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new CosmosQueueStore(container.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetEligibleAsync(" ", " ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetAuditEventsAsync(" ", " ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.RemoveAsync(" ", " ", " ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.MoveToBackAsync(" ", " ", " ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.MoveToBackOnceAsync(" ", " ", " ", " ", cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InMemoryQueueInputsHonorPreCancelledTokenBeforeValidation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new InMemoryQueueStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetEligibleAsync(" ", " ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetAuditEventsAsync(" ", " ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.RemoveAsync(" ", " ", " ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.MoveToBackAsync(" ", " ", " ", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.MoveToBackOnceAsync(" ", " ", " ", " ", cancellation.Token));

        Assert.Empty(await store.GetEligibleAsync("zone-a", "Cab", CancellationToken.None));
        Assert.Empty(await store.GetAuditEventsAsync("zone-a", "Cab", CancellationToken.None));
    }
}
