using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.API;
using Xunit;

namespace TurnOva.Tests;

public sealed class CosmosLegacyRideIdempotencyCleanupTests
{
    private const string KeyId = "legacy-request-key";
    private const string RiderId = "legacy-rider-1";
    private const string RideId = "RIDE-LEGACY-1";

    [Fact]
    public async Task DeleteUsesObservedEtagForExactLegacyIdentity()
    {
        var partitionKey = new PartitionKey(RiderId);
        var record = new RideIdempotencyRecord(KeyId, RiderId, RideId);
        var read = new Mock<ItemResponse<RideIdempotencyRecord>>();
        read.SetupGet(value => value.Resource).Returns(record);
        read.SetupGet(value => value.ETag).Returns("observed-etag");
        var deleted = new Mock<ItemResponse<RideIdempotencyRecord>>();
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();

        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, partitionKey, It.IsAny<ItemRequestOptions?>(), cancellation.Token))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.DeleteItemAsync<RideIdempotencyRecord>(
                KeyId,
                partitionKey,
                It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "observed-etag"),
                cancellation.Token))
            .ReturnsAsync(deleted.Object);

        var result = await CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
            container.Object, KeyId, partitionKey, RiderId, RideId, cancellation.Token);

        Assert.True(result);
        container.Verify(value => value.ReadItemAsync<RideIdempotencyRecord>(
            KeyId, partitionKey, It.IsAny<ItemRequestOptions?>(), cancellation.Token), Times.Once);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            KeyId,
            partitionKey,
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "observed-etag"),
            cancellation.Token), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteDoesNotAttemptWhenReadHasNoResource()
    {
        var read = new Mock<ItemResponse<RideIdempotencyRecord>>();
        read.SetupGet(value => value.Resource).Returns((RideIdempotencyRecord)null!);
        read.SetupGet(value => value.ETag).Returns("observed-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);

        var result = await CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
            container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, CancellationToken.None);

        Assert.False(result);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteDoesNotAttemptWhenReadHasNoEtag()
    {
        var read = new Mock<ItemResponse<RideIdempotencyRecord>>();
        read.SetupGet(value => value.Resource).Returns(new RideIdempotencyRecord(KeyId, RiderId, RideId));
        read.SetupGet(value => value.ETag).Returns(string.Empty);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);

        var result = await CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
            container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, CancellationToken.None);

        Assert.False(result);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("rider")]
    [InlineData("ride")]
    public async Task DeleteDoesNotRemoveARecordWithDifferentIdentity(string mismatch)
    {
        var original = new RideIdempotencyRecord(KeyId, RiderId, RideId);
        var replacement = mismatch switch
        {
            "id" => original with { id = "replacement-key" },
            "rider" => original with { riderId = "replacement-rider" },
            "ride" => original with { rideId = "replacement-ride" },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, "Unknown identity field.")
        };
        var read = new Mock<ItemResponse<RideIdempotencyRecord>>();
        read.SetupGet(value => value.Resource).Returns(replacement);
        read.SetupGet(value => value.ETag).Returns("observed-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);

        var result = await CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
            container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, CancellationToken.None);

        Assert.False(result);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteReturnsFalseWhenRecordIsMissingBeforeRead()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("missing", HttpStatusCode.NotFound, 0, "legacy-idempotency", 0));

        var result = await CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
            container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, CancellationToken.None);

        Assert.False(result);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    public async Task DeleteReturnsFalseWhenRecordDisappearsOrChangesAfterRead(HttpStatusCode statusCode)
    {
        var read = new Mock<ItemResponse<RideIdempotencyRecord>>();
        read.SetupGet(value => value.Resource).Returns(new RideIdempotencyRecord(KeyId, RiderId, RideId));
        read.SetupGet(value => value.ETag).Returns("stale-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.DeleteItemAsync<RideIdempotencyRecord>(
                KeyId,
                It.IsAny<PartitionKey>(),
                It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "stale-etag"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("lost cleanup race", statusCode, 0, "legacy-idempotency", 0));

        var result = await CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
            container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, CancellationToken.None);

        Assert.False(result);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            KeyId,
            It.IsAny<PartitionKey>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "stale-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePropagatesNonRaceReadProviderFailures()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var failure = new CosmosException("unavailable", HttpStatusCode.ServiceUnavailable, 0, "legacy-idempotency", 0);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var thrown = await Assert.ThrowsAsync<CosmosException>(() =>
            CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
                container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, CancellationToken.None));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task DeletePropagatesNonRaceDeleteProviderFailures()
    {
        var read = new Mock<ItemResponse<RideIdempotencyRecord>>();
        read.SetupGet(value => value.Resource).Returns(new RideIdempotencyRecord(KeyId, RiderId, RideId));
        read.SetupGet(value => value.ETag).Returns("observed-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        var failure = new CosmosException("unavailable during delete", HttpStatusCode.ServiceUnavailable, 0, "legacy-idempotency", 0);
        container.Setup(value => value.ReadItemAsync<RideIdempotencyRecord>(
                KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.DeleteItemAsync<RideIdempotencyRecord>(
                KeyId,
                It.IsAny<PartitionKey>(),
                It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "observed-etag"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var thrown = await Assert.ThrowsAsync<CosmosException>(() =>
            CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
                container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, CancellationToken.None));

        Assert.Same(failure, thrown);
        container.Verify(value => value.ReadItemAsync<RideIdempotencyRecord>(
            KeyId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.DeleteItemAsync<RideIdempotencyRecord>(
            KeyId,
            It.IsAny<PartitionKey>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "observed-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteHonorsPreCancelledTokenBeforeCosmosAccess()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
                container.Object, KeyId, new PartitionKey(RiderId), RiderId, RideId, cancellation.Token));

        container.VerifyNoOtherCalls();
    }
    [Theory]
    [InlineData("", RiderId, RideId)]
    [InlineData(KeyId, " ", RideId)]
    [InlineData(KeyId, RiderId, "\t")]
    public async Task DeleteRejectsInvalidIdentityBeforeCosmosAccess(
        string id,
        string riderId,
        string rideId)
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CosmosLegacyRideIdempotencyCleanup.TryDeleteExactAsync(
                container.Object,
                id,
                new PartitionKey(RiderId),
                riderId,
                rideId,
                CancellationToken.None));

        container.VerifyNoOtherCalls();
    }

}
