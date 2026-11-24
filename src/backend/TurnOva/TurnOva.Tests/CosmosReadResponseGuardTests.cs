using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;

namespace TurnOva.Tests;

public sealed class CosmosReadResponseGuardTests
{
    [Fact]
    public async Task AwaitWithCancellationPrecedenceReturnsSuccessfulProviderResult()
    {
        var result = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
            () => Task.FromResult("provider-result"),
            CancellationToken.None);

        Assert.Equal("provider-result", result);
    }

    [Fact]
    public async Task AwaitWithCancellationPrecedencePrefersCancellationOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () =>
                {
                    cancellation.Cancel();
                    return Task.FromException<string>(Unavailable());
                },
                cancellation.Token));
    }

    [Fact]
    public async Task AwaitWithCancellationPrecedencePropagatesProviderFailureWhenNotCancelled()
    {
        var exception = Unavailable();

        var actual = await Assert.ThrowsAsync<CosmosException>(() =>
            CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => Task.FromException<string>(exception),
                CancellationToken.None));

        Assert.Same(exception, actual);
    }

    private const string UnionId = "union-read-guard";
    private const string DriverId = "driver-read-guard";

    [Fact]
    public async Task ReadItemReturnsIdentityMatchedResourceAndUsesPartitionKey()
    {
        var union = CreateUnion();
        var response = CreateResponse(union);
        var container = CreateReadContainer<DriverUnion>(
            UnionId,
            UnionId,
            () => Task.FromResult(response.Object));

        var result = await CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
            container.Object,
            UnionId,
            new PartitionKey(UnionId),
            item => string.Equals(item.UnionId, UnionId, StringComparison.Ordinal),
            "Union",
            CancellationToken.None);

        Assert.Equal(union, result);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId,
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey(UnionId))),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemReturnsNullForNotFound()
    {
        var container = CreateReadContainer<DriverUnion>(
            UnionId,
            UnionId,
            () => Task.FromException<ItemResponse<DriverUnion>>(NotFound()));

        var result = await CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
            container.Object,
            UnionId,
            new PartitionKey(UnionId),
            item => item.UnionId == UnionId,
            "Union",
            CancellationToken.None);

        Assert.Null(result);
        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemRejectsNullResource()
    {
        var response = CreateResponse<DriverUnion>(null!);
        var container = CreateReadContainer<DriverUnion>(
            UnionId,
            UnionId,
            () => Task.FromResult(response.Object));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
                container.Object,
                UnionId,
                new PartitionKey(UnionId),
                item => item.UnionId == UnionId,
                "Union",
                CancellationToken.None));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemRejectsMismatchedResourceIdentity()
    {
        var response = CreateResponse(CreateUnion("union-other"));
        var container = CreateReadContainer<DriverUnion>(
            UnionId,
            UnionId,
            () => Task.FromResult(response.Object));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
                container.Object,
                UnionId,
                new PartitionKey(UnionId),
                item => item.UnionId == UnionId,
                "Union",
                CancellationToken.None));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemHonorsCancellationBeforeCallingCosmos()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var container = new Mock<Container>(MockBehavior.Strict);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
                container.Object,
                UnionId,
                new PartitionKey(UnionId),
                item => item.UnionId == UnionId,
                "Union",
                cancellation.Token));

        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemCancellationTakesPrecedenceOverNotFound()
    {
        using var cancellation = new CancellationTokenSource();
        var container = CreateReadContainer<DriverUnion>(
            UnionId,
            UnionId,
            () =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnion>>(NotFound());
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
                container.Object,
                UnionId,
                new PartitionKey(UnionId),
                item => item.UnionId == UnionId,
                "Union",
                cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemHonorsCancellationAfterSuccessfulProviderResponse()
    {
        using var cancellation = new CancellationTokenSource();
        var response = CreateResponse(CreateUnion());
        var container = CreateReadContainer<DriverUnion>(
            UnionId,
            UnionId,
            () =>
            {
                cancellation.Cancel();
                return Task.FromResult(response.Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
                container.Object,
                UnionId,
                new PartitionKey(UnionId),
                item => item.UnionId == UnionId,
                "Union",
                cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemHonorsCancellationDuringResourceHydration()
    {
        using var cancellation = new CancellationTokenSource();
        var union = CreateUnion();
        var response = new Mock<ItemResponse<DriverUnion>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(() =>
        {
            cancellation.Cancel();
            return union;
        });
        var container = CreateReadContainer<DriverUnion>(
            UnionId,
            UnionId,
            () => Task.FromResult(response.Object));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
                container.Object,
                UnionId,
                new PartitionKey(UnionId),
                item => item.UnionId == UnionId,
                "Union",
                cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        response.VerifyGet(value => value.Resource, Times.Once);
        container.VerifyNoOtherCalls();
        response.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemCancellationTakesPrecedenceOverOtherProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var container = CreateReadContainer<DriverUnion>(
            UnionId,
            UnionId,
            () =>
            {
                cancellation.Cancel();
                return Task.FromException<ItemResponse<DriverUnion>>(Unavailable());
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
                container.Object,
                UnionId,
                new PartitionKey(UnionId),
                item => item.UnionId == UnionId,
                "Union",
                cancellation.Token));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemPropagatesNonNotFoundProviderFailure()
    {
        var container = CreateReadContainer<DriverUnion>(
            UnionId,
            UnionId,
            () => Task.FromException<ItemResponse<DriverUnion>>(Unavailable()));

        await Assert.ThrowsAsync<CosmosException>(() =>
            CosmosReadResponseGuard.ReadItemAsync<DriverUnion>(
                container.Object,
                UnionId,
                new PartitionKey(UnionId),
                item => item.UnionId == UnionId,
                "Union",
                CancellationToken.None));

        container.Verify(value => value.ReadItemAsync<DriverUnion>(
            UnionId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public void RequireResourceRejectsMismatchedDriverAssignmentIdentity()
    {
        var assignment = CreateAssignment("driver-other");

        Assert.Throws<InvalidOperationException>(() =>
            CosmosReadResponseGuard.RequireResource(
                assignment,
                item => item.DriverId == DriverId && !string.IsNullOrWhiteSpace(item.UnionId),
                "Driver Union assignment"));
    }

    [Fact]
    public void RequireResourceRejectsMismatchedAuditEventIdentity()
    {
        var audit = CreateAudit("driver-other", "event-1");

        Assert.Throws<InvalidOperationException>(() =>
            CosmosReadResponseGuard.RequireResource(
                audit,
                item => item.DriverId == DriverId && item.EventId == "event-1",
                "Driver Union audit event"));
    }

    [Fact]
    public async Task ReadAllReturnsIdentityMatchedResourcesAcrossPages()
    {
        var first = CreateUnion("union-page-1");
        var second = CreateUnion("union-page-2");
        var iterator = new Mock<FeedIterator<DriverUnion>>(MockBehavior.Strict);
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(true).Returns(false);
        iterator.SetupSequence(value => value.ReadNextAsync(CancellationToken.None))
            .ReturnsAsync(CreatePage(first).Object)
            .ReturnsAsync(CreatePage(second).Object);

        var results = await CosmosReadResponseGuard.ReadAllAsync(
            iterator.Object,
            item => item.UnionId.StartsWith("union-page-", StringComparison.Ordinal),
            "Union",
            CancellationToken.None);

        Assert.Equal(new[] { first, second }, results);
        iterator.Verify(value => value.HasMoreResults, Times.Exactly(3));
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Exactly(2));
        iterator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadAllRejectsNullResource()
    {
        var iterator = CreateSinglePageIterator<DriverUnion>(CreatePage<DriverUnion>(new DriverUnion[] { null! }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CosmosReadResponseGuard.ReadAllAsync(
                iterator.Object,
                item => item.UnionId == UnionId,
                "Union",
                CancellationToken.None));

        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ReadAllRejectsMismatchedResourceIdentity()
    {
        var iterator = CreateSinglePageIterator<DriverUnion>(CreatePage(CreateUnion("union-other")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CosmosReadResponseGuard.ReadAllAsync(
                iterator.Object,
                item => item.UnionId == UnionId,
                "Union",
                CancellationToken.None));

        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ReadAllHonorsCancellationBeforeCheckingIterator()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var iterator = new Mock<FeedIterator<DriverUnion>>(MockBehavior.Strict);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosReadResponseGuard.ReadAllAsync(
                iterator.Object,
                item => item.UnionId == UnionId,
                "Union",
                cancellation.Token));

        iterator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadAllHonorsCancellationAfterFinalPageBeforeReturning()
    {
        using var cancellation = new CancellationTokenSource();
        var iterator = new Mock<FeedIterator<DriverUnion>>(MockBehavior.Strict);
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(CreatePage(CreateUnion()).Object);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosReadResponseGuard.ReadAllAsync(
                iterator.Object,
                item => item.UnionId == UnionId,
                "Union",
                cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        iterator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadAllCancellationTakesPrecedenceOverProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var iterator = new Mock<FeedIterator<DriverUnion>>(MockBehavior.Strict);
        iterator.Setup(value => value.HasMoreResults).Returns(true);
        iterator.Setup(value => value.ReadNextAsync(cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<FeedResponse<DriverUnion>>(Unavailable());
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosReadResponseGuard.ReadAllAsync(
                iterator.Object,
                item => item.UnionId == UnionId,
                "Union",
                cancellation.Token));

        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(cancellation.Token), Times.Once);
        iterator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadItemReturnsDriverProfileOnlyWhenDocumentAndDriverIdsMatch()
    {
        var profile = CreateDriverProfile();
        var response = CreateResponse(profile);
        var container = CreateReadContainer<DriverOnboardingProfile>(
            DriverId,
            DriverId,
            () => Task.FromResult(response.Object));

        var result = await CosmosReadResponseGuard.ReadItemAsync<DriverOnboardingProfile>(
            container.Object,
            DriverId,
            new PartitionKey(DriverId),
            item => item.Id == DriverId && item.DriverId == DriverId,
            "Driver onboarding profile",
            CancellationToken.None);

        Assert.Equal(profile, result);
        container.Verify(value => value.ReadItemAsync<DriverOnboardingProfile>(
            DriverId, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public void RequireResourceRejectsMismatchedDriverProfileIdentity()
    {
        var profile = CreateDriverProfile(id: "driver-other");

        Assert.Throws<InvalidOperationException>(() =>
            CosmosReadResponseGuard.RequireResource(
                profile,
                item => item.Id == DriverId && item.DriverId == DriverId,
                "Driver onboarding profile"));
    }

    [Fact]
    public async Task ReadAllReturnsOnlyEligibleCabProfilesForRequestedZone()
    {
        var profile = CreateDriverProfile();
        var iterator = CreateSinglePageIterator(CreatePage(profile));

        var results = await CosmosReadResponseGuard.ReadAllAsync(
            iterator.Object,
            item => !string.IsNullOrWhiteSpace(item.DriverId) &&
                item.Id == item.DriverId &&
                string.Equals(item.ZoneId, "zone-a", StringComparison.OrdinalIgnoreCase) &&
                item.Status == "Approved" &&
                item.AvailabilityStatus == "Online" &&
                string.Equals(item.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase),
            "eligible Cab driver profile",
            CancellationToken.None);

        Assert.Equal(new[] { profile }, results);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ReadAllRejectsDriverProfileFromAnotherZone()
    {
        var iterator = CreateSinglePageIterator(CreatePage(CreateDriverProfile(zoneId: "zone-b")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CosmosReadResponseGuard.ReadAllAsync(
                iterator.Object,
                item => item.Id == item.DriverId &&
                    string.Equals(item.ZoneId, "zone-a", StringComparison.OrdinalIgnoreCase) &&
                    item.Status == "Approved" &&
                    item.AvailabilityStatus == "Online" &&
                    string.Equals(item.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase),
                "eligible Cab driver profile",
                CancellationToken.None));
    }

    [Theory]
    [InlineData("Rejected", "Online", "cab")]
    [InlineData("Approved", "Offline", "cab")]
    [InlineData("Approved", "Online", "bike")]
    public async Task ReadAllRejectsUnapprovedUnavailableOrNonCabProfiles(
        string status,
        string availabilityStatus,
        string serviceClass)
    {
        var iterator = CreateSinglePageIterator(CreatePage(CreateDriverProfile(
            status: status,
            availabilityStatus: availabilityStatus,
            serviceClass: serviceClass)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CosmosReadResponseGuard.ReadAllAsync(
                iterator.Object,
                item => item.Id == item.DriverId &&
                    string.Equals(item.ZoneId, "zone-a", StringComparison.OrdinalIgnoreCase) &&
                    item.Status == "Approved" &&
                    item.AvailabilityStatus == "Online" &&
                    string.Equals(item.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase),
                "eligible Cab driver profile",
                CancellationToken.None));
    }

    private static DriverUnion CreateUnion(string? unionId = null)
    {
        var id = unionId ?? UnionId;
        var now = DateTimeOffset.Parse("2026-11-24T00:00:00Z");
        return new DriverUnion(id, "Union", id, null, null, null, ["zone-a"], ["cab"], "Active", now, now);
    }

    private static DriverUnionAssignment CreateAssignment(string driverId)
    {
        var now = DateTimeOffset.Parse("2026-11-24T00:00:00Z");
        return new DriverUnionAssignment(driverId, "union-a", "Union A", "Active", now, now, "Test");
    }

    private static DriverUnionAuditEvent CreateAudit(string driverId, string eventId) =>
        new(eventId, driverId, null, null, "union-a", "Union A", "DriverAssigned", "Test", "actor-1",
            DateTimeOffset.Parse("2026-11-24T00:00:00Z"));

    private static DriverOnboardingProfile CreateDriverProfile(
        string? id = null,
        string? driverId = null,
        string zoneId = "zone-a",
        string status = "Approved",
        string availabilityStatus = "Online",
        string serviceClass = "cab")
    {
        var expectedDriverId = driverId ?? DriverId;
        return new DriverOnboardingProfile(
            id ?? expectedDriverId,
            expectedDriverId,
            zoneId,
            "Driver",
            "+15555550123",
            "Toyota",
            "Etios",
            "TS09AB1234",
            serviceClass,
            4,
            [],
            status,
            DateTimeOffset.Parse("2026-11-24T00:00:00Z"),
            AvailabilityStatus: availabilityStatus);
    }

    private static Mock<ItemResponse<T>> CreateResponse<T>(T resource)
    {
        var response = new Mock<ItemResponse<T>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(resource);
        return response;
    }

    private static Mock<Container> CreateReadContainer<T>(
        string id,
        string partitionKey,
        Func<Task<ItemResponse<T>>> responseFactory)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<T>(
                id,
                It.Is<PartitionKey>(key => key.Equals(new PartitionKey(partitionKey))),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => responseFactory());
        return container;
    }

    private static Mock<FeedIterator<T>> CreateSinglePageIterator<T>(Mock<FeedResponse<T>> page)
    {
        var iterator = new Mock<FeedIterator<T>>(MockBehavior.Strict);
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        return iterator;
    }

    private static Mock<FeedResponse<T>> CreatePage<T>(params T[] items)
    {
        var page = new Mock<FeedResponse<T>>(MockBehavior.Strict);
        page.As<IEnumerable<T>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => items.AsEnumerable().GetEnumerator());
        return page;
    }

    private static CosmosException NotFound() =>
        new("Union resource was not found.", HttpStatusCode.NotFound, 0, "union-read-guard", 0);

    private static CosmosException Unavailable() =>
        new("Cosmos is unavailable.", HttpStatusCode.ServiceUnavailable, 0, "union-read-guard", 0);
}
