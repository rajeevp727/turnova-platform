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

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ReplaceReturnsNullWhenLeaseDisappearsOrAnotherRunnerWinsTheEtagRace(
        HttpStatusCode statusCode)
    {
        var container = new Mock<Container>();
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosMigrationLeaseDocument>(),
                It.IsAny<string>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "lease changed during replace", statusCode, 0, "activity", 0));
        var store = new CosmosMigrationLeaseStore(container.Object);
        var expected = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "stale-etag", DateTimeOffset.UtcNow.AddMinutes(10));

        var renewed = await store.TryReplaceAsync(
            expected, "owner-1", DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);

        Assert.Null(renewed);
        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosMigrationLeaseDocument>(),
            "schema-migrations",
            It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options =>
                options != null && options.IfMatchEtag == "stale-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplacePropagatesNonRaceCosmosFailures()
    {
        var container = new Mock<Container>();
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosMigrationLeaseDocument>(),
                It.IsAny<string>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "cosmos unavailable", HttpStatusCode.ServiceUnavailable, 0, "activity", 0));
        var store = new CosmosMigrationLeaseStore(container.Object);
        var expected = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "etag-observed", DateTimeOffset.UtcNow.AddMinutes(10));

        var exception = await Assert.ThrowsAsync<CosmosException>(() =>
            store.TryReplaceAsync(
                expected, "owner-1", DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosMigrationLeaseDocument>(),
            "schema-migrations",
            It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options =>
                options != null && options.IfMatchEtag == "etag-observed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ReleaseReturnsFalseWhenLeaseIsMissingOrReplaced(HttpStatusCode statusCode)
    {
        var container = new Mock<Container>();
        container.Setup(value => value.DeleteItemAsync<CosmosMigrationLeaseDocument>(
                "schema-migrations",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "lease is missing or replaced", statusCode, 0, "activity", 0));
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

    [Fact]
    public async Task CreateUsesTheMigrationLeaseIdentityAndReturnsCreatedEtag()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var container = new Mock<Container>();
        var response = new Mock<ItemResponse<CosmosMigrationLeaseDocument>>();
        response.SetupGet(value => value.ETag).Returns("etag-created");
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<CosmosMigrationLeaseDocument>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        var store = new CosmosMigrationLeaseStore(container.Object);

        var created = await store.TryCreateAsync("owner-1", expiresAt, CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal("schema-migrations", created!.Id);
        Assert.Equal("owner-1", created.Owner);
        Assert.Equal("etag-created", created.ETag);
        Assert.Equal(expiresAt, created.ExpiresAtUtc);
        container.Verify(value => value.CreateItemAsync(
            It.Is<CosmosMigrationLeaseDocument>(document =>
                document.Id == "schema-migrations" &&
                document.Owner == "owner-1" &&
                document.ExpiresAtUtc == expiresAt),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateReturnsNullWhenTheLeaseAlreadyExists()
    {
        var container = new Mock<Container>();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<CosmosMigrationLeaseDocument>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "lease already exists", HttpStatusCode.Conflict, 0, "migration-lease-test", 0));
        var store = new CosmosMigrationLeaseStore(container.Object);

        var created = await store.TryCreateAsync(
            "owner-1", DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);

        Assert.Null(created);
    }

    [Fact]
    public async Task ReadReturnsLeaseMetadataAndTheObservedEtag()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(20);
        var document = new CosmosMigrationLeaseDocument
        {
            Id = "schema-migrations",
            Owner = "owner-1",
            ExpiresAtUtc = expiresAt
        };
        var container = new Mock<Container>();
        var response = new Mock<ItemResponse<CosmosMigrationLeaseDocument>>();
        response.SetupGet(value => value.Resource).Returns(document);
        response.SetupGet(value => value.ETag).Returns("etag-read");
        container.Setup(value => value.ReadItemAsync<CosmosMigrationLeaseDocument>(
                "schema-migrations",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        var store = new CosmosMigrationLeaseStore(container.Object);

        var lease = await store.ReadAsync(CancellationToken.None);

        Assert.NotNull(lease);
        Assert.Equal("schema-migrations", lease!.Id);
        Assert.Equal("owner-1", lease.Owner);
        Assert.Equal("etag-read", lease.ETag);
        Assert.Equal(expiresAt, lease.ExpiresAtUtc);
        container.Verify(value => value.ReadItemAsync<CosmosMigrationLeaseDocument>(
            "schema-migrations",
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadReturnsNullWhenNoLeaseDocumentExists()
    {
        var container = new Mock<Container>();
        container.Setup(value => value.ReadItemAsync<CosmosMigrationLeaseDocument>(
                "schema-migrations",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "lease not found", HttpStatusCode.NotFound, 0, "migration-lease-test", 0));
        var store = new CosmosMigrationLeaseStore(container.Object);

        var lease = await store.ReadAsync(CancellationToken.None);

        Assert.Null(lease);
    }

    [Fact]
    public async Task ReleaseReturnsFalseWhenTheLeaseDocumentNoLongerExists()
    {
        var container = new Mock<Container>();
        container.Setup(value => value.DeleteItemAsync<CosmosMigrationLeaseDocument>(
                "schema-migrations",
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException(
                "lease not found", HttpStatusCode.NotFound, 0, "migration-lease-test", 0));
        var store = new CosmosMigrationLeaseStore(container.Object);
        var expected = new CosmosMigrationLease(
            "schema-migrations", "owner-1", "etag-current", DateTimeOffset.UtcNow.AddMinutes(10));

        var released = await store.TryReleaseAsync(expected, CancellationToken.None);

        Assert.False(released);
    }

    [Theory]
    [InlineData("", "owner-1", "etag-1", "owner-2")]
    [InlineData("other-id", "owner-1", "etag-1", "owner-2")]
    [InlineData("schema-migrations", "", "etag-1", "owner-2")]
    [InlineData("schema-migrations", "owner-1", "", "owner-2")]
    [InlineData("schema-migrations", "owner-1", "etag-1", " ")]
    public async Task ReplaceRejectsAnInvalidExpectedLeaseBeforeCallingCosmos(
        string id,
        string owner,
        string etag,
        string newOwner)
    {
        var container = new Mock<Container>();
        var store = new CosmosMigrationLeaseStore(container.Object);
        var expected = new CosmosMigrationLease(
            id, owner, etag, DateTimeOffset.UtcNow.AddMinutes(10));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.TryReplaceAsync(
                expected, newOwner, DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None));

        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "owner-1", "etag-1")]
    [InlineData("other-id", "owner-1", "etag-1")]
    [InlineData("schema-migrations", "", "etag-1")]
    [InlineData("schema-migrations", "owner-1", "")]
    public async Task ReleaseRejectsAnInvalidExpectedLeaseBeforeCallingCosmos(
        string id,
        string owner,
        string etag)
    {
        var container = new Mock<Container>();
        var store = new CosmosMigrationLeaseStore(container.Object);
        var expected = new CosmosMigrationLease(
            id, owner, etag, DateTimeOffset.UtcNow.AddMinutes(10));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.TryReleaseAsync(expected, CancellationToken.None));

        container.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task CreateRejectsBlankOwnerBeforeCallingCosmos(string owner)
    {
        var container = new Mock<Container>();
        var store = new CosmosMigrationLeaseStore(container.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.TryCreateAsync(
                owner,
                DateTimeOffset.UtcNow.AddMinutes(30),
                CancellationToken.None));

        container.VerifyNoOtherCalls();
    }

}
