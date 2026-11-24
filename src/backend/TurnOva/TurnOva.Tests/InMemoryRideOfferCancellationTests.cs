using System.Collections;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class InMemoryRideOfferCancellationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PendingForDriverHonorsCancellationDuringOrAfterEnumeration(bool yieldSecond)
    {
        using var cancellation = new CancellationTokenSource();
        var first = CreateState("ride-a", "driver-1", DateTimeOffset.UtcNow.AddMinutes(1));
        var second = yieldSecond
            ? CreateState("ride-b", "driver-1", DateTimeOffset.UtcNow.AddMinutes(2))
            : null;
        var states = new CancellingRideOfferStates(cancellation, first, second);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            InMemoryRideOfferStoreSnapshots.GetPendingForDriver(states, "driver-1", cancellation.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExpiredSnapshotHonorsCancellationDuringOrAfterEnumeration(bool yieldSecond)
    {
        using var cancellation = new CancellationTokenSource();
        var now = DateTimeOffset.UtcNow;
        var first = CreateState("expired-a", "driver-1", now.AddSeconds(-1));
        var second = yieldSecond
            ? CreateState("expired-b", "driver-2", now.AddSeconds(-2))
            : null;
        var states = new CancellingRideOfferStates(cancellation, first, second);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            InMemoryRideOfferStoreSnapshots.GetExpired(states, now, 10, cancellation.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PendingNotificationSnapshotHonorsCancellationDuringOrAfterEnumeration(bool yieldSecond)
    {
        using var cancellation = new CancellationTokenSource();
        var now = DateTimeOffset.UtcNow;
        var first = CreateState("pending-a", "driver-1", now.AddMinutes(1));
        var second = yieldSecond
            ? CreateState("pending-b", "driver-2", now.AddMinutes(2))
            : null;
        var states = new CancellingRideOfferStates(cancellation, first, second);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            InMemoryRideOfferStoreSnapshots.GetPendingNotifications(states, 10, cancellation.Token));
    }

    private static RideOfferState CreateState(string rideId, string driverId, DateTimeOffset expiresAt)
    {
        var offeredAt = expiresAt.AddMinutes(-1);
        return new RideOfferState(
            rideId,
            "zone-1",
            "Cab",
            $"offer-{rideId}",
            driverId,
            offeredAt,
            expiresAt,
            RideOfferStatuses.Pending,
            [new RideOfferAttempt($"offer-{rideId}", driverId, offeredAt, expiresAt, RideOfferStatuses.Pending)],
            NotificationStatus: "Pending");
    }

    private sealed class CancellingRideOfferStates(
        CancellationTokenSource cancellation,
        RideOfferState first,
        RideOfferState? second) : IEnumerable<RideOfferState>
    {
        public IEnumerator<RideOfferState> GetEnumerator()
        {
            yield return first;
            cancellation.Cancel();
            if (second is not null)
                yield return second;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
