using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.API;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class UnionStatusUpdaterTests
{
    private const string UnionId = "union-status-test";

    [Fact]
    public async Task InMemoryStatusUpdatePreservesUnionMetadata()
    {
        var unions = new ConcurrentDictionary<string, DriverUnion>();
        var original = CreateUnion();
        Assert.True(unions.TryAdd(UnionId, original));
        var updatedAt = DateTimeOffset.Parse("2026-11-22T18:00:00Z");

        var result = await UnionStatusUpdater.TryUpdateStatusAsync(
            null, unions, UnionId, "Suspended", updatedAt, CancellationToken.None);

        Assert.Equal(UnionStatusUpdateOutcome.Updated, result.Outcome);
        var expected = original with { Status = "Suspended", UpdatedAtUtc = updatedAt };
        Assert.Equal(expected, result.Union);
        Assert.Equal(expected, unions[UnionId]);
    }

    [Fact]
    public async Task InMemoryStatusUpdateReturnsNotFoundForMissingUnion()
    {
        var result = await UnionStatusUpdater.TryUpdateStatusAsync(
            null,
            new ConcurrentDictionary<string, DriverUnion>(),
            UnionId,
            "Inactive",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(UnionStatusUpdateOutcome.NotFound, result.Outcome);
        Assert.Null(result.Union);
    }

    [Fact]
    public async Task CosmosStatusUpdateUsesObservedEtagAndPreservesUnionMetadata()
    {
        var original = CreateUnion();
        var read = CreateReadResponse(original, "union-status-etag");
        var replacement = new Mock<ItemResponse<DriverUnion>>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, read.Object);

        DriverUnion? written = null;
        PartitionKey? writtenPartition = null;
        ItemRequestOptions? writtenOptions = null;
        container.Setup(value => value.ReplaceItemAsync<DriverUnion>(
                It.IsAny<DriverUnion>(),
                UnionId,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<DriverUnion, string, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, partition, options, _) =>
                {
                    written = item;
                    writtenPartition = partition;
                    writtenOptions = options;
                })
            .ReturnsAsync(replacement.Object);

        var updatedAt = DateTimeOffset.Parse("2026-11-22T18:05:00Z");
        var result = await UnionStatusUpdater.TryUpdateStatusAsync(
            container.Object,
            new ConcurrentDictionary<string, DriverUnion>(),
            UnionId,
            "Inactive",
            updatedAt,
            CancellationToken.None);

        var expected = original with { Status = "Inactive", UpdatedAtUtc = updatedAt };
        Assert.Equal(UnionStatusUpdateOutcome.Updated, result.Outcome);
        Assert.Equal(expected, result.Union);
        Assert.Equal(expected, written);
        Assert.True(writtenPartition.HasValue);
        Assert.Equal(new PartitionKey(UnionId), writtenPartition.Value);
        Assert.Equal("union-status-etag", writtenOptions?.IfMatchEtag);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "union-status-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task CosmosStatusUpdateFailsClosedWithoutObservedEtag(string? etag)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(CreateUnion(), etag).Object);

        var result = await UnionStatusUpdater.TryUpdateStatusAsync(
            container.Object,
            new ConcurrentDictionary<string, DriverUnion>(),
            UnionId,
            "Suspended",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(UnionStatusUpdateOutcome.Conflict, result.Outcome);
        Assert.Null(result.Union);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosStatusUpdateReturnsNotFoundWhenReadFindsNoUnion()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<DriverUnion>(
                UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("union not found", HttpStatusCode.NotFound, 0, "union-status-read", 0));

        var result = await UnionStatusUpdater.TryUpdateStatusAsync(
            container.Object,
            new ConcurrentDictionary<string, DriverUnion>(),
            UnionId,
            "Suspended",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(UnionStatusUpdateOutcome.NotFound, result.Outcome);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task CosmosStatusUpdateReturnsConflictWhenConditionalReplaceLosesRace(HttpStatusCode statusCode)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(CreateUnion(), "union-status-etag").Object);
        container.Setup(value => value.ReplaceItemAsync<DriverUnion>(
                It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("conditional update lost", statusCode, 0, "union-status-write", 0));

        var result = await UnionStatusUpdater.TryUpdateStatusAsync(
            container.Object,
            new ConcurrentDictionary<string, DriverUnion>(),
            UnionId,
            "Suspended",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(UnionStatusUpdateOutcome.Conflict, result.Outcome);
        Assert.Null(result.Union);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "union-status-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosStatusUpdateReturnsNotFoundWhenUnionDisappearsBeforeReplace()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(CreateUnion(), "union-status-etag").Object);
        container.Setup(value => value.ReplaceItemAsync<DriverUnion>(
                It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("union deleted", HttpStatusCode.NotFound, 0, "union-status-write", 0));

        var result = await UnionStatusUpdater.TryUpdateStatusAsync(
            container.Object,
            new ConcurrentDictionary<string, DriverUnion>(),
            UnionId,
            "Suspended",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(UnionStatusUpdateOutcome.NotFound, result.Outcome);
        Assert.Null(result.Union);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosStatusUpdateHonorsCancellationBeforeRead()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionStatusUpdater.TryUpdateStatusAsync(
                container.Object,
                new ConcurrentDictionary<string, DriverUnion>(),
                UnionId,
                "Suspended",
                DateTimeOffset.UtcNow,
                cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosStatusUpdateHonorsCancellationAfterReadBeforeReplace()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var read = CreateReadResponse(CreateUnion(), "union-status-etag");
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<DriverUnion>(
                UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionStatusUpdater.TryUpdateStatusAsync(
                container.Object,
                new ConcurrentDictionary<string, DriverUnion>(),
                UnionId,
                "Suspended",
                DateTimeOffset.UtcNow,
                cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }


    [Fact]
    public async Task CosmosStatusUpdateStopsBeforeReplaceWhenCancellationArrivesDuringResourceHydration()
    {
        using var cancellation = new CancellationTokenSource();
        var read = CreateReadResponse(CreateUnion(), "union-status-etag", onResourceRead: cancellation.Cancel);
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, read.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionStatusUpdater.TryUpdateStatusAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnion>(),
                UnionId, "Suspended", DateTimeOffset.UtcNow, cancellation.Token));

        read.VerifyGet(value => value.Resource, Times.Once);
        read.VerifyGet(value => value.ETag, Times.Never);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
        read.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosStatusUpdateStopsBeforeReplaceWhenCancellationArrivesDuringEtagHydration()
    {
        using var cancellation = new CancellationTokenSource();
        var read = CreateReadResponse(CreateUnion(), "union-status-etag", onEtagRead: cancellation.Cancel);
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, read.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionStatusUpdater.TryUpdateStatusAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnion>(),
                UnionId, "Suspended", DateTimeOffset.UtcNow, cancellation.Token));

        read.VerifyGet(value => value.Resource, Times.Once);
        read.VerifyGet(value => value.ETag, Times.Once);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
        read.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosStatusUpdateReadNotFoundPreservesCancellation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<DriverUnion>(
                UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnion>>(
                    new CosmosException("union not found", HttpStatusCode.NotFound, 0, "union-status-read", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionStatusUpdater.TryUpdateStatusAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnion>(),
                UnionId, "Suspended", DateTimeOffset.UtcNow, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    public async Task CosmosStatusUpdateConditionalFailurePreservesCancellation(HttpStatusCode statusCode)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(CreateUnion(), "union-status-etag").Object);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReplaceItemAsync<DriverUnion>(
                It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnion>>(
                    new CosmosException("conditional Union update failed", statusCode, 0, "union-status-write", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionStatusUpdater.TryUpdateStatusAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnion>(),
                UnionId, "Suspended", DateTimeOffset.UtcNow, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }


    [Fact]
    public async Task CosmosStatusUpdateProviderFailureTakesPrecedenceOverCancellation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(CreateUnion(), "union-status-etag").Object);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReplaceItemAsync<DriverUnion>(
                It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnion>>(
                    new CosmosException("union update unavailable", HttpStatusCode.ServiceUnavailable, 0, "union-status-write", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionStatusUpdater.TryUpdateStatusAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnion>(),
                UnionId, "Suspended", DateTimeOffset.UtcNow, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "union-status-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosStatusUpdateProviderFailurePropagatesUnchangedWithoutCancellation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(CreateUnion(), "union-status-etag").Object);
        var unavailable = new CosmosException(
            "union update unavailable", HttpStatusCode.ServiceUnavailable, 0, "union-status-write", 0);
        container.Setup(value => value.ReplaceItemAsync<DriverUnion>(
                It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(unavailable);

        var exception = await Assert.ThrowsAsync<CosmosException>(() =>
            UnionStatusUpdater.TryUpdateStatusAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnion>(),
                UnionId, "Suspended", DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Same(unavailable, exception);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(), UnionId, It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "union-status-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    private static Mock<ItemResponse<DriverUnion>> CreateReadResponse(
        DriverUnion union,
        string? etag,
        Action? onResourceRead = null,
        Action? onEtagRead = null)
    {
        var response = new Mock<ItemResponse<DriverUnion>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(() =>
        {
            onResourceRead?.Invoke();
            return union;
        });
        response.SetupGet(value => value.ETag).Returns(() =>
        {
            onEtagRead?.Invoke();
            return etag!;
        });
        return response;
    }

    private static void SetupRead(Mock<Container> container, ItemResponse<DriverUnion> response)
    {
        container.Setup(value => value.ReadItemAsync<DriverUnion>(
                UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    [Fact]
    public async Task CosmosStatusUpdateReturnsCommittedUnionWhenCancellationArrivesAfterReplace()
    {
        using var cancellation = new CancellationTokenSource();
        var original = CreateUnion();
        var read = CreateReadResponse(original, "union-status-committed-etag");
        var replacement = new Mock<ItemResponse<DriverUnion>>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, read.Object);
        container.Setup(value => value.ReplaceItemAsync<DriverUnion>(
                It.IsAny<DriverUnion>(),
                UnionId,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(replacement.Object);
            });

        var updatedAt = DateTimeOffset.Parse("2026-11-25T03:30:00Z");
        var result = await UnionStatusUpdater.TryUpdateStatusAsync(
            container.Object,
            new ConcurrentDictionary<string, DriverUnion>(),
            UnionId,
            "Suspended",
            updatedAt,
            cancellation.Token);

        Assert.Equal(UnionStatusUpdateOutcome.Updated, result.Outcome);
        Assert.Equal(original with { Status = "Suspended", UpdatedAtUtc = updatedAt }, result.Union);
        container.Verify(value => value.ReplaceItemAsync<DriverUnion>(
            It.IsAny<DriverUnion>(),
            UnionId,
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    private static DriverUnion CreateUnion()
    {
        var createdAt = DateTimeOffset.Parse("2026-11-20T12:00:00Z");
        return new DriverUnion(
            UnionId,
            "Union Status Test",
            "UNION-STATUS",
            "Hyderabad",
            "Manager",
            null,
            ["hyderabad-central", "hyderabad-north"],
            ["cab"],
            "Active",
            createdAt,
            createdAt);
    }
}
