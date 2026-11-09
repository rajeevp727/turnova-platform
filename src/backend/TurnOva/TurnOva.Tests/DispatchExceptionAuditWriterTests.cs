using System.Collections.Concurrent;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class DispatchExceptionAuditWriterTests
{
    [Fact]
    public async Task RecordAsync_CreatesOneSafeTraceScopedRecordWithoutReadBack()
    {
        var store = new FakeQueueAuditEventStore { CreateResult = true };
        var writer = new DispatchExceptionAuditWriter(store);

        await writer.RecordAsync(
            "driver-actor-1", "Zone-A", "CAB", "trace-123",
            QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None);

        var audit = Assert.IsType<QueueAuditEvent>(store.Created);
        Assert.Equal("dispatch-exception-trace-123", audit.EventId);
        Assert.Equal("zone-a|cab", audit.ZoneServiceKey);
        Assert.Equal("driver-actor-1", audit.DriverId);
        Assert.Equal(QueueAuditEventTypes.DispatchException, audit.EventType);
        Assert.Equal("trace-123", audit.TraceId);
        Assert.Equal(QueueAuditReasonCategories.QueueReadFailed, audit.ReasonCategory);
        Assert.Null(audit.OfferId);
        Assert.Equal(TimeSpan.Zero, audit.OccurredAtUtc.Offset);
        Assert.Equal("zone-a|cab", store.CreatePartitionKey);
        Assert.Equal(0, store.ReadCount);
    }

    [Fact]
    public async Task RecordAsync_TreatsConflictAsIdempotentOnlyWhenCanonicalMetadataMatches()
    {
        var store = new FakeQueueAuditEventStore
        {
            CreateResult = false,
            Existing = ExpectedAudit() with
            {
                OccurredAtUtc = DateTimeOffset.Parse("2026-11-08T12:00:00Z")
            }
        };
        var writer = new DispatchExceptionAuditWriter(store);

        await writer.RecordAsync(
            "driver-actor-1", "zone-a", "cab", "trace-123",
            QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None);

        Assert.Equal(1, store.ReadCount);
        Assert.Equal("dispatch-exception-trace-123", store.ReadEventId);
        Assert.Equal("zone-a|cab", store.ReadPartitionKey);
    }

    [Theory]
    [InlineData("event-id")]
    [InlineData("zone")]
    [InlineData("actor")]
    [InlineData("event-type")]
    [InlineData("trace")]
    [InlineData("reason")]
    [InlineData("offer")]
    public async Task RecordAsync_DoesNotAcceptConflictWhenCanonicalMetadataDiffers(string mismatch)
    {
        var existing = ExpectedAudit();
        existing = mismatch switch
        {
            "event-id" => existing with { EventId = "dispatch-exception-other" },
            "zone" => existing with { ZoneServiceKey = "zone-b|cab" },
            "actor" => existing with { DriverId = "different-driver" },
            "event-type" => existing with { EventType = QueueAuditEventTypes.Joined },
            "trace" => existing with { TraceId = "different-trace" },
            "reason" => existing with { ReasonCategory = QueueAuditReasonCategories.DriverReservationFailed },
            "offer" => existing with { OfferId = "offer-1" },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };

        var store = new FakeQueueAuditEventStore { CreateResult = false, Existing = existing };
        var writer = new DispatchExceptionAuditWriter(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.RecordAsync(
            "driver-actor-1", "zone-a", "cab", "trace-123",
            QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None));

        Assert.Equal(1, store.ReadCount);
    }

    [Fact]
    public async Task RecordAsync_FailsClosedWhenConflictCannotBeReadBack()
    {
        var store = new FakeQueueAuditEventStore { CreateResult = false, Existing = null };
        var writer = new DispatchExceptionAuditWriter(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.RecordAsync(
            "driver-actor-1", "zone-a", "cab", "trace-123",
            QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_PropagatesCancellationInsteadOfTreatingItAsAConflict()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new FakeQueueAuditEventStore
        {
            CreateException = new OperationCanceledException(cancellation.Token)
        };
        var writer = new DispatchExceptionAuditWriter(store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.RecordAsync(
            "driver-actor-1", "zone-a", "cab", "trace-123",
            QueueAuditReasonCategories.QueueReadFailed, cancellation.Token));

        Assert.Equal(0, store.ReadCount);
    }

    [Fact]
    public async Task RecordAsync_ConcurrentMatchingRetriesCreateOneCanonicalEvent()
    {
        var store = new ConcurrentQueueAuditEventStore();
        var writer = new DispatchExceptionAuditWriter(store);

        await Task.WhenAll(
            writer.RecordAsync(
                "driver-actor-1", "zone-a", "cab", "trace-concurrent",
                QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None),
            writer.RecordAsync(
                "driver-actor-1", "zone-a", "cab", "trace-concurrent",
                QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None));

        Assert.Equal(2, store.CreateAttempts);
        Assert.Equal(1, store.ReadCount);
        var canonical = Assert.Single(store.Events);
        Assert.Equal("dispatch-exception-trace-concurrent", canonical.EventId);
        Assert.Equal("driver-actor-1", canonical.DriverId);
        Assert.Equal("trace-concurrent", canonical.TraceId);
        Assert.Equal(QueueAuditReasonCategories.QueueReadFailed, canonical.ReasonCategory);
    }

    [Fact]
    public async Task RecordAsync_ConcurrentTraceReuseWithDifferentActorFailsClosed()
    {
        var store = new ConcurrentQueueAuditEventStore();
        var writer = new DispatchExceptionAuditWriter(store);

        var outcomes = await Task.WhenAll(
            CaptureAsync(() => writer.RecordAsync(
                "driver-actor-1", "zone-a", "cab", "trace-reused",
                QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None)),
            CaptureAsync(() => writer.RecordAsync(
                "driver-actor-2", "zone-a", "cab", "trace-reused",
                QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None)));

        Assert.Single(outcomes.Where(exception => exception is null));
        Assert.IsType<InvalidOperationException>(Assert.Single(outcomes.Where(exception => exception is not null)));
        Assert.Equal(2, store.CreateAttempts);
        Assert.Equal(1, store.ReadCount);
        var canonical = Assert.Single(store.Events);
        Assert.Contains(canonical.DriverId, new[] { "driver-actor-1", "driver-actor-2" });
    }

    [Fact]
    public async Task RecordAsync_PropagatesCancellationDuringCanonicalConflictReadBack()
    {
        var store = new FakeQueueAuditEventStore
        {
            CreateResult = false,
            ReadException = new OperationCanceledException("canonical read cancelled")
        };
        var writer = new DispatchExceptionAuditWriter(store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.RecordAsync(
            "driver-actor-1", "zone-a", "cab", "trace-read-cancelled",
            QueueAuditReasonCategories.QueueReadFailed, CancellationToken.None));

        Assert.Equal(1, store.ReadCount);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class ConcurrentQueueAuditEventStore : IQueueAuditEventStore
    {
        private readonly ConcurrentDictionary<string, QueueAuditEvent> _events = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _bothCreateAttempts =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _createAttempts;
        private int _readCount;

        public int CreateAttempts => Volatile.Read(ref _createAttempts);
        public int ReadCount => Volatile.Read(ref _readCount);
        public IReadOnlyCollection<QueueAuditEvent> Events => _events.Values.ToArray();

        public async Task<bool> TryCreateAsync(
            QueueAuditEvent auditEvent, string partitionKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _createAttempts) == 2)
                _bothCreateAttempts.TrySetResult();

            await _bothCreateAttempts.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return _events.TryAdd($"{partitionKey}|{auditEvent.EventId}", auditEvent);
        }

        public Task<QueueAuditEvent?> ReadAsync(
            string eventId, string partitionKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            _events.TryGetValue($"{partitionKey}|{eventId}", out var existing);
            return Task.FromResult(existing);
        }
    }

    private static QueueAuditEvent ExpectedAudit() => new(
        "dispatch-exception-trace-123",
        "zone-a|cab",
        "driver-actor-1",
        QueueAuditEventTypes.DispatchException,
        DateTimeOffset.Parse("2026-11-09T00:00:00Z"),
        TraceId: "trace-123",
        ReasonCategory: QueueAuditReasonCategories.QueueReadFailed);

    private sealed class FakeQueueAuditEventStore : IQueueAuditEventStore
    {
        public bool CreateResult { get; init; }
        public QueueAuditEvent? Existing { get; init; }
        public QueueAuditEvent? Created { get; private set; }
        public string? CreatePartitionKey { get; private set; }
        public string? ReadEventId { get; private set; }
        public string? ReadPartitionKey { get; private set; }
        public int ReadCount { get; private set; }
        public Exception? CreateException { get; init; }
        public Exception? ReadException { get; init; }

        public Task<bool> TryCreateAsync(
            QueueAuditEvent auditEvent, string partitionKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CreateException is not null) throw CreateException;
            Created = auditEvent;
            CreatePartitionKey = partitionKey;
            return Task.FromResult(CreateResult);
        }

        public Task<QueueAuditEvent?> ReadAsync(
            string eventId, string partitionKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (ReadException is not null) throw ReadException;
            ReadEventId = eventId;
            ReadPartitionKey = partitionKey;
            return Task.FromResult(Existing);
        }
    }
}
