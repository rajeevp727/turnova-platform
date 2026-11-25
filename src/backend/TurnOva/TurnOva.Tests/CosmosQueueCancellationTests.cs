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
    public async Task AddRejectsNullEntryBeforeCreatingTransactionalBatch()
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new CosmosQueueStore(container.Object).AddAsync(null!, CancellationToken.None));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddCancellationTakesPrecedenceOverNullEntry()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).AddAsync(null!, cancellation.Token));

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
    public async Task QueueJoinProviderFailureTakesPrecedenceOverCancellation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var batch = new Mock<TransactionalBatch>(MockBehavior.Strict);
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
                return Task.FromException<TransactionalBatchResponse>(
                    new InvalidOperationException("queue join batch failed"));
            });
        container.Setup(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Returns(batch.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).AddAsync(entry, cancellation.Token));

        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(cancellation.Token), Times.Once);
        container.VerifyNoOtherCalls();
        batch.VerifyNoOtherCalls();
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
                item.DriverId == "driver-1" &&
                item.ZoneServiceKey == "zone-a|cab" &&
                item.JoinedAt == entry.JoinedAt &&
                item.Sequence == entry.JoinedAt.UtcTicks),
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

    private static Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>> CreateCancellingQueueResponse(
        CancellationTokenSource cancellation)
    {
        var response = new Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(() =>
        {
            cancellation.Cancel();
            return new CosmosQueueStore.QueueEntryDocument
            {
                Id = "driver-1",
                DriverId = "driver-1",
                ZoneId = "zone-a",
                ServiceClass = "Cab",
                ZoneServiceKey = "zone-a|cab",
                JoinedAt = DateTimeOffset.Parse("2026-11-23T00:00:00Z"),
                Sequence = 1,
                IsEligible = true
            };
        });
        return response;
    }

    [Fact]
    public async Task RemoveHonorsCancellationAfterSuccessfulPointReadBeforeBatch()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        var response = CreateCancellingQueueResponse(cancellation);
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).RemoveAsync("driver-1", "zone-a", "Cab", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
        response.VerifyGet(value => value.Resource, Times.Once);
        response.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MoveToBackHonorsCancellationAfterSuccessfulPointReadBeforeBatch()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        var response = CreateCancellingQueueResponse(cancellation);
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackAsync("driver-1", "zone-a", "Cab", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
        response.VerifyGet(value => value.Resource, Times.Once);
        response.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MoveToBackOnceHonorsCancellationAfterSuccessfulPointReadBeforeBatch()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        var response = CreateCancellingQueueResponse(cancellation);
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackOnceAsync(
                "driver-1", "zone-a", "Cab", "offer-1", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        container.VerifyNoOtherCalls();
        response.VerifyGet(value => value.Resource, Times.Once);
        response.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("move-to-back")]
    [InlineData("move-to-back-once")]
    public async Task QueueMutationCancellationTakesPrecedenceDuringEtagHydration(string operation)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        var response = new Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "zone-a",
            ServiceClass = "Cab",
            ZoneServiceKey = "zone-a|cab",
            JoinedAt = DateTimeOffset.Parse("2026-11-24T00:00:00Z"),
            Sequence = 1,
            IsEligible = true
        });
        response.SetupGet(value => value.ETag).Returns(() =>
        {
            cancellation.Cancel();
            return "observed-etag";
        });
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        var store = new CosmosQueueStore(container.Object);

        Task<bool> operationTask = operation switch
        {
            "remove" => store.RemoveAsync("driver-1", "zone-a", "Cab", cancellation.Token),
            "move-to-back" => store.MoveToBackAsync("driver-1", "zone-a", "Cab", cancellation.Token),
            "move-to-back-once" => store.MoveToBackOnceAsync(
                "driver-1", "zone-a", "Cab", "offer-1", cancellation.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operationTask);

        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        response.VerifyGet(value => value.Resource, Times.Once);
        response.VerifyGet(value => value.ETag, Times.Once);
        response.VerifyNoOtherCalls();
        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("move-to-back")]
    [InlineData("move-to-back-once")]
    public async Task QueueMutationsRemainSuccessfulWhenCancellationArrivesAfterAtomicCommit(string operation)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var batch = new Mock<TransactionalBatch>(MockBehavior.Strict);
        var batchResponse = new Mock<TransactionalBatchResponse>();
        batchResponse.SetupGet(value => value.IsSuccessStatusCode).Returns(true);
        var readResponse = new Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>>(MockBehavior.Strict);
        readResponse.SetupGet(value => value.Resource).Returns(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "zone-a",
            ServiceClass = "Cab",
            ZoneServiceKey = "zone-a|cab",
            JoinedAt = DateTimeOffset.Parse("2026-11-23T00:00:00Z"),
            Sequence = 1,
            IsEligible = true
        });
        readResponse.SetupGet(value => value.ETag).Returns("observed-etag");
        using var cancellation = new CancellationTokenSource();

        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                cancellation.Token))
            .ReturnsAsync(readResponse.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<QueueAuditEvent>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(batch.Object);

        if (operation == "remove")
        {
            batch.Setup(value => value.DeleteItem(
                    "driver-1",
                    It.Is<TransactionalBatchItemRequestOptions>(options => options.IfMatchEtag == "observed-etag")))
                .Returns(batch.Object);
        }
        else
        {
            batch.Setup(value => value.ReplaceItem(
                    "driver-1",
                    It.Is<CosmosQueueStore.QueueEntryDocument>(item => item.DriverId == "driver-1"),
                    It.Is<TransactionalBatchItemRequestOptions>(options => options.IfMatchEtag == "observed-etag")))
                .Returns(batch.Object);
        }

        batch.Setup(value => value.ExecuteAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(batchResponse.Object);
            });
        container.Setup(value => value.CreateTransactionalBatch(
                It.Is<PartitionKey>(key => key.Equals(new PartitionKey("zone-a|cab")))))
            .Returns(batch.Object);

        var store = new CosmosQueueStore(container.Object);
        var result = operation switch
        {
            "remove" => await store.RemoveAsync("driver-1", "zone-a", "Cab", cancellation.Token),
            "move-to-back" => await store.MoveToBackAsync("driver-1", "zone-a", "Cab", cancellation.Token),
            "move-to-back-once" => await store.MoveToBackOnceAsync(
                "driver-1", "zone-a", "Cab", "offer-1", cancellation.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.True(result);
        readResponse.VerifyGet(value => value.Resource, Times.Once);
        readResponse.VerifyGet(value => value.ETag, Times.Once);
        readResponse.VerifyNoOtherCalls();
        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("zone-a|cab")))), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<QueueAuditEvent>(audit => audit.ZoneServiceKey == "zone-a|cab"),
            It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Once);
        if (operation == "remove")
        {
            batch.Verify(value => value.DeleteItem(
                "driver-1",
                It.Is<TransactionalBatchItemRequestOptions>(options => options.IfMatchEtag == "observed-etag")), Times.Once);
            batch.Verify(value => value.ReplaceItem(
                It.IsAny<string>(),
                It.IsAny<CosmosQueueStore.QueueEntryDocument>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Never);
        }
        else
        {
            batch.Verify(value => value.ReplaceItem(
                "driver-1",
                It.Is<CosmosQueueStore.QueueEntryDocument>(item => item.DriverId == "driver-1"),
                It.Is<TransactionalBatchItemRequestOptions>(options => options.IfMatchEtag == "observed-etag")), Times.Once);
            batch.Verify(value => value.DeleteItem(
                It.IsAny<string>(),
                It.IsAny<TransactionalBatchItemRequestOptions>()), Times.Never);
        }
        batch.Verify(value => value.ExecuteAsync(cancellation.Token), Times.Once);
        batchResponse.VerifyGet(value => value.IsSuccessStatusCode, Times.Once);
        batch.VerifyNoOtherCalls();
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
