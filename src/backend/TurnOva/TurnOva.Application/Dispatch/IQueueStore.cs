using TurnOva.Domain.Dispatch;

namespace TurnOva.Application.Dispatch;

public interface IQueueStore
{
    Task<IReadOnlyList<DriverQueueEntry>> GetEligibleAsync(string zoneId, string serviceClass, CancellationToken cancellationToken);
    Task AddAsync(DriverQueueEntry entry, CancellationToken cancellationToken);
}