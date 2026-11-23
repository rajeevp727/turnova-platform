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

    [Fact]
    public async Task GetEligibleStopsBeforeReadingNextPageWhenCancellationArrivesAfterFirstPage()
    {
        using var cancellation = new CancellationTokenSource();
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            DriverId = "driver-1",
            ZoneId = "zone-a",
            ServiceClass = "Cab",
            JoinedAt = DateTimeOffset.Parse("2026-11-23T00:00:00Z"),
            Sequence = 1,
            IsEligible = true
        });
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(true);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(page.Object);
            });
        var container = CreateQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync("zone-a", "Cab", cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(),
            It.IsAny<string?>(),
            It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task GetEligibleHonorsCancellationAfterFinalPageBeforeReturningResults()
    {
        using var cancellation = new CancellationTokenSource();
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            DriverId = "driver-1",
            ZoneId = "zone-a",
            ServiceClass = "Cab",
            JoinedAt = DateTimeOffset.Parse("2026-11-23T00:00:00Z"),
            Sequence = 1,
            IsEligible = true
        });
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(page.Object);
            });
        var container = CreateQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync("zone-a", "Cab", cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(),
            It.IsAny<string?>(),
            It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditEventsStopsBeforeReadingNextPageWhenCancellationArrivesAfterFirstPage()
    {
        using var cancellation = new CancellationTokenSource();
        var page = CreatePage(new QueueAuditEvent(
            "event-1",
            "zone-a|cab",
            "driver-1",
            QueueAuditEventTypes.Joined,
            DateTimeOffset.Parse("2026-11-23T00:00:00Z")));
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(true);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(page.Object);
            });
        var container = CreateQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync("zone-a", "Cab", cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(),
            It.IsAny<string?>(),
            It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditEventsHonorsCancellationAfterFinalPageBeforeReturningResults()
    {
        using var cancellation = new CancellationTokenSource();
        var page = CreatePage(new QueueAuditEvent(
            "event-1",
            "zone-a|cab",
            "driver-1",
            QueueAuditEventTypes.Joined,
            DateTimeOffset.Parse("2026-11-23T00:00:00Z")));
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(page.Object);
            });
        var container = CreateQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync("zone-a", "Cab", cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(),
            It.IsAny<string?>(),
            It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task GetEligibleCancellationTakesPrecedenceOverQueryProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.Setup(value => value.HasMoreResults).Returns(true);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<FeedResponse<CosmosQueueStore.QueueEntryDocument>>(Unavailable());
            });
        var container = CreateQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync("zone-a", "Cab", cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAuditEventsCancellationTakesPrecedenceOverQueryProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.Setup(value => value.HasMoreResults).Returns(true);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<FeedResponse<QueueAuditEvent>>(Unavailable());
            });
        var container = CreateQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync("zone-a", "Cab", cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveCancellationTakesPrecedenceOverPointReadProviderFailure()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<CosmosQueueStore.QueueEntryDocument>>(Unavailable());
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).RemoveAsync("driver-1", "zone-a", "Cab", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MoveToBackCancellationTakesPrecedenceOverPointReadProviderFailure()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<CosmosQueueStore.QueueEntryDocument>>(Unavailable());
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackAsync("driver-1", "zone-a", "Cab", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MoveToBackOnceCancellationTakesPrecedenceOverPointReadProviderFailure()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<CosmosQueueStore.QueueEntryDocument>>(Unavailable());
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackOnceAsync(
                "driver-1", "zone-a", "Cab", "offer-1", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    private static CosmosException Unavailable() =>
        new("Cosmos is unavailable.", System.Net.HttpStatusCode.ServiceUnavailable, 0, "queue-cancellation-test", 0);

    private static Mock<Container> CreateQueryContainer<T>(Mock<FeedIterator<T>> iterator)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<T>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string?>(),
                It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return container;
    }

    private static Mock<FeedResponse<T>> CreatePage<T>(params T[] items)
    {
        var page = new Mock<FeedResponse<T>>(MockBehavior.Strict);
        page.As<IEnumerable<T>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => items.AsEnumerable().GetEnumerator());
        return page;
    }

}
