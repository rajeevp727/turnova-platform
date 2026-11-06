using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class CosmosQueueStore(Container queueContainer) : IQueueStore
{
    public async Task<IReadOnlyList<DriverQueueEntry>> GetEligibleAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        var key = QueueKey(zoneId, serviceClass);
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.zoneServiceKey = @key AND c.isEligible = true")
            .WithParameter("@key", key);
        using var iterator = queueContainer.GetItemQueryIterator<QueueEntryDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(key) });

        var entries = new List<DriverQueueEntry>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            entries.AddRange(page.Select(x => new DriverQueueEntry(
                x.DriverId, x.ZoneId, x.ServiceClass, x.JoinedAt, x.Sequence, x.IsEligible)));
        }

        return entries.OrderBy(x => x.Sequence).ThenBy(x => x.JoinedAt).ToArray();
    }

    public async Task AddAsync(DriverQueueEntry entry, CancellationToken cancellationToken)
    {
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
        if (response[0].StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new InvalidOperationException("Driver is already in this queue.");
        throw new InvalidOperationException(
            $"Queue join and audit event could not be committed atomically (Cosmos status {(int)response.StatusCode}).");
    }

    public async Task<bool> RemoveAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        var key = QueueKey(zoneId, serviceClass);
        var audit = CreateAuditEvent(key, driverId, QueueAuditEventTypes.Left);
        var response = await queueContainer.CreateTransactionalBatch(new PartitionKey(key))
            .DeleteItem(driverId)
            .CreateItem(audit)
            .ExecuteAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return true;
        if (response[0].StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        throw new InvalidOperationException(
            $"Queue leave and audit event could not be committed atomically (Cosmos status {(int)response.StatusCode}).");
    }

    public async Task<bool> MoveToBackAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        var key = QueueKey(zoneId, serviceClass);
        try
        {
            var response = await queueContainer.ReadItemAsync<QueueEntryDocument>(
                driverId, new PartitionKey(key), cancellationToken: cancellationToken);
            var document = response.Resource;
            document.JoinedAt = DateTimeOffset.UtcNow;
            document.Sequence = document.JoinedAt.UtcTicks;
            var audit = CreateAuditEvent(key, driverId, QueueAuditEventTypes.MovedToBack);
            var batch = await queueContainer.CreateTransactionalBatch(new PartitionKey(key))
                .ReplaceItem(driverId, document, new TransactionalBatchItemRequestOptions { IfMatchEtag = response.ETag })
                .CreateItem(audit)
                .ExecuteAsync(cancellationToken);
            if (batch.IsSuccessStatusCode) return true;
            if (batch[0].StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
                return false;
            throw new InvalidOperationException(
                $"Queue rotation and audit event could not be committed atomically (Cosmos status {(int)batch.StatusCode}).");
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
    }

    public async Task<bool> MoveToBackOnceAsync(string driverId, string zoneId, string serviceClass, string offerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(offerId)) throw new ArgumentException("Offer ID is required.", nameof(offerId));
        var key = QueueKey(zoneId, serviceClass);
        try
        {
            var response = await queueContainer.ReadItemAsync<QueueEntryDocument>(driverId, new PartitionKey(key), cancellationToken: cancellationToken);
            var document = response.Resource;
            if (document.LastRotationOfferId == offerId) return true;
            document.JoinedAt = DateTimeOffset.UtcNow;
            document.Sequence = document.JoinedAt.UtcTicks;
            document.LastRotationOfferId = offerId;
            var audit = CreateAuditEvent(key, driverId, QueueAuditEventTypes.RotatedToBack, offerId);
            var batch = await queueContainer.CreateTransactionalBatch(new PartitionKey(key))
                .ReplaceItem(driverId, document, new TransactionalBatchItemRequestOptions { IfMatchEtag = response.ETag })
                .CreateItem(audit)
                .ExecuteAsync(cancellationToken);
            if (batch.IsSuccessStatusCode) return true;
            if (batch[0].StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
                return false;
            throw new InvalidOperationException(
                $"Offer rotation and audit event could not be committed atomically (Cosmos status {(int)batch.StatusCode}).");
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        { return false; }
    }

    public async Task<IReadOnlyList<QueueAuditEvent>> GetAuditEventsAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        var key = QueueKey(zoneId, serviceClass);
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.zoneServiceKey = @key AND c.documentType = @documentType ORDER BY c.occurredAtUtc DESC, c.eventId DESC")
            .WithParameter("@key", key)
            .WithParameter("@documentType", "queue-audit");
        using var iterator = queueContainer.GetItemQueryIterator<QueueAuditEvent>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(key) });
        var events = new List<QueueAuditEvent>();
        while (iterator.HasMoreResults)
            events.AddRange(await iterator.ReadNextAsync(cancellationToken));
        return events;
    }

    private static QueueAuditEvent CreateAuditEvent(
        string zoneServiceKey, string driverId, string eventType, string? offerId = null) =>
        new(Guid.NewGuid().ToString("N"), zoneServiceKey, driverId, eventType, DateTimeOffset.UtcNow, offerId);

    private static string QueueKey(string zoneId, string serviceClass) =>
        $"{zoneId.Trim().ToLowerInvariant()}|{serviceClass.Trim().ToLowerInvariant()}";

    private sealed class QueueEntryDocument
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
