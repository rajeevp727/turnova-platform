using System.Collections.Concurrent;
using TurnOva.Application.Support;
using TurnOva.Domain.Support;

namespace TurnOva.Infrastructure.Support;

public sealed class InMemorySupportRequestStore : ISupportRequestStore
{
    private readonly ConcurrentDictionary<string, SupportRequest> _requests = new(StringComparer.Ordinal);

    public Task<SupportRequestWriteResult> CreateOrGetAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_requests.TryAdd(request.Id, request))
            return Task.FromResult(new SupportRequestWriteResult(SupportRequestWriteStatus.Created, request));

        if (!_requests.TryGetValue(request.Id, out var existing))
            return Task.FromResult(new SupportRequestWriteResult(SupportRequestWriteStatus.Conflict, request));

        var sameSubmission =
            string.Equals(existing.ReporterId, request.ReporterId, StringComparison.Ordinal) &&
            string.Equals(existing.ReporterRole, request.ReporterRole, StringComparison.Ordinal) &&
            string.Equals(existing.Subject, request.Subject, StringComparison.Ordinal) &&
            string.Equals(existing.Description, request.Description, StringComparison.Ordinal);

        return Task.FromResult(new SupportRequestWriteResult(
            sameSubmission ? SupportRequestWriteStatus.Replayed : SupportRequestWriteStatus.Conflict,
            sameSubmission ? existing : request));
    }
}
