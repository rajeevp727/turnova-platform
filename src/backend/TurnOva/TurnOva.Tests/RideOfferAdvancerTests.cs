using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Moq;
using TurnOva.API;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class RideOfferAdvancerTests
{
    private const string RideId = "ride-1";
    private const string DriverId = "driver-1";
    private const string ZoneId = "zone-1";

    [Fact]
    public async Task AdvanceAsyncHonorsPreCancelledTokenBeforeOfferRead()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var offers = new Mock<IRideOfferStore>(MockBehavior.Strict);
        var queues = new Mock<IQueueStore>(MockBehavior.Strict);
        var advancer = CreateAdvancer(offers.Object, queues.Object, null, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            advancer.AdvanceAsync(RideId, cancellation.Token));

        offers.Verify(value => value.GetByRideAsync(RideId, cancellation.Token), Times.Never);
        offers.VerifyNoOtherCalls();
        queues.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdvanceAsyncStopsBeforeQueueReadWhenRideResourceHydrationCancels()
    {
        using var cancellation = new CancellationTokenSource();
        var read = new Mock<ItemResponse<DemoRide>>(MockBehavior.Strict);
        read.SetupGet(value => value.Resource).Returns(() =>
        {
            cancellation.Cancel();
            return CreateRide();
        });
        read.SetupGet(value => value.ETag).Returns("unused-etag");
        var rideContainer = new Mock<Container>(MockBehavior.Strict);
        rideContainer.Setup(value => value.ReadItemAsync<DemoRide>(
                RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .ReturnsAsync(read.Object);
        var offers = CreateOfferStore(cancellation.Token);
        var queues = CreateQueueStore(cancellation.Token);
        var advancer = CreateAdvancer(offers.Object, queues.Object, null, rideContainer.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            advancer.AdvanceAsync(RideId, cancellation.Token));

        read.VerifyGet(value => value.Resource, Times.Once);
        read.VerifyGet(value => value.ETag, Times.Never);
        read.VerifyNoOtherCalls();
        rideContainer.Verify(value => value.ReadItemAsync<DemoRide>(
            RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token), Times.Once);
        rideContainer.VerifyNoOtherCalls();
        queues.Verify(value => value.GetEligibleAsync(ZoneId, "cab", cancellation.Token), Times.Never);
        queues.VerifyNoOtherCalls();
        offers.Verify(value => value.TryReplaceAsync(
            It.IsAny<RideOfferState>(), "offer-etag", cancellation.Token), Times.Never);
    }

    [Fact]
    public async Task AdvanceAsyncStopsBeforeQueueMutationWhenProfileResourceHydrationCancels()
    {
        using var cancellation = new CancellationTokenSource();
        var rideRead = new Mock<ItemResponse<DemoRide>>(MockBehavior.Strict);
        rideRead.SetupGet(value => value.Resource).Returns(CreateRide());
        var rideContainer = new Mock<Container>(MockBehavior.Strict);
        rideContainer.Setup(value => value.ReadItemAsync<DemoRide>(
                RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .ReturnsAsync(rideRead.Object);

        var profileRead = new Mock<ItemResponse<DriverOnboardingProfile>>(MockBehavior.Strict);
        profileRead.SetupGet(value => value.Resource).Returns(() =>
        {
            cancellation.Cancel();
            return CreateProfile();
        });
        profileRead.SetupGet(value => value.ETag).Returns("unused-etag");
        var driverContainer = new Mock<Container>(MockBehavior.Strict);
        driverContainer.Setup(value => value.ReadItemAsync<DriverOnboardingProfile>(
                DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .ReturnsAsync(profileRead.Object);

        var offers = CreateOfferStore(cancellation.Token);
        var queues = CreateQueueStore(cancellation.Token);
        var advancer = CreateAdvancer(offers.Object, queues.Object, driverContainer.Object, rideContainer.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            advancer.AdvanceAsync(RideId, cancellation.Token));

        rideRead.VerifyGet(value => value.Resource, Times.Once);
        rideRead.VerifyNoOtherCalls();
        profileRead.VerifyGet(value => value.Resource, Times.Once);
        profileRead.VerifyGet(value => value.ETag, Times.Never);
        profileRead.VerifyNoOtherCalls();
        queues.Verify(value => value.GetEligibleAsync(ZoneId, "cab", cancellation.Token), Times.Once);
        queues.Verify(value => value.RemoveAsync(DriverId, ZoneId, "cab", cancellation.Token), Times.Never);
        queues.VerifyNoOtherCalls();
        offers.Verify(value => value.TryReplaceAsync(
            It.IsAny<RideOfferState>(), "offer-etag", cancellation.Token), Times.Never);
    }

    [Fact]
    public async Task AdvanceAsyncCancellationTakesPrecedenceOverRideReadProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new CosmosException(
            "ride-read-failed-after-cancellation", HttpStatusCode.ServiceUnavailable, 0,
            "ride-advancer-test", 0);
        var rideContainer = new Mock<Container>(MockBehavior.Strict);
        rideContainer.Setup(value => value.ReadItemAsync<DemoRide>(
                RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DemoRide>>(failure);
            });
        var offers = CreateOfferStore(cancellation.Token);
        var queues = CreateQueueStore(cancellation.Token);
        var advancer = CreateAdvancer(offers.Object, queues.Object, null, rideContainer.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            advancer.AdvanceAsync(RideId, cancellation.Token));

        queues.Verify(value => value.GetEligibleAsync(ZoneId, "cab", cancellation.Token), Times.Never);
        queues.VerifyNoOtherCalls();
        offers.Verify(value => value.TryReplaceAsync(
            It.IsAny<RideOfferState>(), "offer-etag", cancellation.Token), Times.Never);
    }

    [Fact]
    public async Task AdvanceAsyncCancellationTakesPrecedenceOverProfileReadProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var rideRead = new Mock<ItemResponse<DemoRide>>(MockBehavior.Strict);
        rideRead.SetupGet(value => value.Resource).Returns(CreateRide());
        var rideContainer = new Mock<Container>(MockBehavior.Strict);
        rideContainer.Setup(value => value.ReadItemAsync<DemoRide>(
                RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .ReturnsAsync(rideRead.Object);

        var failure = new CosmosException(
            "profile-read-failed-after-cancellation", HttpStatusCode.ServiceUnavailable, 0,
            "ride-advancer-test", 0);
        var driverContainer = new Mock<Container>(MockBehavior.Strict);
        driverContainer.Setup(value => value.ReadItemAsync<DriverOnboardingProfile>(
                DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverOnboardingProfile>>(failure);
            });
        var offers = CreateOfferStore(cancellation.Token);
        var queues = CreateQueueStore(cancellation.Token);
        var advancer = CreateAdvancer(offers.Object, queues.Object, driverContainer.Object, rideContainer.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            advancer.AdvanceAsync(RideId, cancellation.Token));

        queues.Verify(value => value.GetEligibleAsync(ZoneId, "cab", cancellation.Token), Times.Once);
        queues.Verify(value => value.RemoveAsync(DriverId, ZoneId, "cab", cancellation.Token), Times.Never);
        queues.VerifyNoOtherCalls();
        offers.Verify(value => value.TryReplaceAsync(
            It.IsAny<RideOfferState>(), "offer-etag", cancellation.Token), Times.Never);
    }

    [Fact]
    public async Task AdvanceAsyncFailsClosedWhenRideIdentityDoesNotMatchRequestedRide()
    {
        using var cancellation = new CancellationTokenSource();
        var read = new Mock<ItemResponse<DemoRide>>(MockBehavior.Strict);
        read.SetupGet(value => value.Resource).Returns(CreateRide("ride-other"));
        var rideContainer = new Mock<Container>(MockBehavior.Strict);
        rideContainer.Setup(value => value.ReadItemAsync<DemoRide>(
                RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .ReturnsAsync(read.Object);
        var offers = CreateOfferStore(cancellation.Token);
        var queues = CreateQueueStore(cancellation.Token);
        var advancer = CreateAdvancer(offers.Object, queues.Object, null, rideContainer.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            advancer.AdvanceAsync(RideId, cancellation.Token));

        queues.Verify(value => value.GetEligibleAsync(ZoneId, "cab", cancellation.Token), Times.Never);
        queues.VerifyNoOtherCalls();
        offers.Verify(value => value.TryReplaceAsync(
            It.IsAny<RideOfferState>(), "offer-etag", cancellation.Token), Times.Never);
    }

    [Fact]
    public async Task AdvanceAsyncFailsClosedWhenDriverProfileIdentityDoesNotMatchRequestedDriver()
    {
        using var cancellation = new CancellationTokenSource();
        var rideRead = new Mock<ItemResponse<DemoRide>>(MockBehavior.Strict);
        rideRead.SetupGet(value => value.Resource).Returns(CreateRide());
        var rideContainer = new Mock<Container>(MockBehavior.Strict);
        rideContainer.Setup(value => value.ReadItemAsync<DemoRide>(
                RideId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .ReturnsAsync(rideRead.Object);

        var profileRead = new Mock<ItemResponse<DriverOnboardingProfile>>(MockBehavior.Strict);
        profileRead.SetupGet(value => value.Resource).Returns(CreateProfile("driver-other"));
        var driverContainer = new Mock<Container>(MockBehavior.Strict);
        driverContainer.Setup(value => value.ReadItemAsync<DriverOnboardingProfile>(
                DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .ReturnsAsync(profileRead.Object);
        var offers = CreateOfferStore(cancellation.Token);
        var queues = CreateQueueStore(cancellation.Token);
        var advancer = CreateAdvancer(offers.Object, queues.Object, driverContainer.Object, rideContainer.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            advancer.AdvanceAsync(RideId, cancellation.Token));

        queues.Verify(value => value.GetEligibleAsync(ZoneId, "cab", cancellation.Token), Times.Once);
        queues.Verify(value => value.RemoveAsync(DriverId, ZoneId, "cab", cancellation.Token), Times.Never);
        queues.VerifyNoOtherCalls();
        offers.Verify(value => value.TryReplaceAsync(
            It.IsAny<RideOfferState>(), "offer-etag", cancellation.Token), Times.Never);
    }

    private static Mock<IRideOfferStore> CreateOfferStore(CancellationToken cancellationToken)
    {
        var offers = new Mock<IRideOfferStore>(MockBehavior.Strict);
        offers.Setup(value => value.GetByRideAsync(RideId, cancellationToken))
            .ReturnsAsync(CreateResolvedOffer());
        offers.Setup(value => value.TryReplaceAsync(
                It.IsAny<RideOfferState>(), "offer-etag", cancellationToken))
            .ReturnsAsync(true);
        return offers;
    }

    private static Mock<IQueueStore> CreateQueueStore(CancellationToken cancellationToken)
    {
        var queues = new Mock<IQueueStore>(MockBehavior.Strict);
        queues.Setup(value => value.GetEligibleAsync(ZoneId, "cab", cancellationToken))
            .ReturnsAsync(new[] { new DriverQueueEntry(DriverId, ZoneId, "cab", DateTimeOffset.UtcNow, 1, true) });
        queues.Setup(value => value.RemoveAsync(DriverId, ZoneId, "cab", cancellationToken))
            .ReturnsAsync(true);
        return queues;
    }

    private static RideOfferAdvancer CreateAdvancer(
        IRideOfferStore offers,
        IQueueStore queues,
        Container? driverContainer,
        Container? rideContainer) =>
        new(
            offers,
            queues,
            new ConcurrentDictionary<string, DriverOnboardingProfile>(),
            new ConcurrentDictionary<string, DemoRide>(),
            driverContainer,
            rideContainer,
            TimeProvider.System,
            Options.Create(new DispatchOfferOptions()));

    private static RideOfferState CreateResolvedOffer()
    {
        var now = DateTimeOffset.UtcNow;
        return new RideOfferState(
            RideId, ZoneId, "cab", "offer-current", "driver-old",
            now.AddMinutes(-1), now.AddMinutes(1), RideOfferStatuses.Rejected,
            [new RideOfferAttempt("offer-current", "driver-old", now.AddMinutes(-1), now, RideOfferStatuses.Rejected, now)],
            "offer-etag");
    }

    private static DemoRide CreateRide(string rideId = RideId)
    {
        var now = DateTimeOffset.UtcNow;
        return new DemoRide(
            rideId, "KPHB", "HITEC City", 1, "Requested", now, null,
            "rider-1", null, 17.493, 78.399, null, null, null, now,
            false, "cab", ZoneId);
    }

    private static DriverOnboardingProfile CreateProfile(string driverId = DriverId)
    {
        var now = DateTimeOffset.UtcNow;
        return new DriverOnboardingProfile(
            driverId, driverId, ZoneId, "Driver", "+919000000001",
            "Toyota", "Camry", "TS09AB1234", "cab", 4, [], "Approved", now,
            AvailabilityStatus: "Online");
    }
}
