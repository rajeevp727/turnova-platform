using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosQueueCancellationTests
{
    [Fact]
    public async Task GetEligibleHonorsPreCancelledTokenBeforeCreatingQuery()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync("zone-a", "Cab", cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddHonorsPreCancelledTokenBeforeCreatingTransactionalBatch()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var entry = new DriverQueueEntry("driver-1", "zone-a", "Cab", DateTimeOffset.UtcNow, 1, true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).AddAsync(entry, cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveHonorsPreCancelledTokenBeforeReadingQueueEntry()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).RemoveAsync(
                "driver-1", "zone-a", "Cab", cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MoveToBackHonorsPreCancelledTokenBeforeReadingQueueEntry()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackAsync(
                "driver-1", "zone-a", "Cab", cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAuditEventsHonorsPreCancelledTokenBeforeCreatingQuery()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync(
                "zone-a", "Cab", cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task QueueJoinRemainsSuccessfulWhenCancellationArrivesAfterAtomicCommit()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var batch = new Mock<TransactionalBatch>(MockBehavior.Strict);
        var response = new Mock<TransactionalBatchResponse>();
        response.SetupGet(value => value.IsSuccessStatusCode).Returns(true);
        using var cancellation = new CancellationTokenSource();
        var entry = new DriverQueueEntry("driver-1", "zone-a", "Cab", DateTimeOffset.UtcNow, 1, true);
        batch.Setup(value => value.CreateItem(
                It.IsAny<CosmosQueueStore.QueueEntryDocument>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<QueueAuditEvent>(),
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

        await new CosmosQueueStore(container.Object).AddAsync(entry, cancellation.Token);

        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("zone-a|cab")))), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<CosmosQueueStore.QueueEntryDocument>(item =>
                item.DriverId == "driver-1" && item.ZoneServiceKey == "zone-a|cab"),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<QueueAuditEvent>(item =>
                item.DriverId == "driver-1" &&
                item.ZoneServiceKey == "zone-a|cab" &&
                item.EventType == QueueAuditEventTypes.Joined),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(cancellation.Token), Times.Once);
        container.VerifyNoOtherCalls();
        batch.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DispatchExceptionAuditHonorsPreCancelledTokenBeforeStoreCall()
    {
        var store = new Mock<IQueueAuditEventStore>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DispatchExceptionAuditWriter(store.Object).RecordAsync(
                "driver-actor-1", "zone-a", "Cab", "trace-1",
                QueueAuditReasonCategories.QueueReadFailed, cancellation.Token));

        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DispatchExceptionAuditHonorsCancellationAfterCreateConflictBeforeReadBack()
    {
        var store = new Mock<IQueueAuditEventStore>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        store.Setup(value => value.TryCreateAsync(
                It.IsAny<QueueAuditEvent>(), "zone-a|cab", cancellation.Token))
            .Callback<QueueAuditEvent, string, CancellationToken>((_, _, _) => cancellation.Cancel())
            .ReturnsAsync(false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DispatchExceptionAuditWriter(store.Object).RecordAsync(
                "driver-actor-1", "zone-a", "Cab", "trace-2",
                QueueAuditReasonCategories.QueueReadFailed, cancellation.Token));

        store.Verify(value => value.TryCreateAsync(
            It.IsAny<QueueAuditEvent>(), "zone-a|cab", cancellation.Token), Times.Once);
        store.Verify(value => value.ReadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DispatchExceptionAuditHonorsCancellationAfterConflictReadBeforeIdempotentReturn()
    {
        var store = new Mock<IQueueAuditEventStore>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        store.Setup(value => value.TryCreateAsync(
                It.IsAny<QueueAuditEvent>(), "zone-a|cab", cancellation.Token))
            .ReturnsAsync(false);
        store.Setup(value => value.ReadAsync(
                "dispatch-exception-trace-3", "zone-a|cab", cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult<QueueAuditEvent?>(null);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DispatchExceptionAuditWriter(store.Object).RecordAsync(
                "driver-actor-1", "zone-a", "Cab", "trace-3",
                QueueAuditReasonCategories.QueueReadFailed, cancellation.Token));

        store.Verify(value => value.TryCreateAsync(
            It.IsAny<QueueAuditEvent>(), "zone-a|cab", cancellation.Token), Times.Once);
        store.Verify(value => value.ReadAsync(
            "dispatch-exception-trace-3", "zone-a|cab", cancellation.Token), Times.Once);
        store.VerifyNoOtherCalls();
    }
}
