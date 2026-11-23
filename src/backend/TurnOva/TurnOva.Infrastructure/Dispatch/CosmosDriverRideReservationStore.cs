using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;

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
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow)
            return false;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            cancellationToken.ThrowIfCancellationRequested();
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
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var currentReservation = RequireResource(current, driverId);

                // Expiry must be reconciled against the authoritative ride state by the API.
                // Never steal/delete a lease here: doing so could leave an active Negotiating
                // ride without its exclusive driver reservation.
                if (currentReservation.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
                    return false;

                return currentReservation.RideId == rideId;
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
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var currentReservation = RequireResource(current, driverId);
            if (currentReservation.RideId != rideId)
                return false;
            if (currentReservation.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
                return false;
            if (currentReservation.ExpiresAtUtc is null)
                return true;

            cancellationToken.ThrowIfCancellationRequested();
            if (!CosmosEtagPolicy.HasObservedEtag(current.ETag))
                return false;

            var updated = currentReservation with { ExpiresAtUtc = null };
            var auditEvent = DriverRideReservationAuditEvent.Create(
                updated, DriverRideReservationAuditEventTypes.MadePermanent, auditContext,
                currentReservation.ExpiresAtUtc, expiresAtUtc: null);
            using var batch = await container.CreateTransactionalBatch(new PartitionKey(driverId))
                .ReplaceItem(driverId, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
                .CreateItem(auditEvent)
                .ExecuteAsync(cancellationToken);

            if (batch.IsSuccessStatusCode)
                return true;
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReservationWriteConflict(batch))
                return false;
            throw BatchFailure("promote", batch.StatusCode);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow)
            return false;

        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var currentReservation = RequireResource(current, driverId);
            if (currentReservation.RideId != rideId)
                return false;
            if (currentReservation.ExpiresAtUtc == expiresAtUtc)
                return true;

            cancellationToken.ThrowIfCancellationRequested();
            if (!CosmosEtagPolicy.HasObservedEtag(current.ETag))
                return false;

            var updated = currentReservation with { ExpiresAtUtc = expiresAtUtc };
            var auditEvent = DriverRideReservationAuditEvent.Create(
                updated, DriverRideReservationAuditEventTypes.ExpiryChanged, auditContext,
                currentReservation.ExpiresAtUtc, expiresAtUtc);
            using var batch = await container.CreateTransactionalBatch(new PartitionKey(driverId))
                .ReplaceItem(driverId, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
                .CreateItem(auditEvent)
                .ExecuteAsync(cancellationToken);

            if (batch.IsSuccessStatusCode)
                return true;
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReservationWriteConflict(batch))
                return false;
            throw BatchFailure("change expiry", batch.StatusCode);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    public async Task<DriverRideReservation?> GetActiveAsync(
        string driverId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Return expired leases too. The API must expire/reconcile the linked ride
            // before releasing the reservation; the store cannot infer ride lifecycle.
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return RequireResource(current, driverId);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            using var iterator = container.GetItemQueryIterator<DriverRideReservation>(
                DriverRideReservationQuery.Create(batch));
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!iterator.HasMoreResults) break;

                var page = await iterator.ReadNextAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var reservation in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    reservations.Add(RequireQueryItem(reservation, batch));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var current = await container.ReadItemAsync<DriverRideReservation>(
                driverId, new PartitionKey(driverId), cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var currentReservation = RequireResource(current, driverId);
            if (currentReservation.RideId != rideId)
                return false;

            cancellationToken.ThrowIfCancellationRequested();
            if (!CosmosEtagPolicy.HasObservedEtag(current.ETag))
                return false;

            var auditEvent = DriverRideReservationAuditEvent.Create(
                currentReservation, DriverRideReservationAuditEventTypes.Released, auditContext,
                currentReservation.ExpiresAtUtc, expiresAtUtc: null);
            using var batch = await container.CreateTransactionalBatch(new PartitionKey(driverId))
                .DeleteItem(driverId, new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
                .CreateItem(auditEvent)
                .ExecuteAsync(cancellationToken);

            if (batch.IsSuccessStatusCode)
                return true;
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReservationWriteConflict(batch))
                return false;
            throw BatchFailure("release", batch.StatusCode);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static DriverRideReservation RequireQueryItem(
        DriverRideReservation? reservation,
        IReadOnlyCollection<string> expectedDriverIds)
    {
        if (reservation is null)
            throw new InvalidOperationException("Cosmos returned an empty driver reservation query item.");

        if (string.IsNullOrWhiteSpace(reservation.DriverId) ||
            string.IsNullOrWhiteSpace(reservation.RideId) ||
            !expectedDriverIds.Contains(reservation.DriverId, StringComparer.Ordinal))
            throw new InvalidOperationException("Cosmos returned a malformed driver reservation query item.");

        return reservation;
    }

    private static DriverRideReservation RequireResource(
        ItemResponse<DriverRideReservation> response,
        string expectedDriverId)
    {
        var reservation = response.Resource ?? throw new InvalidOperationException(
            "Cosmos returned an empty driver reservation resource.");

        if (string.IsNullOrWhiteSpace(expectedDriverId) ||
            !string.Equals(reservation.DriverId, expectedDriverId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(reservation.RideId))
            throw new InvalidOperationException("Cosmos returned a malformed driver reservation resource.");

        return reservation;
    }

    private static bool IsReservationWriteConflict(TransactionalBatchResponse batch) =>
        batch[0].StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed;

    private static InvalidOperationException BatchFailure(string operation, System.Net.HttpStatusCode statusCode) =>
        new($"Driver reservation {operation} and its audit event could not be committed atomically (Cosmos status {(int)statusCode}).");
}
