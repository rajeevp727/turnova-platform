using System.Collections.Concurrent;
using System.Collections.Generic;

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

        // ICollection.Remove on ConcurrentDictionary compares both key and value
        // atomically, unlike TryGetValue followed by TryRemove(key, out _).
        return ((ICollection<KeyValuePair<string, string>>)reservations)
            .Remove(new KeyValuePair<string, string>(key, expectedRideId));
    }
}
