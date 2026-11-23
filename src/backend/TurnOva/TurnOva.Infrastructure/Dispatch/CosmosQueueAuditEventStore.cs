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
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await container.CreateItemAsync(
                auditEvent,
                new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
            // A successful create is durable. Do not turn a committed audit write into a
            // cancellation result if cancellation arrives after Cosmos confirms the commit.
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    public async Task<QueueAuditEvent?> ReadAsync(
        string eventId,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var response = await container.ReadItemAsync<QueueAuditEvent>(
                eventId,
                new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return response.Resource ?? throw new InvalidOperationException(
                "Cosmos returned an empty queue audit event resource.");
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }
}
