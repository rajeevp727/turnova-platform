using Microsoft.Azure.Cosmos;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

/// <summary>Cosmos adapter for single-item dispatch-exception audit writes and canonical conflict read-back.</summary>
internal sealed class CosmosQueueAuditEventStore(Container container) : IQueueAuditEventStore
{
    public async Task<bool> TryCreateAsync(
        QueueAuditEvent auditEvent,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await container.CreateItemAsync(
                auditEvent,
                new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<QueueAuditEvent?> ReadAsync(
        string eventId,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<QueueAuditEvent>(
                eventId,
                new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
