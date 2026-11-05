using TurnOva.Domain.Dispatch;

namespace TurnOva.Application.Dispatch;

public interface IQueueStore
{
    Task<IReadOnlyList<DriverQueueEntry>> GetEligibleAsync(string zoneId, string serviceClass, CancellationToken cancellationToken);
    Task AddAsync(DriverQueueEntry entry, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken);
    Task<bool> MoveToBackAsync(string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken);
}