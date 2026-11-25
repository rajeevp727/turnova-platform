using Microsoft.Azure.Cosmos;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;

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
            _ = await CosmosReadResponseGuard.AwaitCommittedWriteWithCancellationPrecedenceAsync(
                () => container.CreateItemAsync(
                    auditEvent,
                    new PartitionKey(partitionKey),
                    cancellationToken: cancellationToken),
                cancellationToken);
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
            var response = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => container.ReadItemAsync<QueueAuditEvent>(
                    eventId,
                    new PartitionKey(partitionKey),
                    cancellationToken: cancellationToken),
                cancellationToken);
            var auditEvent = CosmosReadResponseGuard.ReadResource(
                response,
                item => string.Equals(item.EventId, eventId, StringComparison.Ordinal) &&
                    string.Equals(item.ZoneServiceKey, partitionKey, StringComparison.Ordinal),
                "queue audit event",
                cancellationToken,
                "Cosmos returned a malformed queue audit event resource.");

            return auditEvent;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }
}
