using System.Collections.Concurrent;
namespace TurnOva.API;

public static class InMemoryRideIdempotencyReservations
{
    public static bool TryGetRideId(
        ConcurrentDictionary<string, string> reservations,
        string key,
        out string rideId)
    {
        ArgumentNullException.ThrowIfNull(reservations);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return reservations.TryGetValue(key, out rideId!);
    }

    public static bool TryRemoveExact(
        ConcurrentDictionary<string, string> reservations,
        string key,
        string expectedRideId)
    {
        ArgumentNullException.ThrowIfNull(reservations);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRideId);

        // The .NET 10 key/value overload performs an atomic compare-and-remove;
        // a stale cleanup cannot remove a replacement reservation for the same key.
        return reservations.TryRemove(new KeyValuePair<string, string>(key, expectedRideId));
    }
    public static bool TryRemoveExact<T>(
        ConcurrentDictionary<string, T> reservations,
        string key,
        T expectedValue)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(reservations);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(expectedValue);

        return reservations.TryRemove(new KeyValuePair<string, T>(key, expectedValue));
    }
}
(
        ConcurrentDictionary<string, T> reservations,
        string key,
        T expectedValue)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(reservations);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(expectedValue);

        return reservations.TryRemove(new KeyValuePair<string, T>(key, expectedValue));
    }
