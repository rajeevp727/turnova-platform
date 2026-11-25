using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.API;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class UnionAssignmentWriterTests
{
    private const string DriverId = "driver-union-writer-test";

    [Fact]
    public async Task InMemoryCreateUsesTryAdd()
    {
        var assignments = new ConcurrentDictionary<string, DriverUnionAssignment>();
        var assignment = CreateAssignment();

        Assert.True(await UnionAssignmentWriter.TrySaveAsync(
            null, assignments, assignment, expectedCurrent: null, CancellationToken.None));
        Assert.Equal(assignment, assignments[DriverId]);
    }

    [Fact]
    public async Task InMemoryUpdateUsesExactExpectedSnapshot()
    {
        var assignments = new ConcurrentDictionary<string, DriverUnionAssignment>();
        var current = CreateAssignment();
        Assert.True(assignments.TryAdd(DriverId, current));
        var updated = current with { UnionId = "union-next", UnionName = "Next Union" };

        Assert.True(await UnionAssignmentWriter.TrySaveAsync(
            null, assignments, updated, current, CancellationToken.None));
        Assert.Equal(updated, assignments[DriverId]);
    }

    [Fact]
    public async Task InMemoryUpdateDoesNotOverwriteWhenExpectedSnapshotIsStale()
    {
        var assignments = new ConcurrentDictionary<string, DriverUnionAssignment>();
        var current = CreateAssignment();
        Assert.True(assignments.TryAdd(DriverId, current));
        var stale = current with { UpdatedAtUtc = current.UpdatedAtUtc.AddMinutes(-1) };
        var proposed = current with { UnionId = "union-stale-writer" };

        Assert.False(await UnionAssignmentWriter.TrySaveAsync(
            null, assignments, proposed, stale, CancellationToken.None));
        Assert.Equal(current, assignments[DriverId]);
    }

    [Fact]
    public async Task CosmosCreateUsesCreateItemInsteadOfUpsert()
    {
        var assignment = CreateAssignment();
        var created = new Mock<ItemResponse<DriverUnionAssignment>>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.CreateItemAsync<DriverUnionAssignment>(
                assignment,
                It.Is<PartitionKey?>(key => key.HasValue && key.Value.Equals(new PartitionKey(DriverId))),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(created.Object);

        Assert.True(await UnionAssignmentWriter.TrySaveAsync(
            container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
            assignment, expectedCurrent: null, CancellationToken.None));

        container.Verify(value => value.CreateItemAsync<DriverUnionAssignment>(
            assignment, It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.UpsertItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosCreateConflictDoesNotOverwriteExistingAssignment()
    {
        var assignment = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.CreateItemAsync<DriverUnionAssignment>(
                assignment, It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("assignment already exists", HttpStatusCode.Conflict, 0, "union-assignment-create", 0));

        Assert.False(await UnionAssignmentWriter.TrySaveAsync(
            container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
            assignment, expectedCurrent: null, CancellationToken.None));

        container.Verify(value => value.CreateItemAsync<DriverUnionAssignment>(
            assignment, It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.UpsertItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementUsesExpectedSnapshotAndObservedEtag()
    {
        var current = CreateAssignment();
        var updated = current with { UnionId = "union-next", UnionName = "Next Union" };
        var read = CreateReadResponse(current, "union-assignment-etag");
        var replacement = new Mock<ItemResponse<DriverUnionAssignment>>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, read.Object);

        ItemRequestOptions? writtenOptions = null;
        container.Setup(value => value.ReplaceItemAsync<DriverUnionAssignment>(
                updated, DriverId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<DriverUnionAssignment, string, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (_, _, _, options, _) => writtenOptions = options)
            .ReturnsAsync(replacement.Object);

        Assert.True(await UnionAssignmentWriter.TrySaveAsync(
            container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
            updated, current, CancellationToken.None));

        Assert.Equal("union-assignment-etag", writtenOptions?.IfMatchEtag);
        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            updated, DriverId, It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "union-assignment-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementDoesNotWriteWhenCurrentSnapshotDiffersFromExpected()
    {
        var expected = CreateAssignment();
        var persisted = expected with { UpdatedAtUtc = expected.UpdatedAtUtc.AddMinutes(1) };
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(persisted, "union-assignment-etag").Object);

        Assert.False(await UnionAssignmentWriter.TrySaveAsync(
            container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
            expected with { UnionId = "union-next" }, expected, CancellationToken.None));

        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task CosmosReplacementFailsClosedWithoutObservedEtag(string? etag)
    {
        var current = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(current, etag).Object);

        Assert.False(await UnionAssignmentWriter.TrySaveAsync(
            container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
            current with { UnionId = "union-next" }, current, CancellationToken.None));

        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    public async Task CosmosReplacementReturnsFalseWhenConditionalWriteLosesRace(HttpStatusCode statusCode)
    {
        var current = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(current, "union-assignment-etag").Object);
        container.Setup(value => value.ReplaceItemAsync<DriverUnionAssignment>(
                It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("conditional assignment update lost", statusCode, 0, "union-assignment-write", 0));

        Assert.False(await UnionAssignmentWriter.TrySaveAsync(
            container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
            current with { UnionId = "union-next" }, current, CancellationToken.None));

        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "union-assignment-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementReturnsFalseWhenAssignmentIsMissingBeforeRead()
    {
        var current = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<DriverUnionAssignment>(
                DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("assignment not found", HttpStatusCode.NotFound, 0, "union-assignment-read", 0));

        Assert.False(await UnionAssignmentWriter.TrySaveAsync(
            container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
            current with { UnionId = "union-next" }, current, CancellationToken.None));

        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementReturnsFalseWhenAssignmentDisappearsBeforeWrite()
    {
        var current = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(current, "union-assignment-etag").Object);
        container.Setup(value => value.ReplaceItemAsync<DriverUnionAssignment>(
                It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("assignment deleted", HttpStatusCode.NotFound, 0, "union-assignment-write", 0));

        Assert.False(await UnionAssignmentWriter.TrySaveAsync(
            container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
            current with { UnionId = "union-next" }, current, CancellationToken.None));

        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "union-assignment-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosCreateHonorsCancellationBeforeWrite()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                CreateAssignment(), expectedCurrent: null, cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementHonorsCancellationAfterReadBeforeWrite()
    {
        var current = CreateAssignment();
        var read = CreateReadResponse(current, "union-assignment-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<DriverUnionAssignment>(
                DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(read.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                current with { UnionId = "union-next" }, current, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementStopsBeforeWriteWhenCancellationArrivesDuringResourceHydration()
    {
        using var cancellation = new CancellationTokenSource();
        var current = CreateAssignment();
        var read = CreateReadResponse(current, "union-assignment-etag", onResourceRead: cancellation.Cancel);
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, read.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                current with { UnionId = "union-next" }, current, cancellation.Token));

        read.VerifyGet(value => value.Resource, Times.Once);
        read.VerifyGet(value => value.ETag, Times.Never);
        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
        read.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementStopsBeforeWriteWhenCancellationArrivesDuringEtagHydration()
    {
        using var cancellation = new CancellationTokenSource();
        var current = CreateAssignment();
        var read = CreateReadResponse(current, "union-assignment-etag", onEtagRead: cancellation.Cancel);
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, read.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                current with { UnionId = "union-next" }, current, cancellation.Token));

        read.VerifyGet(value => value.Resource, Times.Once);
        read.VerifyGet(value => value.ETag, Times.Once);
        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
        read.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosProviderFailureIsNotConvertedIntoAConflict()
    {
        var current = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<DriverUnionAssignment>(
                DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("provider unavailable", HttpStatusCode.ServiceUnavailable, 0, "union-assignment-read", 0));

        var error = await Assert.ThrowsAsync<CosmosException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                current with { UnionId = "union-next" }, current, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }


    [Fact]
    public async Task CosmosCreateConflictPreservesCancellation()
    {
        var assignment = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.CreateItemAsync<DriverUnionAssignment>(
                assignment, It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnionAssignment>>(
                    new CosmosException("assignment already exists", HttpStatusCode.Conflict, 0, "union-assignment-create", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                assignment, expectedCurrent: null, cancellation.Token));

        container.Verify(value => value.CreateItemAsync<DriverUnionAssignment>(
            assignment, It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.UpsertItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementReadNotFoundPreservesCancellation()
    {
        var current = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<DriverUnionAssignment>(
                DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnionAssignment>>(
                    new CosmosException("assignment not found", HttpStatusCode.NotFound, 0, "union-assignment-read", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                current with { UnionId = "union-next" }, current, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    public async Task CosmosReplacementConditionalFailurePreservesCancellation(HttpStatusCode statusCode)
    {
        var current = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(current, "union-assignment-etag").Object);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReplaceItemAsync<DriverUnionAssignment>(
                It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnionAssignment>>(
                    new CosmosException("conditional assignment write failed", statusCode, 0, "union-assignment-write", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                current with { UnionId = "union-next" }, current, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }


    [Fact]
    public async Task CosmosCreateProviderFailureTakesPrecedenceOverCancellation()
    {
        var assignment = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        var unavailable = new CosmosException(
            "assignment create unavailable", HttpStatusCode.ServiceUnavailable, 0, "union-assignment-create", 0);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.CreateItemAsync<DriverUnionAssignment>(
                assignment, It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnionAssignment>>(unavailable);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                assignment, expectedCurrent: null, cancellation.Token));

        container.Verify(value => value.CreateItemAsync<DriverUnionAssignment>(
            assignment, It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosCreateProviderFailurePropagatesUnchangedWithoutCancellation()
    {
        var assignment = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        var unavailable = new CosmosException(
            "assignment create unavailable", HttpStatusCode.ServiceUnavailable, 0, "union-assignment-create", 0);
        container.Setup(value => value.CreateItemAsync<DriverUnionAssignment>(
                assignment, It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(unavailable);

        var exception = await Assert.ThrowsAsync<CosmosException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                assignment, expectedCurrent: null, CancellationToken.None));

        Assert.Same(unavailable, exception);
        container.Verify(value => value.CreateItemAsync<DriverUnionAssignment>(
            assignment, It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementProviderFailureTakesPrecedenceOverCancellation()
    {
        var current = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(current, "union-assignment-etag").Object);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReplaceItemAsync<DriverUnionAssignment>(
                It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnionAssignment>>(
                    new CosmosException("assignment replace unavailable", HttpStatusCode.ServiceUnavailable, 0, "union-assignment-write", 0));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                current with { UnionId = "union-next" }, current, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "union-assignment-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementProviderFailurePropagatesUnchangedWithoutCancellation()
    {
        var current = CreateAssignment();
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, CreateReadResponse(current, "union-assignment-etag").Object);
        var unavailable = new CosmosException(
            "assignment replace unavailable", HttpStatusCode.ServiceUnavailable, 0, "union-assignment-write", 0);
        container.Setup(value => value.ReplaceItemAsync<DriverUnionAssignment>(
                It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(unavailable);

        var exception = await Assert.ThrowsAsync<CosmosException>(() =>
            UnionAssignmentWriter.TrySaveAsync(
                container.Object, new ConcurrentDictionary<string, DriverUnionAssignment>(),
                current with { UnionId = "union-next" }, current, CancellationToken.None));

        Assert.Same(unavailable, exception);
        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.IsAny<DriverUnionAssignment>(), DriverId, It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "union-assignment-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    private static Mock<ItemResponse<DriverUnionAssignment>> CreateReadResponse(
        DriverUnionAssignment assignment,
        string? etag,
        Action? onResourceRead = null,
        Action? onEtagRead = null)
    {
        var response = new Mock<ItemResponse<DriverUnionAssignment>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(() =>
        {
            onResourceRead?.Invoke();
            return assignment;
        });
        response.SetupGet(value => value.ETag).Returns(() =>
        {
            onEtagRead?.Invoke();
            return etag!;
        });
        return response;
    }

    private static void SetupRead(
        Mock<Container> container,
        ItemResponse<DriverUnionAssignment> response)
    {
        container.Setup(value => value.ReadItemAsync<DriverUnionAssignment>(
                DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    [Fact]
    public async Task CosmosCreateReturnsCommittedAssignmentWhenCancellationArrivesAfterWrite()
    {
        using var cancellation = new CancellationTokenSource();
        var assignment = CreateAssignment();
        var created = new Mock<ItemResponse<DriverUnionAssignment>>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.CreateItemAsync<DriverUnionAssignment>(
                assignment,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(created.Object);
            });

        var result = await UnionAssignmentWriter.TrySaveAsync(
            container.Object,
            new ConcurrentDictionary<string, DriverUnionAssignment>(),
            assignment,
            expectedCurrent: null,
            cancellation.Token);

        Assert.True(result);
        container.Verify(value => value.CreateItemAsync<DriverUnionAssignment>(
            assignment, It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CosmosReplacementReturnsCommittedAssignmentWhenCancellationArrivesAfterWrite()
    {
        using var cancellation = new CancellationTokenSource();
        var current = CreateAssignment();
        var read = CreateReadResponse(current, "union-assignment-etag");
        var response = new Mock<ItemResponse<DriverUnionAssignment>>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        SetupRead(container, read.Object);
        container.Setup(value => value.ReplaceItemAsync<DriverUnionAssignment>(
                It.Is<DriverUnionAssignment>(assignment => assignment.UnionId == "union-next"),
                DriverId,
                It.IsAny<PartitionKey?>(),
                It.Is<ItemRequestOptions?>(options =>
                    options != null && options.IfMatchEtag == "union-assignment-etag"),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(response.Object);
            });

        var result = await UnionAssignmentWriter.TrySaveAsync(
            container.Object,
            new ConcurrentDictionary<string, DriverUnionAssignment>(),
            current with { UnionId = "union-next" },
            current,
            cancellation.Token);

        Assert.True(result);
        read.VerifyGet(value => value.Resource, Times.Once);
        read.VerifyGet(value => value.ETag, Times.Once);
        read.VerifyNoOtherCalls();
        container.Verify(value => value.ReadItemAsync<DriverUnionAssignment>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync<DriverUnionAssignment>(
            It.Is<DriverUnionAssignment>(assignment => assignment.UnionId == "union-next"),
            DriverId,
            It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options =>
                options != null && options.IfMatchEtag == "union-assignment-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    private static DriverUnionAssignment CreateAssignment()
    {
        var assignedAt = DateTimeOffset.Parse("2026-11-22T18:00:00Z");
        return new DriverUnionAssignment(
            DriverId, "union-current", "Current Union", "Active", assignedAt, assignedAt,
            "Operations assignment", "audit-event-current", "Assigned", "ops-actor",
            null, null, assignedAt, "operation-current", false);
    }
}
