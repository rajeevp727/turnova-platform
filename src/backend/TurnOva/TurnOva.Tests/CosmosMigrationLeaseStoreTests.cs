using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Tests;

public sealed class CosmosMigrationLeaseStoreTests
{
    [Fact]
    public async Task ReplaceUsesTheObservedEtagAsIfMatch()
    {
        var container = new Mock<Container>();
        var response = new Mock<ItemResponse<CosmosMigrationLeaseDocument>>();
        response.SetupGet(value => value.ETag).Returns("etag-new");
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosMigrationLeaseDocument>(),
                It.IsAny<string>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        var store = new CosmosMigrationLeaseStore(container.Object);
        var expected = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "etag-observed", DateTimeOffset.UtcNow.AddMinutes(10));

        var renewed = await store.TryReplaceAsync(
            expected, "owner-1", DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);

        Assert.Equal("etag-new", renewed?.ETag);
        container.Verify(value => value.ReplaceItemAsync(
            It.Is<CosmosMigrationLeaseDocument>(document =>
                document.Id == "schema-migrations" && document.Owner == "owner-1"),
            "schema-migrations",
            It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options =>
                options != null && options.IfMatchEtag == "etag-observed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplaceReturnsNullWhenAnotherRunnerWinsTheEtagRace()
    {
        var container = new Mock<Container>();
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosMigrationLeaseDocument>(),
                It.IsAny<string>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "lease replaced", HttpStatusCode.PreconditionFailed, 0, "activity", 0));
        var store = new CosmosMigrationLeaseStore(container.Object);
        var expected = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "stale-etag", DateTimeOffset.UtcNow.AddMinutes(10));

        var renewed = await store.TryReplaceAsync(
            expected, "owner-1", DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);

        Assert.Null(renewed);
    }

    [Fact]
    public async Task ReleaseUsesTheLatestEtagAndDoesNotDeleteAReplacementLease()
    {
        var container = new Mock<Container>();
        container.Setup(value => value.DeleteItemAsync<CosmosMigrationLeaseDocument>(
                "schema-migrations",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "lease replaced", HttpStatusCode.PreconditionFailed, 0, "activity", 0));
        var store = new CosmosMigrationLeaseStore(container.Object);
        var expected = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "latest-etag", DateTimeOffset.UtcNow.AddMinutes(10));

        var released = await store.TryReleaseAsync(expected, CancellationToken.None);

        Assert.False(released);
        container.Verify(value => value.DeleteItemAsync<CosmosMigrationLeaseDocument>(
            "schema-migrations",
            It.IsAny<PartitionKey>(),
            It.Is<ItemRequestOptions?>(options =>
                options != null && options.IfMatchEtag == "latest-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
