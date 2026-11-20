using System.Collections.Concurrent;
using TurnOva.API;

namespace TurnOva.Tests;

public sealed class InMemoryRideIdempotencyReservationsTests
{
    [Fact]
    public void TryGetRideIdReturnsFalseWhenReservationWasReleasedAfterTryAddConflict()
    {
        const string key = "rider:request-key";
        var reservations = new ConcurrentDictionary<string, string>();

        Assert.True(reservations.TryAdd(key, "original-ride"));
        Assert.False(reservations.TryAdd(key, "retry-ride"));

        // The original request fails and releases its reservation before the
        // retry reads the value after its failed TryAdd.
        Assert.True(reservations.TryRemove(key, out _));

        Assert.False(InMemoryRideIdempotencyReservations.TryGetRideId(
            reservations, key, out _));
    }

    [Fact]
    public void TryRemoveExactDoesNotDeleteAReplacementReservation()
    {
        const string key = "rider:request-key";
        var reservations = new ConcurrentDictionary<string, string>();

        Assert.True(reservations.TryAdd(key, "original-ride"));
        Assert.True(reservations.TryRemove(key, out _));
        Assert.True(reservations.TryAdd(key, "replacement-ride"));

        Assert.False(InMemoryRideIdempotencyReservations.TryRemoveExact(
            reservations, key, "original-ride"));
        Assert.Equal("replacement-ride", reservations[key]);

        Assert.True(InMemoryRideIdempotencyReservations.TryRemoveExact(
            reservations, key, "replacement-ride"));
        Assert.False(reservations.ContainsKey(key));
    }
    [Fact]
    public void TryRemoveExactDoesNotDeleteAReplacementBookingRecord()
    {
        const string key = "rider:request-key";
        var reservations = new ConcurrentDictionary<string, RideIdempotencyRecord>();
        var now = DateTimeOffset.UtcNow;
        var original = new RideIdempotencyRecord(
            "request-key", "rider", "original-ride", "original-hash", now);
        var replacement = new RideIdempotencyRecord(
            "request-key", "rider", "replacement-ride", "replacement-hash", now.AddSeconds(1));

        Assert.True(reservations.TryAdd(key, replacement));

        Assert.False(InMemoryRideIdempotencyReservations.TryRemoveExact(
            reservations, key, original));
        Assert.Same(replacement, reservations[key]);

        Assert.True(InMemoryRideIdempotencyReservations.TryRemoveExact(
            reservations, key, replacement));
        Assert.Empty(reservations);
    }

}
