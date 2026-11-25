using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class DispatchExceptionAuditInputBoundaryTests
{
    public static IEnumerable<object[]> InvalidInputs =>
    [
        [null!, "zone-a", "cab", "trace-123", QueueAuditReasonCategories.QueueReadFailed],
        [" ", "zone-a", "cab", "trace-123", QueueAuditReasonCategories.QueueReadFailed],
        ["driver-actor-1", " ", "cab", "trace-123", QueueAuditReasonCategories.QueueReadFailed],
        ["driver-actor-1", "zone-a", " ", "trace-123", QueueAuditReasonCategories.QueueReadFailed],
        ["driver-actor-1", "zone-a", "auto", "trace-123", QueueAuditReasonCategories.QueueReadFailed],
        ["driver-actor-1", "zone-a", "cab", " ", QueueAuditReasonCategories.QueueReadFailed],
        ["driver-actor-1", "zone-a", "cab", new string('x', 129), QueueAuditReasonCategories.QueueReadFailed],
        ["driver-actor-1", "zone-a", "cab", "trace/unsafe", QueueAuditReasonCategories.QueueReadFailed],
        ["driver-actor-1", "zone-a", "cab", "tracé-123", QueueAuditReasonCategories.QueueReadFailed],
        ["driver-actor-1", "zone-a", "cab", "trace-123", "sensitive-exception-message"]
    ];

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task AuditWriterRejectsInvalidMetadataBeforeCallingPersistence(
        string actorId,
        string zoneId,
        string serviceClass,
        string traceId,
        string reasonCategory)
    {
        var store = new RecordingQueueAuditEventStore();
        var writer = new DispatchExceptionAuditWriter(store);

        await Assert.ThrowsAsync<ArgumentException>(() => writer.RecordAsync(
            actorId, zoneId, serviceClass, traceId, reasonCategory, CancellationToken.None));

        Assert.Equal(0, store.CreateCount);
        Assert.Equal(0, store.ReadCount);
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task InMemoryStoreRejectsInvalidMetadataBeforeDeduplicationOrAuditMutation(
        string actorId,
        string zoneId,
        string serviceClass,
        string traceId,
        string reasonCategory)
    {
        var store = new InMemoryQueueStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordDispatchExceptionAsync(
            actorId, zoneId, serviceClass, traceId, reasonCategory, CancellationToken.None));

        Assert.Empty(await store.GetAuditEventsAsync("zone-a", "cab", CancellationToken.None));
        Assert.Empty(await store.GetAuditEventsAsync("zone-a", "auto", CancellationToken.None));
    }

    [Fact]
    public async Task InMemoryStoreAcceptsMatchingRetryAndRejectsTraceReuseWithDifferentMetadata()
    {
        var store = new InMemoryQueueStore();

        await store.RecordDispatchExceptionAsync(
            "driver-actor-1", "zone-a", "cab", "trace-idempotent",
            QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None);
        await store.RecordDispatchExceptionAsync(
            "driver-actor-1", "zone-a", "cab", "trace-idempotent",
            QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordDispatchExceptionAsync(
            "driver-actor-2", "zone-a", "cab", "trace-idempotent",
            QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordDispatchExceptionAsync(
            "driver-actor-1", "zone-a", "cab", "trace-idempotent",
            QueueAuditReasonCategories.DriverReservationFailed, CancellationToken.None));

        var audit = Assert.Single(await store.GetAuditEventsAsync("zone-a", "cab", CancellationToken.None));
        Assert.Equal("driver-actor-1", audit.DriverId);
        Assert.Equal(QueueAuditReasonCategories.QueueReadFailed, audit.ReasonCategory);
    }

    [Fact]
    public async Task AuditWriterCancellationTakesPrecedenceOverInvalidMetadata()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new RecordingQueueAuditEventStore();
        var writer = new DispatchExceptionAuditWriter(store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.RecordAsync(
            " ", " ", " ", " ", "unknown", cancellation.Token));

        Assert.Equal(0, store.CreateCount);
        Assert.Equal(0, store.ReadCount);
    }

    [Fact]
    public async Task InMemoryStoreCancellationTakesPrecedenceOverInvalidMetadata()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new InMemoryQueueStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RecordDispatchExceptionAsync(
            " ", " ", " ", " ", "unknown", cancellation.Token));

        Assert.Empty(await store.GetAuditEventsAsync("zone-a", "cab", CancellationToken.None));
    }

    private sealed class RecordingQueueAuditEventStore : IQueueAuditEventStore
    {
        public int CreateCount { get; private set; }
        public int ReadCount { get; private set; }

        public Task<bool> TryCreateAsync(
            QueueAuditEvent auditEvent,
            string partitionKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            return Task.FromResult(true);
        }

        public Task<QueueAuditEvent?> ReadAsync(
            string eventId,
            string partitionKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult<QueueAuditEvent?>(null);
        }
    }
}
