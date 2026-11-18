using TurnOva.Domain.Support;

namespace TurnOva.Application.Support;

public interface ISupportRequestStore
{
    Task<SupportRequestWriteResult> CreateOrGetAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default);
}
