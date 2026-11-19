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
    public async Task SelectedBookingIdempotencyReplayCannotReturnAnotherRidersRide()
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
                originalRecord.RequestHash, originalRecord.CreatedAtUtc)));

        using var replayRequest = CreateSelectedBookingRequest(
            replayKey, driverId, unionId, "KPHB", "HITEC City", []);
        using var replayResponse = await otherRider.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.Conflict, replayResponse.StatusCode);
        var body = await replayResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ownedRideId, body, StringComparison.Ordinal);

        var reservations = factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>();
        Assert.Null(await reservations.GetActiveAsync(otherRiderId, CancellationToken.None));
    }
}
