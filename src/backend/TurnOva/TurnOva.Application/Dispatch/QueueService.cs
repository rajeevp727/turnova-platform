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

    public Task RecordDispatchExceptionAsync(
        string actorId,
        string zoneId,
        string serviceClass,
        string traceId,
        string reasonCategory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(zoneId) ||
            string.IsNullOrWhiteSpace(serviceClass) || string.IsNullOrWhiteSpace(traceId) ||
            traceId.Length > 128 || traceId.Any(char.IsControl))
            throw new ArgumentException("A valid actor, queue scope and server trace identifier are required.");

        if (!string.Equals(serviceClass, "cab", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Dispatch exception auditing is limited to the Cab MVP.");

        if (reasonCategory is not QueueAuditReasonCategories.QueueReadFailed and
            not QueueAuditReasonCategories.DriverReservationFailed and
            not QueueAuditReasonCategories.DriverReservationPromotionRecovered)
            throw new ArgumentException("The dispatch exception category is not allow-listed.");

        return store.RecordDispatchExceptionAsync(
            actorId, zoneId, serviceClass, traceId, reasonCategory, cancellationToken);
    }
}
