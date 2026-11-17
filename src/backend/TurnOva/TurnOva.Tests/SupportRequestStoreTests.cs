using System.Net;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Support;
using TurnOva.Infrastructure.Support;

namespace TurnOva.Tests;

public sealed class SupportRequestStoreTests
{
    [Fact]
    public void SupportRequestSerializesPerItemTtlUsingCosmosPropertyName()
    {
        var request = CreateRequest() with { Ttl = 604800 };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(604800, document.RootElement.GetProperty("ttl").GetInt32());
    }

    [Fact]
    public async Task InMemoryStoreCreatesAndReplaysSameSubmission()
    {
        var store = new InMemorySupportRequestStore();
        var request = CreateRequest();

        var created = await store.CreateOrGetAsync(request);
        var replay = await store.CreateOrGetAsync(request with { SubmittedAtUtc = request.SubmittedAtUtc.AddMinutes(1) });

        Assert.Equal(SupportRequestWriteStatus.Created, created.Status);
        Assert.Equal(SupportRequestWriteStatus.Replayed, replay.Status);
        Assert.Equal(request.SubmittedAtUtc, replay.Request.SubmittedAtUtc);
    }

    [Fact]
    public async Task InMemoryStoreRejectsSameKeyWithDifferentContent()
    {
        var store = new InMemorySupportRequestStore();
        var request = CreateRequest();

        await store.CreateOrGetAsync(request);
        var conflict = await store.CreateOrGetAsync(request with { Description = "Different content for the same idempotency key." });

        Assert.Equal(SupportRequestWriteStatus.Conflict, conflict.Status);
        Assert.Equal(request.Description, conflict.Request.Description);
    }

    [Fact]
    public async Task InMemoryStoreConcurrentSameKeyCreatesOnlyOneRecord()
    {
        var store = new InMemorySupportRequestStore();
        var request = CreateRequest();

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => store.CreateOrGetAsync(request)));

        Assert.Single(results, result => result.Status == SupportRequestWriteStatus.Created);
        Assert.Equal(15, results.Count(result => result.Status == SupportRequestWriteStatus.Replayed));
    }

    [Fact]
    public async Task CosmosStoreCreatesInReporterPartition()
    {
        var container = new Mock<Container>();
        var response = new Mock<ItemResponse<SupportRequest>>();
        PartitionKey? capturedPartitionKey = null;
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<SupportRequest, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (_, partitionKey, _, _) => capturedPartitionKey = partitionKey)
            .ReturnsAsync(response.Object);

        var store = new CosmosSupportRequestStore(container.Object);
        var request = CreateRequest();

        var result = await store.CreateOrGetAsync(request);

        Assert.Equal(SupportRequestWriteStatus.Created, result.Status);
        Assert.Equal(new PartitionKey(request.ReporterId), capturedPartitionKey);
        container.Verify(value => value.CreateItemAsync(
            It.Is<SupportRequest>(item => item.Id == request.Id && item.ReporterId == request.ReporterId),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CosmosStoreReplaysSameContentAfterCreateConflict()
    {
        var container = new Mock<Container>();
        var request = CreateRequest();
        var response = new Mock<ItemResponse<SupportRequest>>();
        response.SetupGet(value => value.Resource).Returns(request);
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("duplicate", HttpStatusCode.Conflict, 0, "activity", 0));
        container.Setup(value => value.ReadItemAsync<SupportRequest>(
                request.Id,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var result = await new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(request);

        Assert.Equal(SupportRequestWriteStatus.Replayed, result.Status);
        Assert.Equal(request.Id, result.Request.Id);
    }

    [Fact]
    public async Task CosmosStoreDoesNotOverwriteDifferentContentOnIdempotencyConflict()
    {
        var container = new Mock<Container>();
        var request = CreateRequest();
        var existing = request with { Description = "Original stored support content." };
        var response = new Mock<ItemResponse<SupportRequest>>();
        response.SetupGet(value => value.Resource).Returns(existing);
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("duplicate", HttpStatusCode.Conflict, 0, "activity", 0));
        container.Setup(value => value.ReadItemAsync<SupportRequest>(
                request.Id,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var result = await new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(request);

        Assert.Equal(SupportRequestWriteStatus.Conflict, result.Status);
        Assert.Equal(existing.Description, result.Request.Description);
    }

    [Fact]
    public async Task CosmosStoreFailsClosedWhenConflictItemCannotBeRead()
    {
        var container = new Mock<Container>();
        var request = CreateRequest();
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<SupportRequest>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("duplicate", HttpStatusCode.Conflict, 0, "activity", 0));
        container.Setup(value => value.ReadItemAsync<SupportRequest>(
                request.Id,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("missing", HttpStatusCode.NotFound, 0, "activity", 0));

        var result = await new CosmosSupportRequestStore(container.Object).CreateOrGetAsync(request);

        Assert.Equal(SupportRequestWriteStatus.Conflict, result.Status);
    }

    private static SupportRequest CreateRequest() => new(
        "support-request-id-1",
        "rider-1",
        "Rider",
        "Trip update issue",
        "The trip status did not refresh after reconnecting.",
        DateTimeOffset.Parse("2026-11-17T12:00:00Z"));
}
