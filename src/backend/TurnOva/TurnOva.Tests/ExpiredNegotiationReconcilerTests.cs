using TurnOva.API;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class ExpiredNegotiationReconcilerTests
{
    [Fact]
    public async Task EtagConflictRereadsLatestTerminalRideBeforeReleasingMatchingReservation()
    {
        var now = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        var current = CreateRide("Negotiating", now, now.AddMinutes(-1));
        var latest = current with { Status = RideLifecycle.Expired, NegotiationExpiresAtUtc = null };
        var releasedRideIds = new List<string>();

        var result = await ExpiredNegotiationReconciler.ReconcileAsync(
            current,
            now,
            (_, _) => Task.FromResult<DemoRide?>(null),
            (rideId, _) => Task.FromResult<DemoRide?>(latest),
            (ride, _) =>
            {
                releasedRideIds.Add(ride.RideId);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.False(result.Transitioned);
        Assert.Equal(latest, result.Ride);
        Assert.Equal([current.RideId], releasedRideIds);
    }

    [Fact]
    public async Task EtagConflictWithLatestActiveRideDoesNotReleaseOrUnblockDriver()
    {
        var now = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        var current = CreateRide("AgreementConfirmed", now, now.AddMinutes(-1));
        var latest = current with { NegotiationExpiresAtUtc = now.AddMinutes(2) };
        var releaseCount = 0;

        var result = await ExpiredNegotiationReconciler.ReconcileAsync(
            current,
            now,
            (_, _) => Task.FromResult<DemoRide?>(null),
            (rideId, _) => Task.FromResult<DemoRide?>(latest),
            (_, _) =>
            {
                releaseCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.False(result.Transitioned);
        Assert.Equal(latest, result.Ride);
        Assert.Equal(0, releaseCount);
    }

    [Fact]
    public async Task UnexpiredNegotiationDoesNotAttemptTransitionOrRelease()
    {
        var now = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        var current = CreateRide("Negotiating", now, now.AddMinutes(1));
        var transitionCount = 0;
        var readCount = 0;
        var releaseCount = 0;

        var result = await ExpiredNegotiationReconciler.ReconcileAsync(
            current,
            now,
            (_, _) =>
            {
                transitionCount++;
                return Task.FromResult<DemoRide?>(null);
            },
            (_, _) =>
            {
                readCount++;
                return Task.FromResult<DemoRide?>(current);
            },
            (_, _) =>
            {
                releaseCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(result.Attempted);
        Assert.False(result.Transitioned);
        Assert.Equal(current, result.Ride);
        Assert.Equal(0, transitionCount);
        Assert.Equal(0, readCount);
        Assert.Equal(0, releaseCount);
    }

    [Fact]
    public async Task SuccessfulExpiryTransitionReleasesOnlyTheTransitionedTerminalRide()
    {
        var now = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        var current = CreateRide("Negotiating", now, now.AddMinutes(-1));
        var expired = current with { Status = RideLifecycle.Expired, NegotiationExpiresAtUtc = null };
        var releasedRideIds = new List<string>();
        var readCount = 0;

        var result = await ExpiredNegotiationReconciler.ReconcileAsync(
            current,
            now,
            (_, _) => Task.FromResult<DemoRide?>(expired),
            (_, _) =>
            {
                readCount++;
                return Task.FromResult<DemoRide?>(null);
            },
            (ride, _) =>
            {
                releasedRideIds.Add(ride.RideId);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.True(result.Transitioned);
        Assert.Equal(expired, result.Ride);
        Assert.Equal([current.RideId], releasedRideIds);
        Assert.Equal(0, readCount);
    }

    private static DemoRide CreateRide(string status, DateTimeOffset now, DateTimeOffset? expiresAtUtc) =>
        new(
            "etag-conflict-ride", "KPHB", "HITEC City", 1, status, now.AddMinutes(-5), null,
            "etag-conflict-rider", "etag-conflict-driver", 17.493, 78.399, null, null,
            null, now, false, "cab", "hyderabad-central",
            UnionId: "etag-conflict-union",
            NegotiationExpiresAtUtc: expiresAtUtc);
}
