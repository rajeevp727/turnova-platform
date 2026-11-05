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

        try
        {
            await queueContainer.CreateItemAsync(
                document, new PartitionKey(document.ZoneServiceKey),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("Driver is already in this queue.", ex);
        }
    }

    public async Task<bool> RemoveAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken)
    {
        var key = QueueKey(zoneId, serviceClass);
        try
        {
            await queueContainer.DeleteItemAsync<QueueEntryDocument>(
                driverId, new PartitionKey(key), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
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
            await queueContainer.ReplaceItemAsync(
                document, driverId, new PartitionKey(key),
                new ItemRequestOptions { IfMatchEtag = response.ETag }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
    }

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
    }
}
