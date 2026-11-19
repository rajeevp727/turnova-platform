using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed partial class RideApiIntegrationTests
{
    [Fact]
    public async Task LegacyOfferRejectionRetryAfterRotationInterruptionDoesNotRotateTwice()
    {
        const string riderId = "legacy-reject-crash-rider";
        const string firstDriverId = "legacy-reject-crash-first";
        const string secondDriverId = "legacy-reject-crash-second";
        var queueStore = new CrashAfterFirstRotationQueueStore(new InMemoryQueueStore());
        var advancer = new RecordingRideOfferAdvancer();

        using var factory = new TurnOvaApiFactory(
            legacyFifoRiderRequestsEnabled: true,
            rideOfferAdvancer: advancer,
            queueStore: queueStore);
        using var rider = factory.CreateClient("Rider", riderId);
        using var firstDriver = factory.CreateClient("Driver", firstDriverId);
        using var secondDriver = factory.CreateClient("Driver", secondDriverId);

        await EnqueueLegacyDriverAsync(factory, firstDriverId);
        await EnqueueLegacyDriverAsync(factory, secondDriverId);
        var ride = await CreateRideAsync(rider, "legacy-reject-crash-request");
        var offerStore = factory.Services.GetRequiredService<IRideOfferStore>();
        var initialOffer = await offerStore.GetByRideAsync(ride.RideId, CancellationToken.None);

        Assert.NotNull(initialOffer);
        Assert.Equal(firstDriverId, initialOffer!.DriverId);
        Assert.Equal(RideOfferStatuses.Pending, initialOffer.Status);

        // Simulate process interruption after the queue rotation is durable but before
        // the offer aggregate can be marked Rejected.
        using var interrupted = await firstDriver.PostAsync($"/api/v1/rides/{ride.RideId}/reject", null);
        Assert.Equal(HttpStatusCode.InternalServerError, interrupted.StatusCode);
        var errorBody = await interrupted.Content.ReadAsStringAsync();
        Assert.DoesNotContain("simulated interruption after durable queue rotation", errorBody,
            StringComparison.OrdinalIgnoreCase);

        var afterInterruptionQueue = await queueStore.GetEligibleAsync(
            "hyderabad-central", "cab", CancellationToken.None);
        Assert.Equal(new[] { secondDriverId, firstDriverId },
            afterInterruptionQueue.Select(entry => entry.DriverId));
        var afterInterruptionOffer = await offerStore.GetByRideAsync(ride.RideId, CancellationToken.None);
        Assert.Equal(RideOfferStatuses.Pending, afterInterruptionOffer?.Status);
        Assert.Empty(advancer.AdvancedRideIds);
        var activityLedger = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverWorkActivity>>();
        Assert.Empty(activityLedger.Values.Where(item =>
            item.DriverId == firstDriverId && item.ActivityType == "Rejected"));

        // The retry must recognize the same offer ID and must not move the Driver again.
        using var retry = await firstDriver.PostAsync($"/api/v1/rides/{ride.RideId}/reject", null);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal(2, queueStore.RotationCalls);

        var afterRetryQueue = await queueStore.GetEligibleAsync(
            "hyderabad-central", "cab", CancellationToken.None);
        Assert.Equal(new[] { secondDriverId, firstDriverId },
            afterRetryQueue.Select(entry => entry.DriverId));

        var auditEvents = await queueStore.GetAuditEventsAsync(
            "hyderabad-central", "cab", CancellationToken.None);
        var rotation = Assert.Single(auditEvents.Where(item =>
            item.EventType == QueueAuditEventTypes.RotatedToBack &&
            item.OfferId == initialOffer.CurrentOfferId));
        Assert.Equal(firstDriverId, rotation.DriverId);

        var resolvedOffer = await offerStore.GetByRideAsync(ride.RideId, CancellationToken.None);
        Assert.Equal(RideOfferStatuses.Rejected, resolvedOffer?.Status);
        Assert.Equal(RideOfferStatuses.Rejected,
            Assert.Single(resolvedOffer!.Attempts, item => item.OfferId == initialOffer.CurrentOfferId).Status);
        Assert.Equal(new[] { ride.RideId }, advancer.AdvancedRideIds.ToArray());
        Assert.Single(activityLedger.Values.Where(item =>
            item.DriverId == firstDriverId && item.ActivityType == "Rejected"));
    }

    private sealed class CrashAfterFirstRotationQueueStore(IQueueStore inner) : IQueueStore
    {
        private int _crashAfterNextSuccessfulRotation = 1;
        private int _rotationCalls;

        public int RotationCalls => Volatile.Read(ref _rotationCalls);

        public Task<IReadOnlyList<DriverQueueEntry>> GetEligibleAsync(
            string zoneId, string serviceClass, CancellationToken cancellationToken) =>
            inner.GetEligibleAsync(zoneId, serviceClass, cancellationToken);

        public Task AddAsync(DriverQueueEntry entry, CancellationToken cancellationToken) =>
            inner.AddAsync(entry, cancellationToken);

        public Task<bool> RemoveAsync(
            string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken) =>
            inner.RemoveAsync(driverId, zoneId, serviceClass, cancellationToken);

        public Task<bool> MoveToBackAsync(
            string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken) =>
            inner.MoveToBackAsync(driverId, zoneId, serviceClass, cancellationToken);

        public async Task<bool> MoveToBackOnceAsync(
            string driverId, string zoneId, string serviceClass, string offerId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _rotationCalls);
            var moved = await inner.MoveToBackOnceAsync(
                driverId, zoneId, serviceClass, offerId, cancellationToken);
            if (moved && Interlocked.Exchange(ref _crashAfterNextSuccessfulRotation, 0) == 1)
                throw new InvalidOperationException("simulated interruption after durable queue rotation");
            return moved;
        }

        public Task RecordDispatchExceptionAsync(
            string actorId, string zoneId, string serviceClass, string traceId, string reasonCategory,
            CancellationToken cancellationToken) =>
            inner.RecordDispatchExceptionAsync(
                actorId, zoneId, serviceClass, traceId, reasonCategory, cancellationToken);

        public Task<IReadOnlyList<QueueAuditEvent>> GetAuditEventsAsync(
            string zoneId, string serviceClass, CancellationToken cancellationToken) =>
            inner.GetAuditEventsAsync(zoneId, serviceClass, cancellationToken);
    }
}
