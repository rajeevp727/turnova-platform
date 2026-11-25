using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class DriverQueueEntryValidationTests
{
    [Theory]
    [InlineData(" ", "zone-a", "Cab", false)]
    [InlineData("driver-1", " ", "Cab", false)]
    [InlineData("driver-1", "zone-a", "\t", false)]
    [InlineData("driver-1", "zone-a", "Cab", true)]
    public async Task InMemoryStoreRejectsMalformedJoinBeforeQueueOrAuditMutation(
        string driverId, string zoneId, string serviceClass, bool defaultTimestamp)
    {
        var store = new InMemoryQueueStore();
        var entry = Entry(driverId, zoneId, serviceClass, defaultTimestamp);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddAsync(entry, CancellationToken.None));

        Assert.Empty(await store.GetEligibleAsync("zone-a", "Cab", CancellationToken.None));
        Assert.Empty(await store.GetAuditEventsAsync("zone-a", "Cab", CancellationToken.None));
    }

    [Theory]
    [InlineData(" ", "zone-a", "Cab", false)]
    [InlineData("driver-1", " ", "Cab", false)]
    [InlineData("driver-1", "zone-a", "\t", false)]
    [InlineData("driver-1", "zone-a", "Cab", true)]
    public async Task CosmosStoreRejectsMalformedJoinBeforeCreatingTransactionalBatch(
        string driverId, string zoneId, string serviceClass, bool defaultTimestamp)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var entry = Entry(driverId, zoneId, serviceClass, defaultTimestamp);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new CosmosQueueStore(container.Object).AddAsync(entry, CancellationToken.None));

        Assert.Equal("entry", exception.ParamName);
        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(" ", "zone-a", "Cab", false)]
    [InlineData("driver-1", " ", "Cab", false)]
    [InlineData("driver-1", "zone-a", " ", false)]
    [InlineData("driver-1", "zone-a", "Cab", true)]
    public void DomainQueueRejectsMalformedJoinWithoutAddingAnEntry(
        string driverId, string zoneId, string serviceClass, bool defaultTimestamp)
    {
        var queue = new FifoQueue();
        var entry = Entry(driverId, zoneId, serviceClass, defaultTimestamp);

        Assert.Throws<ArgumentException>(() => queue.Enqueue(entry));

        Assert.Empty(queue.Entries);
    }

    [Fact]
    public async Task InMemoryStoreStillAcceptsNonCabLegacyQueueEntries()
    {
        var store = new InMemoryQueueStore();
        var entry = Entry("legacy-driver", "legacy-zone", "Auto", defaultTimestamp: false);

        await store.AddAsync(entry, CancellationToken.None);

        var stored = Assert.Single(await store.GetEligibleAsync(
            "legacy-zone", "Auto", CancellationToken.None));
        Assert.Equal("legacy-driver", stored.DriverId);
        Assert.Equal("Auto", stored.ServiceClass);
        Assert.Single(await store.GetAuditEventsAsync(
            "legacy-zone", "Auto", CancellationToken.None));
    }

    private static DriverQueueEntry Entry(
        string driverId, string zoneId, string serviceClass, bool defaultTimestamp) =>
        new(
            driverId,
            zoneId,
            serviceClass,
            defaultTimestamp ? default : DateTimeOffset.Parse("2026-11-26T00:00:00Z"),
            Sequence: 1,
            IsEligible: true);
}
