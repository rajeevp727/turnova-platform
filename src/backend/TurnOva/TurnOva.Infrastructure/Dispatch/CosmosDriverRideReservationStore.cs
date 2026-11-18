using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class CosmosDriverRideReservationStore(Container container) : IDriverRideReservationStore
{
    public Task<bool> TryAcquireAsync(
        string driverId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null) =>
        TryAcquireCoreAsync(driverId, rideId, null, cancellationToken, auditContext);

    public Task<bool> TryAcquireTemporaryAsync(
        string driverId,
        string rideId,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null) =>
        TryAcquireCoreAsync(driverId, rideId, expiresAtUtc, cancellationToken, auditContext);

    private async Task<bool> TryAcquireCoreAsync(
        string driverId,
        string rideId,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken,
        DriverRideReservationAuditContext? auditContext)
    {
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow)
            return false;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var reservation = new DriverRideReservation(
                driverId, rideId, DateTimeOffset.UtcNow, expiresAtUtc);
            var auditEvent = DriverRideReservationAuditEvent.Create(
                reservation, DriverRideReservationAuditEventTypes.Acquired, auditContext,
                previousExpiresAtUtc: null, expiresAtUtc);

            // Reservation and audit fact share the existing /driverId partition. A batch makes
            // the lock visible only when its append-only audit event is committed as well.
            using var batch = await container.CreateTransactionalBatch(new PartitionKey(driverId))
                .CreateItem(reservation)
                .CreateItem(auditEvent)
                .ExecuteAsync(cancellationToken);

            if (batch.IsSuccessStatusCode)
                return true;

            if (batch[0].StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                ItemResponse<DriverRideReservation> current;
                try
                {
                    current = await container.ReadItemAsync<DriverRideReservation>(
                        driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
                }
                catch (CosmosException missing) when (missing.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    continue;
                }

                // Expiry must be reconciled against the authoritative ride state by the API.
                // Never steal/delete a lease here: doing so could leave an active Negotiating
                // ride without its exclusive driver reservation.
                if (current.Resource.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
                    return false;

                return current.Resource.RideId == rideId;
            }

            throw BatchFailure("acquire", batch.StatusCode);
        }

        return false;
    }

    public async Task<bool> TryMakePermanentAsync(
        string driverId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            if (current.Resource.RideId != rideId)
                return false;
            if (current.Resource.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
                return false;
            if (current.Resource.ExpiresAtUtc is null)
                return true;

            var updated = current.Resource with { ExpiresAtUtc = null };
            var auditEvent = DriverRideReservationAuditEvent.Create(
                updated, DriverRideReservationAuditEventTypes.MadePermanent, auditContext,
                current.Resource.ExpiresAtUtc, expiresAtUtc: null);
            using var batch = await container.CreateTransactionalBatch(new PartitionKey(driverId))
                .ReplaceItem(driverId, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
                .CreateItem(auditEvent)
                .ExecuteAsync(cancellationToken);

            if (batch.IsSuccessStatusCode)
                return true;
            if (IsReservationWriteConflict(batch))
                return false;
            throw BatchFailure("promote", batch.StatusCode);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> TrySetExpiryAsync(
        string driverId,
        string rideId,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow)
            return false;

        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            if (current.Resource.RideId != rideId)
                return false;
            if (current.Resource.ExpiresAtUtc == expiresAtUtc)
                return true;

            var updated = current.Resource with { ExpiresAtUtc = expiresAtUtc };
            var auditEvent = DriverRideReservationAuditEvent.Create(
                updated, DriverRideReservationAuditEventTypes.ExpiryChanged, auditContext,
                current.Resource.ExpiresAtUtc, expiresAtUtc);
            using var batch = await container.CreateTransactionalBatch(new PartitionKey(driverId))
                .ReplaceItem(driverId, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
                .CreateItem(auditEvent)
                .ExecuteAsync(cancellationToken);

            if (batch.IsSuccessStatusCode)
                return true;
            if (IsReservationWriteConflict(batch))
                return false;
            throw BatchFailure("change expiry", batch.StatusCode);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<DriverRideReservation?> GetActiveAsync(
        string driverId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Return expired leases too. The API must expire/reconcile the linked ride
            // before releasing the reservation; the store cannot infer ride lifecycle.
            return (await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken)).Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DriverRideReservation>> GetActiveForDriversAsync(
        IReadOnlyCollection<string> driverIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driverIds);
        cancellationToken.ThrowIfCancellationRequested();
        var reservations = new List<DriverRideReservation>();
        foreach (var batch in DriverIdBatching.Chunk(driverIds))
        {
            using var iterator = container.GetItemQueryIterator<DriverRideReservation>(
                DriverRideReservationQuery.Create(batch));
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                reservations.AddRange(page);
            }
        }

        return reservations;
    }

    public async Task<bool> HasActiveAsync(
        string driverId,
        CancellationToken cancellationToken = default) =>
        await GetActiveAsync(driverId, cancellationToken) is not null;

    public async Task<bool> TryReleaseAsync(
        string driverId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            if (current.Resource.RideId != rideId)
                return false;

            var auditEvent = DriverRideReservationAuditEvent.Create(
                current.Resource, DriverRideReservationAuditEventTypes.Released, auditContext,
                current.Resource.ExpiresAtUtc, expiresAtUtc: null);
            using var batch = await container.CreateTransactionalBatch(new PartitionKey(driverId))
                .DeleteItem(driverId, new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
                .CreateItem(auditEvent)
                .ExecuteAsync(cancellationToken);

            if (batch.IsSuccessStatusCode)
                return true;
            if (IsReservationWriteConflict(batch))
                return false;
            throw BatchFailure("release", batch.StatusCode);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static bool IsReservationWriteConflict(TransactionalBatchResponse batch) =>
        batch[0].StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed;

    private static InvalidOperationException BatchFailure(string operation, System.Net.HttpStatusCode statusCode) =>
        new($"Driver reservation {operation} and its audit event could not be committed atomically (Cosmos status {(int)statusCode}).");
}
