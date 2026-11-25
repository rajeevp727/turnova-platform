using TurnOva.Domain.Dispatch;

namespace TurnOva.Application.Dispatch;

public sealed class QueueService(IQueueStore store)
{
    public Task<IReadOnlyList<DriverQueueEntry>> GetPositionedQueueAsync(
        string zoneId, string serviceClass, CancellationToken cancellationToken = default) =>
        store.GetEligibleAsync(zoneId, serviceClass, cancellationToken);

    public async Task JoinAsync(DriverQueueEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DriverQueueEntry.ValidateForJoin(entry);

        await store.AddAsync(entry, cancellationToken);
    }

    public Task<bool> MoveToBackAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(driverId) || string.IsNullOrWhiteSpace(zoneId) ||
            string.IsNullOrWhiteSpace(serviceClass))
            throw new ArgumentException("Driver, zone and service class are required.");

        return store.MoveToBackAsync(driverId, zoneId, serviceClass, cancellationToken);
    }

    public Task<bool> LeaveAsync(
        string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(driverId) || string.IsNullOrWhiteSpace(zoneId) ||
            string.IsNullOrWhiteSpace(serviceClass))
            throw new ArgumentException("Driver, zone and service class are required.");

        return store.RemoveAsync(driverId, zoneId, serviceClass, cancellationToken);
    }

    public Task RecordDispatchExceptionAsync(
        string actorId,
        string zoneId,
        string serviceClass,
        string traceId,
        string reasonCategory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DispatchExceptionAuditInput.Validate(actorId, zoneId, serviceClass, traceId, reasonCategory);

        return store.RecordDispatchExceptionAsync(
            actorId, zoneId, serviceClass, traceId, reasonCategory, cancellationToken);
    }
}
