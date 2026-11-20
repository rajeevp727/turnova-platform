using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.API;
using Xunit;

namespace TurnOva.Tests;

public sealed class CosmosRideIdempotencyCleanupTests
{
    private const string KeyId = "request-key";
    private const string RiderId = "rider-1";
    private const string RideId = "ride-1";
    private const string RequestHash = "request-hash";

    [Fact]
    public async Task DeleteUsesObservedEtagForTheExactRiderRideAndRequest()
    {
        var record = new RideIdempotencyRecord(KeyId, RiderId, RideId, RequestHash, DateTimeOffset.UtcNow);
        var read = new Mock<ItemResponse<RideIdempotencyRecord>>();
        read.SetupGet(value => value.Resource).Returns(record);
        read.SetupGet(value => value.ETag).Returns("observed-etag");
        var deleted = new Mock<ItemResponse<RideIdempotencyRecord>>();
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        var partitionKey = new PartitionKey(RiderId);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId,
                partitionKey,
                It.IsAny<ItemRequestOptions?>(),
                cancellation.Token))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.DeleteItemAsync<RideIdempotencyRecord>(
                KeyId,
                partitionKey,
                It.Is<ItemRequestOptions?>(options => options?.IfMatchEtag == "observed-etag"),
                cancellation.Token))
            .ReturnsAsync(deleted.Object);

        var result = await CosmosRideIdempotencyCleanup.TryDeleteExactAsync(
            container.Object, KeyId, partitionKey, RiderId, RideId, RequestHash, cancellation.Token);

        Assert.True(result);
        container.Verify(value => value.ReadItemAsync<RideIdempotencyRecord>(
            KeyId, partitionKey, It.IsAny<ItemRequestOptions?>(), cancellation.Token), Times.Once);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            KeyId, partitionKey,
            It.Is<ItemRequestOptions?>(options => options?.IfMatchEtag == "observed-etag"),
            cancellation.Token), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteDoesNotRemoveARecordWithDifferentIdentityOrRequestHash()
    {
        var original = new RideIdempotencyRecord(KeyId, RiderId, RideId, RequestHash, DateTimeOffset.UtcNow);
        var replacements = new[]
        {
            original with { id = "different-key" },
            original with { riderId = "different-rider" },
            original with { rideId = "different-ride" },
            original with { RequestHash = "different-hash" }
        };

        foreach (var replacement in replacements)
        {
            var read = new Mock<ItemResponse<RideIdempotencyRecord>>();
            read.SetupGet(value => value.Resource).Returns(replacement);
            read.SetupGet(value => value.ETag).Returns("observed-etag");
            var container = new Mock<Container>(MockBehavior.Strict);
            container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                    KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(read.Object);

            var result = await CosmosRideIdempotencyCleanup.TryDeleteExactAsync(
                container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, RequestHash, CancellationToken.None);

            Assert.False(result);
            container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            container.Verify(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task DeleteReturnsFalseWhenRecordDisappearsBeforeRead()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("missing", HttpStatusCode.NotFound, 0, "idempotency-cleanup", 0));

        var result = await CosmosRideIdempotencyCleanup.TryDeleteExactAsync(
            container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, RequestHash, CancellationToken.None);

        Assert.False(result);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteReturnsFalseWhenEtagChangesAfterRead()
    {
        var record = new RideIdempotencyRecord(KeyId, RiderId, RideId, RequestHash, DateTimeOffset.UtcNow);
        var read = new Mock<ItemResponse<RideIdempotencyRecord>>();
        read.SetupGet(value => value.Resource).Returns(record);
        read.SetupGet(value => value.ETag).Returns("stale-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.DeleteItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(),
                It.Is<ItemRequestOptions?>(options => options?.IfMatchEtag == "stale-etag"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("etag changed", HttpStatusCode.PreconditionFailed, 0, "idempotency-cleanup", 0));

        var result = await CosmosRideIdempotencyCleanup.TryDeleteExactAsync(
            container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, RequestHash, CancellationToken.None);

        Assert.False(result);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            KeyId, It.IsAny<PartitionKey>(),
            It.Is<ItemRequestOptions?>(options => options?.IfMatchEtag == "stale-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePropagatesNonRaceProviderFailures()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var failure = new CosmosException("unavailable", HttpStatusCode.ServiceUnavailable, 0, "idempotency-cleanup", 0);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var thrown = await Assert.ThrowsAsync<CosmosException>(() =>
            CosmosRideIdempotencyCleanup.TryDeleteExactAsync(
                container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, RequestHash, CancellationToken.None));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task DeleteHonorsPreCancelledTokenBeforeCosmosAccess()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosRideIdempotencyCleanup.TryDeleteExactAsync(
                container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, RequestHash, cancellation.Token));

        container.VerifyNoOtherCalls();
    }
}
