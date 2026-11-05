using TurnOva.Domain.Dispatch;

namespace TurnOva.Application.Dispatch;

public sealed class QueueService(IQueueStore store)
{
    public Task<IReadOnlyList<DriverQueueEntry>> GetPositionedQueueAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken = default) =>
        store.GetEligibleAsync(zoneId, serviceClass, cancellationToken);

    public async Task JoinAsync(DriverQueueEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.DriverId) || string.IsNullOrWhiteSpace(entry.ZoneId) ||
            string.IsNullOrWhiteSpace(entry.ServiceClass))
            throw new ArgumentException("Driver, zone and service class are required.");

        await store.AddAsync(entry, cancellationToken);
    }

    public Task<bool> MoveToBackAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(driverId) || string.IsNullOrWhiteSpace(zoneId) ||
            string.IsNullOrWhiteSpace(serviceClass))
            throw new ArgumentException("Driver, zone and service class are required.");

        return store.MoveToBackAsync(driverId, zoneId, serviceClass, cancellationToken);
    }

    public Task<bool> LeaveAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(driverId) || string.IsNullOrWhiteSpace(zoneId) ||
            string.IsNullOrWhiteSpace(serviceClass))
            throw new ArgumentException("Driver, zone and service class are required.");

        return store.RemoveAsync(driverId, zoneId, serviceClass, cancellationToken);
    }
}