using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.API;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class UnionCreateIfAbsentTests
{
    [Fact]
    public async Task InMemoryCreateHonorsCancellationBeforeMutatingStore()
    {
        var union = CreateUnion();
        var inMemoryUnions = new ConcurrentDictionary<string, DriverUnion>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnionCreateIfAbsent.TryCreateAsync(
                null,
                inMemoryUnions,
                union,
                cancellation.Token));

        Assert.Empty(inMemoryUnions);
    }

    [Fact]
    public async Task CosmosCreateConflictReturnsFalseWithoutFallingBackToInMemoryStore()
    {
        var union = CreateUnion();
        var inMemoryUnions = new ConcurrentDictionary<string, DriverUnion>();
        var container = new Mock<Container>(MockBehavior.Strict);
        PartitionKey? observedPartitionKey = null;
        CancellationToken observedCancellationToken = default;

        container.Setup(value => value.CreateItemAsync(
                It.IsAny<DriverUnion>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<DriverUnion, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (document, partitionKey, _, cancellationToken) =>
                {
                    Assert.Equal(union, document);
                    observedPartitionKey = partitionKey;
                    observedCancellationToken = cancellationToken;
                })
            .ThrowsAsync(new CosmosException(
                "union already exists", HttpStatusCode.Conflict, 0, "union-create-test", 0));

        using var cancellation = new CancellationTokenSource();
        var created = await UnionCreateIfAbsent.TryCreateAsync(
            container.Object, inMemoryUnions, union, cancellation.Token);

        Assert.False(created);
        Assert.Empty(inMemoryUnions);
        Assert.True(observedPartitionKey.HasValue);
        Assert.Equal(new PartitionKey(union.UnionId), observedPartitionKey.Value);
        Assert.Equal(cancellation.Token, observedCancellationToken);
        container.Verify(value => value.CreateItemAsync(
            It.IsAny<DriverUnion>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CosmosCreatePropagatesNonConflictFailures()
    {
        var union = CreateUnion();
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.CreateItemAsync(
                It.IsAny<DriverUnion>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cosmos-unavailable"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UnionCreateIfAbsent.TryCreateAsync(
                container.Object,
                new ConcurrentDictionary<string, DriverUnion>(),
                union,
                CancellationToken.None));

        Assert.Equal("cosmos-unavailable", exception.Message);
        container.Verify(value => value.CreateItemAsync(
            It.IsAny<DriverUnion>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DriverUnion CreateUnion()
    {
        var now = DateTimeOffset.UtcNow;
        return new DriverUnion(
            "union-cosmos-conflict-test",
            "Cosmos Conflict Test Union",
            "HYD-COSMOS",
            "Hyderabad",
            "Union Manager",
            null,
            ["hyderabad-central"],
            ["cab"],
            "Active",
            now,
            now);
    }
}
