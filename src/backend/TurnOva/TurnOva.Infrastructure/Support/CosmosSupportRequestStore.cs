using System.Net;
using Microsoft.Azure.Cosmos;
using TurnOva.Application.Support;
using TurnOva.Domain.Support;

namespace TurnOva.Infrastructure.Support;

public sealed class CosmosSupportRequestStore(Container container) : ISupportRequestStore
{
    public async Task<SupportRequestWriteResult> CreateOrGetAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await container.CreateItemAsync(
                request,
                new PartitionKey(request.ReporterId),
                cancellationToken: cancellationToken);
            return new SupportRequestWriteResult(SupportRequestWriteStatus.Created, request);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            try
            {
                var existing = (await container.ReadItemAsync<SupportRequest>(
                    request.Id,
                    new PartitionKey(request.ReporterId),
                    cancellationToken: cancellationToken)).Resource;

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
                return new SupportRequestWriteResult(SupportRequestWriteStatus.Conflict, request);
            }
        }
    }
}
