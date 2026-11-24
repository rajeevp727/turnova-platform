using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class CosmosQueueStore(Container queueContainer) : IQueueStore
{
    private readonly DispatchExceptionAuditWriter _dispatchExceptionAuditWriter =
        new(new CosmosQueueAuditEventStore(queueContainer));
    public async Task<IReadOnlyList<DriverQueueEntry>> GetEligibleAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = QueueKey(zoneId, serviceClass);
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.zoneServiceKey = @key AND c.isEligible = true")
            .WithParameter("@key", key);
        using var iterator = queueContainer.GetItemQueryIterator<QueueEntryDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(key) });

        var entries = new List<DriverQueueEntry>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!iterator.HasMoreResults) break;

            var page = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => iterator.ReadNextAsync(cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = RequireEligibleQueryItem(item, key);
                entries.Add(new DriverQueueEntry(
                    entry.DriverId, entry.ZoneId, entry.ServiceClass, entry.JoinedAt, entry.Sequence, entry.IsEligible));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return entries.OrderBy(x => x.Sequence).ThenBy(x => x.JoinedAt).ToArray();
    }

    public async Task AddAsync(DriverQueueEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);
        var document = new QueueEntryDocument
        {
            Id = entry.DriverId,
            DriverId = entry.DriverId,
            ZoneId = entry.ZoneId,
            ServiceClass = entry.ServiceClass,
            ZoneServiceKey = QueueKey(entry.ZoneId, entry.ServiceClass),
            JoinedAt = entry.JoinedAt,
            Sequence = entry.JoinedAt.UtcTicks,
            IsEligible = entry.IsEligible
        };

        var audit = CreateAuditEvent(document.ZoneServiceKey, entry.DriverId, QueueAuditEventTypes.Joined);
        var response = await queueContainer.CreateTransactionalBatch(new PartitionKey(document.ZoneServiceKey))
            .CreateItem(document)
            .CreateItem(audit)
            .ExecuteAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return;
        cancellationToken.ThrowIfCancellationRequested();
        if (response[0].StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new InvalidOperationException("Driver is already in this queue.");
        throw new InvalidOperationException(
            $"Queue join and audit event could not be committed atomically (Cosmos status {(int)response.StatusCode}).");
    }

    public async Task<bool> RemoveAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = QueueKey(zoneId, serviceClass);
        try
        {
            var current = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => queueContainer.ReadItemAsync<QueueEntryDocument>(
                    driverId, new PartitionKey(key), cancellationToken: cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _ = RequireResource(current, driverId, key);

            // A leave must not delete a queue entry that was replaced or rejoined after
            // this read. Without an observed ETag, fail closed rather than issuing an
            // unconditional transactional delete.
            if (!CosmosEtagPolicy.HasObservedEtag(current.ETag))
                return false;

            var audit = CreateAuditEvent(key, driverId, QueueAuditEventTypes.Left);
            var response = await queueContainer.CreateTransactionalBatch(new PartitionKey(key))
                .DeleteItem(driverId, new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
                .CreateItem(audit)
                .ExecuteAsync(cancellationToken);
            if (response.IsSuccessStatusCode) return true;
            cancellationToken.ThrowIfCancellationRequested();
            if (response[0].StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
                return false;
            throw new InvalidOperationException(
                $"Queue leave and audit event could not be committed atomically (Cosmos status {(int)response.StatusCode}).");
        }
        catch (CosmosException ex) when (
            ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    public async Task<bool> MoveToBackAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = QueueKey(zoneId, serviceClass);
        try
        {
            var response = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => queueContainer.ReadItemAsync<QueueEntryDocument>(
                    driverId, new PartitionKey(key), cancellationToken: cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var document = RequireResource(response, driverId, key);
            if (!CosmosEtagPolicy.HasObservedEtag(response.ETag))
                return false;
            document.JoinedAt = DateTimeOffset.UtcNow;
            document.Sequence = document.JoinedAt.UtcTicks;
            var audit = CreateAuditEvent(key, driverId, QueueAuditEventTypes.MovedToBack);
            var batch = await queueContainer.CreateTransactionalBatch(new PartitionKey(key))
                .ReplaceItem(driverId, document, new TransactionalBatchItemRequestOptions { IfMatchEtag = response.ETag })
                .CreateItem(audit)
                .ExecuteAsync(cancellationToken);
            if (batch.IsSuccessStatusCode) return true;
            cancellationToken.ThrowIfCancellationRequested();
            if (batch[0].StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
                return false;
            throw new InvalidOperationException(
                $"Queue rotation and audit event could not be committed atomically (Cosmos status {(int)batch.StatusCode}).");
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    public async Task<bool> MoveToBackOnceAsync(string driverId, string zoneId, string serviceClass, string offerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(offerId)) throw new ArgumentException("Offer ID is required.", nameof(offerId));
        var key = QueueKey(zoneId, serviceClass);
        try
        {
            var response = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => queueContainer.ReadItemAsync<QueueEntryDocument>(
                    driverId, new PartitionKey(key), cancellationToken: cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var document = RequireResource(response, driverId, key);
            if (document.LastRotationOfferId == offerId) return true;
            if (!CosmosEtagPolicy.HasObservedEtag(response.ETag))
                return false;
            document.JoinedAt = DateTimeOffset.UtcNow;
            document.Sequence = document.JoinedAt.UtcTicks;
            document.LastRotationOfferId = offerId;
            var audit = CreateAuditEvent(key, driverId, QueueAuditEventTypes.RotatedToBack, offerId);
            var batch = await queueContainer.CreateTransactionalBatch(new PartitionKey(key))
                .ReplaceItem(driverId, document, new TransactionalBatchItemRequestOptions { IfMatchEtag = response.ETag })
                .CreateItem(audit)
                .ExecuteAsync(cancellationToken);
            if (batch.IsSuccessStatusCode) return true;
            cancellationToken.ThrowIfCancellationRequested();
            if (batch[0].StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
                return false;
            throw new InvalidOperationException(
                $"Offer rotation and audit event could not be committed atomically (Cosmos status {(int)batch.StatusCode}).");
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        { cancellationToken.ThrowIfCancellationRequested(); return false; }
    }

    public Task RecordDispatchExceptionAsync(
        string actorId,
        string zoneId,
        string serviceClass,
        string traceId,
        string reasonCategory,
        CancellationToken cancellationToken) =>
        _dispatchExceptionAuditWriter.RecordAsync(
            actorId, zoneId, serviceClass, traceId, reasonCategory, cancellationToken);

    public async Task<IReadOnlyList<QueueAuditEvent>> GetAuditEventsAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = QueueKey(zoneId, serviceClass);
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.zoneServiceKey = @key AND c.documentType = @documentType ORDER BY c.occurredAtUtc DESC")
            .WithParameter("@key", key)
            .WithParameter("@documentType", "queue-audit");
        using var iterator = queueContainer.GetItemQueryIterator<QueueAuditEvent>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(key) });
        var events = new List<QueueAuditEvent>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!iterator.HasMoreResults) break;

            var page = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => iterator.ReadNextAsync(cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(RequireAuditQueryItem(item, key));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return events;
    }

    private static QueueAuditEvent CreateAuditEvent(
        string zoneServiceKey,
        string driverId,
        string eventType,
        string? offerId = null,
        string? traceId = null,
        string? reasonCategory = null) =>
        new(Guid.NewGuid().ToString("N"), zoneServiceKey, driverId, eventType,
            DateTimeOffset.UtcNow, offerId, traceId, reasonCategory);

    private static QueueEntryDocument RequireEligibleQueryItem(
        QueueEntryDocument? document,
        string expectedZoneServiceKey)
    {
        if (document is null)
            throw new InvalidOperationException("Cosmos returned an empty queue entry query item.");

        if (string.IsNullOrWhiteSpace(document.Id) ||
            string.IsNullOrWhiteSpace(document.DriverId) ||
            !string.Equals(document.Id, document.DriverId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(document.ZoneId) ||
            string.IsNullOrWhiteSpace(document.ServiceClass) ||
            !document.IsEligible ||
            !string.Equals(document.ZoneServiceKey, expectedZoneServiceKey, StringComparison.Ordinal) ||
            !string.Equals(QueueKey(document.ZoneId, document.ServiceClass), expectedZoneServiceKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Cosmos returned a malformed queue entry query item.");

        return document;
    }

    private static QueueAuditEvent RequireAuditQueryItem(
        QueueAuditEvent? auditEvent,
        string expectedZoneServiceKey)
    {
        if (auditEvent is null)
            throw new InvalidOperationException("Cosmos returned an empty queue audit event query item.");

        if (string.IsNullOrWhiteSpace(auditEvent.EventId) ||
            string.IsNullOrWhiteSpace(auditEvent.DriverId) ||
            string.IsNullOrWhiteSpace(auditEvent.EventType) ||
            !string.Equals(auditEvent.ZoneServiceKey, expectedZoneServiceKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Cosmos returned a malformed queue audit event query item.");

        return auditEvent;
    }

    private static QueueEntryDocument RequireResource(
        ItemResponse<QueueEntryDocument> response,
        string expectedDriverId,
        string expectedZoneServiceKey)
    {
        var document = response.Resource ?? throw new InvalidOperationException(
            "Cosmos returned an empty queue entry resource.");

        if (string.IsNullOrWhiteSpace(document.Id) ||
            string.IsNullOrWhiteSpace(document.DriverId) ||
            !string.Equals(document.Id, expectedDriverId, StringComparison.Ordinal) ||
            !string.Equals(document.DriverId, expectedDriverId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(document.ZoneId) ||
            string.IsNullOrWhiteSpace(document.ServiceClass) ||
            !string.Equals(document.ZoneServiceKey, expectedZoneServiceKey, StringComparison.Ordinal) ||
            !string.Equals(QueueKey(document.ZoneId, document.ServiceClass), expectedZoneServiceKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Cosmos returned a malformed queue entry resource.");

        return document;
    }

    private static string QueueKey(string zoneId, string serviceClass) =>
        $"{zoneId.Trim().ToLowerInvariant()}|{serviceClass.Trim().ToLowerInvariant()}";

    internal sealed class QueueEntryDocument
    {
        public string Id { get; set; } = string.Empty;
        public string DriverId { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public string ServiceClass { get; set; } = string.Empty;
        public string ZoneServiceKey { get; set; } = string.Empty;
        public DateTimeOffset JoinedAt { get; set; }
        public long Sequence { get; set; }
        public bool IsEligible { get; set; }
        public string? LastRotationOfferId { get; set; }
    }
}
