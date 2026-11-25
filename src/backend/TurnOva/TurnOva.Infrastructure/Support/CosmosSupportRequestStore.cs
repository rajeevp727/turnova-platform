using System.Net;
using Microsoft.Azure.Cosmos;
using TurnOva.Application.Support;
using TurnOva.Domain.Support;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Infrastructure.Support;

public sealed class CosmosSupportRequestStore(Container container) : ISupportRequestStore
{
    public async Task<SupportRequestWriteResult> CreateOrGetAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _ = await CosmosReadResponseGuard.AwaitCommittedWriteWithCancellationPrecedenceAsync(
                () => container.CreateItemAsync(
                    request,
                    new PartitionKey(request.ReporterId),
                    cancellationToken: cancellationToken),
                cancellationToken);
            return new SupportRequestWriteResult(SupportRequestWriteStatus.Created, request);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var existingResponse = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                    () => container.ReadItemAsync<SupportRequest>(
                        request.Id,
                        new PartitionKey(request.ReporterId),
                        cancellationToken: cancellationToken),
                    cancellationToken);
                var existing = CosmosReadResponseGuard.ReadResource(
                    existingResponse,
                    item => string.Equals(item.Id, request.Id, StringComparison.Ordinal) &&
                        string.Equals(item.ReporterId, request.ReporterId, StringComparison.Ordinal),
                    "support request",
                    cancellationToken,
                    "Cosmos returned a malformed support request resource.");

                var sameSubmission =
                    string.Equals(existing.ReporterId, request.ReporterId, StringComparison.Ordinal) &&
                    string.Equals(existing.ReporterRole, request.ReporterRole, StringComparison.Ordinal) &&
                    string.Equals(existing.Subject, request.Subject, StringComparison.Ordinal) &&
                    string.Equals(existing.Description, request.Description, StringComparison.Ordinal);

                // Always return the canonical persisted item after a create conflict.
                // The API maps Conflict to a generic response and never echoes this content.
                return new SupportRequestWriteResult(
                    sameSubmission ? SupportRequestWriteStatus.Replayed : SupportRequestWriteStatus.Conflict,
                    existing);
            }
            catch (CosmosException missing) when (missing.StatusCode == HttpStatusCode.NotFound)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new SupportRequestWriteResult(SupportRequestWriteStatus.Conflict, request);
            }
        }
    }
}
