using Microsoft.Azure.Cosmos;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class CosmosRiderActiveBookingReservationStore(Container container)
    : IRiderActiveBookingReservationStore
{
    public async Task<RiderBookingReservationAcquireResult> TryAcquireAsync(
        RiderActiveBookingReservation reservation,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        if (reservation.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
            return RiderBookingReservationAcquireResult.Conflict;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var audit = RiderActiveBookingReservationAuditEvent.Create(
                reservation, RiderActiveBookingReservationAuditEventTypes.Acquired,
                auditContext, previousExpiresAtUtc: null, reservation.ExpiresAtUtc);
            using var batch = await container.CreateTransactionalBatch(new PartitionKey(reservation.RiderId))
                .CreateItem(reservation)
                .CreateItem(audit)
                .ExecuteAsync(cancellationToken);

            if (batch.IsSuccessStatusCode)
                return RiderBookingReservationAcquireResult.Acquired;

            if (batch[0].StatusCode != System.Net.HttpStatusCode.Conflict)
                throw BatchFailure("acquire", batch.StatusCode);

            RiderActiveBookingReservation? current;
            try
            {
                current = (await container.ReadItemAsync<RiderActiveBookingReservation>(
                    RiderActiveBookingReservation.DocumentId,
                    new PartitionKey(reservation.RiderId),
                    cancellationToken: cancellationToken)).Resource;
            }
            catch (CosmosException missing) when (missing.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                continue;
            }

            // An expired record is never stolen here. The API first reconciles its ride.
            if (current.RideId == reservation.RideId &&
                current.RequestHash == reservation.RequestHash &&
                (current.ExpiresAtUtc is null || current.ExpiresAtUtc > DateTimeOffset.UtcNow))
                return RiderBookingReservationAcquireResult.AlreadyOwned;

            return RiderBookingReservationAcquireResult.Conflict;
        }

        return RiderBookingReservationAcquireResult.Conflict;
    }

    public async Task<RiderActiveBookingReservation?> GetActiveAsync(
        string riderId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await container.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId,
                new PartitionKey(riderId),
                cancellationToken: cancellationToken)).Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> TryMakePermanentAsync(
        string riderId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ItemResponse<RiderActiveBookingReservation> current;
            try
            {
                current = await container.ReadItemAsync<RiderActiveBookingReservation>(
                    RiderActiveBookingReservation.DocumentId,
                    new PartitionKey(riderId),
                    cancellationToken: cancellationToken);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            if (current.Resource.RideId != rideId)
                return false;
            if (current.Resource.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
                return false;
            if (current.Resource.ExpiresAtUtc is null)
                return true;

            var updated = current.Resource with { ExpiresAtUtc = null };
            var audit = RiderActiveBookingReservationAuditEvent.Create(
                updated, RiderActiveBookingReservationAuditEventTypes.MadePermanent,
                auditContext, current.Resource.ExpiresAtUtc, expiresAtUtc: null);
            using var batch = await container.CreateTransactionalBatch(new PartitionKey(riderId))
                .ReplaceItem(
                    RiderActiveBookingReservation.DocumentId,
                    updated,
                    new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
                .CreateItem(audit)
                .ExecuteAsync(cancellationToken);

            if (batch.IsSuccessStatusCode)
                return true;
            if (IsReservationWriteConflict(batch))
                continue;
            throw BatchFailure("promote", batch.StatusCode);
        }

        var latest = await GetActiveAsync(riderId, cancellationToken);
        return latest?.RideId == rideId && latest.ExpiresAtUtc is null;
    }

    public async Task<bool> TryReleaseAsync(
        string riderId,
        string rideId,
        CancellationToken cancellationToken = default,
        DriverRideReservationAuditContext? auditContext = null)
    {
        ItemResponse<RiderActiveBookingReservation> current;
        try
        {
            current = await container.ReadItemAsync<RiderActiveBookingReservation>(
                RiderActiveBookingReservation.DocumentId,
                new PartitionKey(riderId),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        if (current.Resource.RideId != rideId)
            return false;

        var audit = RiderActiveBookingReservationAuditEvent.Create(
            current.Resource, RiderActiveBookingReservationAuditEventTypes.Released,
            auditContext, current.Resource.ExpiresAtUtc, expiresAtUtc: null);
        using var batch = await container.CreateTransactionalBatch(new PartitionKey(riderId))
            .DeleteItem(
                RiderActiveBookingReservation.DocumentId,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
            .CreateItem(audit)
            .ExecuteAsync(cancellationToken);

        if (batch.IsSuccessStatusCode)
            return true;
        if (IsReservationWriteConflict(batch))
            return false;
        throw BatchFailure("release", batch.StatusCode);
    }

    private static bool IsReservationWriteConflict(TransactionalBatchResponse batch) =>
        batch[0].StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed;

    private static InvalidOperationException BatchFailure(string operation, System.Net.HttpStatusCode statusCode) =>
        new($"Rider booking reservation {operation} and its audit event could not be committed atomically (Cosmos status {(int)statusCode}).");
}
