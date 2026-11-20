using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TurnOva.API;
using TurnOva.Application.Dispatch;
using Xunit;

namespace TurnOva.Tests;

public sealed partial class RideApiIntegrationTests
{
    [Fact]
    public async Task StaleSelectedBookingIdempotencyReplayCannotRecoverAnotherRidersRide()
    {
        const string ownerId = "selected-idempotency-owner";
        const string otherRiderId = "selected-idempotency-other";
        const string driverId = "selected-idempotency-driver";
        const string unionId = "selected-idempotency-union";
        const string originalKey = "selected-idempotency-owner-key";
        const string replayKey = "selected-idempotency-other-key";

        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var owner = factory.CreateClient("Rider", ownerId);
        using var otherRider = factory.CreateClient("Rider", otherRiderId);
        using var driver = factory.CreateClient("Driver", driverId);

        var ownedRideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, owner, driver, driverId, unionId, originalKey, 450m);

        var bookingIdempotency =
            factory.Services.GetRequiredService<ConcurrentDictionary<string, RideIdempotencyRecord>>();
        var originalKeyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalKey)));
        var replayKeyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(replayKey)));
        var originalRecord = bookingIdempotency[$"{ownerId}:{originalKeyId}"];
        Assert.True(bookingIdempotency.TryAdd(
            $"{otherRiderId}:{replayKeyId}",
            new RideIdempotencyRecord(
                replayKeyId, otherRiderId, ownedRideId,
                originalRecord.RequestHash, DateTimeOffset.UtcNow.AddMinutes(-2))));

        using var replayRequest = CreateSelectedBookingRequest(
            replayKey, driverId, unionId, "KPHB", "HITEC City", []);
        using var replayResponse = await otherRider.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.Conflict, replayResponse.StatusCode);
        var body = await replayResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ownedRideId, body, StringComparison.Ordinal);

        var reservations = factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>();
        Assert.Null(await reservations.GetActiveAsync(otherRiderId, CancellationToken.None));
    }
    [Theory]
    [InlineData("id")]
    [InlineData("rider")]
    [InlineData("ride")]
    public async Task SelectedBookingReplayRejectsMismatchedIdempotencyEnvelope(string mismatch)
    {
        const string riderId = "selected-idempotency-envelope-owner";
        const string driverId = "selected-idempotency-envelope-driver";
        const string unionId = "selected-idempotency-envelope-union";
        const string key = "selected-idempotency-envelope-key";

        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, driverId, unionId, key, 475m);

        var records = factory.Services.GetRequiredService<ConcurrentDictionary<string, RideIdempotencyRecord>>();
        var keyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var recordKey = $"{riderId}:{keyId}";
        var original = records[recordKey];
        var corrupted = mismatch switch
        {
            "id" => original with { id = "different-key" },
            "rider" => original with { riderId = "different-rider" },
            "ride" => original with { rideId = string.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, "Unknown idempotency envelope field.")
        };
        Assert.True(records.TryUpdate(recordKey, corrupted, original));

        using var replayRequest = CreateSelectedBookingRequest(key, driverId, unionId, "KPHB", "HITEC City", []);
        using var replayResponse = await rider.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.Conflict, replayResponse.StatusCode);
        var body = await replayResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rideId, body, StringComparison.Ordinal);
        var reservations = factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>();
        Assert.Equal(rideId, (await reservations.GetActiveAsync(riderId, CancellationToken.None))?.RideId);
    }
}
