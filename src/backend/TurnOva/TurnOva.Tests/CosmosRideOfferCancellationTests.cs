using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosRideOfferCancellationTests
{
    [Fact]
    public async Task EveryRideOfferStoreOperationObservesPreCancelledTokenBeforeCosmosWork()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var store = new CosmosRideOfferStore(container.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetByRideAsync("ride-1", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetPendingForDriverAsync("driver-1", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetExpiredAsync(DateTimeOffset.UtcNow, 1, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetPendingNotificationsAsync(1, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.TryCreateAsync(CreateState(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.TryReplaceAsync(CreateState(), "observed-etag", cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByRideHonorsCancellationAfterSuccessfulReadBeforeReturningSnapshot()
    {
        var state = CreateState();
        var read = CreateReadResponse(state);
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<RideOfferState>(
                state.RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRideOfferStore(container.Object).GetByRideAsync(state.RideId, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            state.RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByRideDoesNotTranslateNotFoundToNullAfterCancellation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<RideOfferState>(
                "ride-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<RideOfferState>>(
                    new CosmosException("not found", HttpStatusCode.NotFound, 0, "ride-offer-read-test", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRideOfferStore(container.Object).GetByRideAsync("ride-1", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            "ride-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPendingForDriverHonorsCancellationAfterPageReadBeforeReturningOffer()
    {
        using var cancellation = new CancellationTokenSource();
        var page = CreatePage([CreateState()]);
        var iterator = CreateIterator(page, () =>
        {
            cancellation.Cancel();
            return Task.FromResult(page.Object);
        });
        var container = CreateQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRideOfferStore(container.Object).GetPendingForDriverAsync("driver-1", cancellation.Token));

        container.Verify(value => value.GetItemQueryIterator<RideOfferState>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task GetExpiredHonorsCancellationAfterPageReadBeforePointRead()
    {
        using var cancellation = new CancellationTokenSource();
        var page = CreatePage([CreateState()]);
        var iterator = CreateIterator(page, () =>
        {
            cancellation.Cancel();
            return Task.FromResult(page.Object);
        });
        var container = CreateQueryContainer(iterator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRideOfferStore(container.Object).GetExpiredAsync(DateTimeOffset.UtcNow, 1, cancellation.Token));

        container.Verify(value => value.GetItemQueryIterator<RideOfferState>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetExpiredHonorsCancellationAfterPointReadBeforeReturningSnapshot()
    {
        var state = CreateState();
        var page = CreatePage([state]);
        var iterator = CreateIterator(page);
        var read = CreateReadResponse(state);
        var container = CreateQueryContainer(iterator);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<RideOfferState>(
                state.RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRideOfferStore(container.Object).GetExpiredAsync(DateTimeOffset.UtcNow, 1, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            state.RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingNotificationsHonorsCancellationAfterPointReadBeforeReturningSnapshot()
    {
        var state = CreateState();
        var page = CreatePage([state]);
        var iterator = CreateIterator(page);
        var read = CreateReadResponse(state);
        var container = CreateQueryContainer(iterator);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<RideOfferState>(
                state.RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRideOfferStore(container.Object).GetPendingNotificationsAsync(1, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<RideOfferState>(
            state.RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryCreateDoesNotTranslateConflictToFalseAfterCancellation()
    {
        var state = CreateState();
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<RideOfferState>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<RideOfferState>>(
                    new CosmosException("already exists", HttpStatusCode.Conflict, 0, "ride-offer-create-test", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRideOfferStore(container.Object).TryCreateAsync(state, cancellation.Token));

        container.Verify(value => value.CreateItemAsync(
            It.IsAny<RideOfferState>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryReplaceDoesNotTranslateStaleWriteToFalseAfterCancellation()
    {
        var state = CreateState();
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<RideOfferState>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<RideOfferState>>(
                    new CosmosException("etag changed", HttpStatusCode.PreconditionFailed, 0, "ride-offer-replace-test", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosRideOfferStore(container.Object).TryReplaceAsync(state, "observed-etag", cancellation.Token));

        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<RideOfferState>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SuccessfulDurableOfferWritesRemainAuthoritativeWhenCancellationArrivesAfterCommit()
    {
        var state = CreateState();
        var createResponse = new Mock<ItemResponse<RideOfferState>>(MockBehavior.Strict);
        createResponse.SetupGet(value => value.StatusCode).Returns(HttpStatusCode.Created);
        var createContainer = new Mock<Container>(MockBehavior.Strict);
        using var createCancellation = new CancellationTokenSource();
        createContainer.Setup(value => value.CreateItemAsync(
                It.IsAny<RideOfferState>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                createCancellation.Cancel();
                return Task.FromResult(createResponse.Object);
            });

        Assert.True(await new CosmosRideOfferStore(createContainer.Object)
            .TryCreateAsync(state, createCancellation.Token));

        var replaceResponse = new Mock<ItemResponse<RideOfferState>>(MockBehavior.Strict);
        var replaceContainer = new Mock<Container>(MockBehavior.Strict);
        using var replaceCancellation = new CancellationTokenSource();
        replaceContainer.Setup(value => value.ReplaceItemAsync(
                It.IsAny<RideOfferState>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                replaceCancellation.Cancel();
                return Task.FromResult(replaceResponse.Object);
            });

        Assert.True(await new CosmosRideOfferStore(replaceContainer.Object)
            .TryReplaceAsync(state, "observed-etag", replaceCancellation.Token));
    }

    private static Mock<Container> CreateQueryContainer(Mock<FeedIterator<RideOfferState>> iterator)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<RideOfferState>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return container;
    }

    private static Mock<FeedIterator<RideOfferState>> CreateIterator(
        Mock<FeedResponse<RideOfferState>> page,
        Func<Task<FeedResponse<RideOfferState>>>? readNext = null)
    {
        var iterator = new Mock<FeedIterator<RideOfferState>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .Returns(readNext ?? (() => Task.FromResult(page.Object)));
        return iterator;
    }

    private static Mock<FeedResponse<T>> CreatePage<T>(IReadOnlyList<T> items)
    {
        var page = new Mock<FeedResponse<T>>(MockBehavior.Strict);
        page.As<IEnumerable<T>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => items.GetEnumerator());
        return page;
    }

    private static Mock<ItemResponse<RideOfferState>> CreateReadResponse(RideOfferState state)
    {
        var response = new Mock<ItemResponse<RideOfferState>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(state);
        response.SetupGet(value => value.ETag).Returns("observed-etag");
        return response;
    }

    private static RideOfferState CreateState()
    {
        var offeredAt = DateTimeOffset.UtcNow;
        var expiresAt = offeredAt.AddMinutes(10);
        return new RideOfferState(
            "ride-cancellation",
            "zone-1",
            "Cab",
            "offer-cancellation",
            "driver-1",
            offeredAt,
            expiresAt,
            RideOfferStatuses.Pending,
            [new RideOfferAttempt("offer-cancellation", "driver-1", offeredAt, expiresAt, RideOfferStatuses.Pending)]);
    }
}
