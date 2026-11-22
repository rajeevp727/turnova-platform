using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosQueueAuditEventStoreCancellationTests
{
    [Fact]
    public async Task BothOperationsHonorPreCancelledTokenBeforeCosmosWork()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var store = new CosmosQueueAuditEventStore(container.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryCreateAsync(CreateEvent(), "zone-a|cab", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.ReadAsync("event-1", "zone-a|cab", cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateConflictDoesNotHideCancellation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var conflict = new CosmosException(
            "audit event already exists", HttpStatusCode.Conflict, 0, "queue-audit-create-test", 0);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<QueueAuditEvent>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<QueueAuditEvent>>(conflict);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueAuditEventStore(container.Object)
                .TryCreateAsync(CreateEvent(), "zone-a|cab", cancellation.Token));

        container.Verify(value => value.CreateItemAsync(
            It.IsAny<QueueAuditEvent>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingReadDoesNotHideCancellation()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var missing = new CosmosException(
            "audit event not found", HttpStatusCode.NotFound, 0, "queue-audit-read-test", 0);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<QueueAuditEvent>(
                "event-1",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<QueueAuditEvent>>(missing);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueAuditEventStore(container.Object)
                .ReadAsync("event-1", "zone-a|cab", cancellation.Token));

        container.Verify(value => value.ReadItemAsync<QueueAuditEvent>(
            "event-1",
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SuccessfulReadHonorsCancellationBeforeReturningSnapshot()
    {
        var auditEvent = CreateEvent();
        var response = new Mock<ItemResponse<QueueAuditEvent>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(auditEvent);
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<QueueAuditEvent>(
                auditEvent.EventId,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(response.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CosmosQueueAuditEventStore(container.Object)
                .ReadAsync(auditEvent.EventId, auditEvent.ZoneServiceKey, cancellation.Token));

        container.Verify(value => value.ReadItemAsync<QueueAuditEvent>(
            auditEvent.EventId,
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
        response.VerifyGet(value => value.Resource, Times.Never);
    }

    [Fact]
    public async Task SuccessfulCreateRemainsAuthoritativeWhenCancellationArrivesAfterCommit()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<QueueAuditEvent>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(Mock.Of<ItemResponse<QueueAuditEvent>>());
            });

        var created = await new CosmosQueueAuditEventStore(container.Object)
            .TryCreateAsync(CreateEvent(), "zone-a|cab", cancellation.Token);

        Assert.True(created);
        container.Verify(value => value.CreateItemAsync(
            It.IsAny<QueueAuditEvent>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateConflictWithoutCancellationStillReturnsFalse()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var conflict = new CosmosException(
            "audit event already exists", HttpStatusCode.Conflict, 0, "queue-audit-create-test", 0);
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<QueueAuditEvent>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(conflict);

        var created = await new CosmosQueueAuditEventStore(container.Object)
            .TryCreateAsync(CreateEvent(), "zone-a|cab", CancellationToken.None);

        Assert.False(created);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingReadWithoutCancellationStillReturnsNull()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var missing = new CosmosException(
            "audit event not found", HttpStatusCode.NotFound, 0, "queue-audit-read-test", 0);
        container.Setup(value => value.ReadItemAsync<QueueAuditEvent>(
                "event-1",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(missing);

        var result = await new CosmosQueueAuditEventStore(container.Object)
            .ReadAsync("event-1", "zone-a|cab", CancellationToken.None);

        Assert.Null(result);
        container.VerifyNoOtherCalls();
    }

    private static QueueAuditEvent CreateEvent() =>
        new(
            "event-1",
            "zone-a|cab",
            "driver-1",
            QueueAuditEventTypes.DispatchException,
            DateTimeOffset.Parse("2026-11-23T00:00:00Z"),
            TraceId: "trace-1",
            ReasonCategory: QueueAuditReasonCategories.QueueReadFailed);
}
