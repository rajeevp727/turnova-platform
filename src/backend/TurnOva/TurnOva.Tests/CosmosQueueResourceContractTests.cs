using Microsoft.Azure.Cosmos;
using Moq;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class CosmosQueueResourceContractTests
{
    [Fact]
    public async Task QueueRemovalRejectsNullResourceBeforeStartingDeleteBatch()
    {
        var (container, response) = CreateNullResourceRead();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).RemoveAsync(
                "driver-1", "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue entry resource.", exception.Message);
        VerifyReadOnly(container, response);
    }

    [Fact]
    public async Task QueueRotationRejectsNullResourceBeforeStartingReplaceBatch()
    {
        var (container, response) = CreateNullResourceRead();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackAsync(
                "driver-1", "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue entry resource.", exception.Message);
        VerifyReadOnly(container, response);
    }

    [Fact]
    public async Task OfferScopedQueueRotationRejectsNullResourceBeforeStartingReplaceBatch()
    {
        var (container, response) = CreateNullResourceRead();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackOnceAsync(
                "driver-1", "hyderabad-central", "cab", "offer-1", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue entry resource.", exception.Message);
        VerifyReadOnly(container, response);
    }

    [Theory]
    [InlineData("remove", "different-driver", "driver-1")]
    [InlineData("remove", "driver-1", "different-driver")]
    [InlineData("rotate", "different-driver", "driver-1")]
    [InlineData("rotate", "driver-1", "different-driver")]
    [InlineData("rotate-once", "different-driver", "driver-1")]
    [InlineData("rotate-once", "driver-1", "different-driver")]
    public async Task QueueMutationRejectsResourceWhenDocumentIdOrDriverIdDoesNotMatchBeforeStartingWrite(
        string operation, string documentId, string resourceDriverId)
    {
        var (container, response) = CreateResourceRead(new CosmosQueueStore.QueueEntryDocument
        {
            Id = documentId,
            DriverId = resourceDriverId,
            ZoneId = "hyderabad-central",
            ServiceClass = "cab",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.UtcNow,
            Sequence = 1,
            IsEligible = true
        });
        var store = new CosmosQueueStore(container.Object);
        Func<Task<bool>> action = operation switch
        {
            "remove" => () => store.RemoveAsync(
                "driver-1", "hyderabad-central", "cab", CancellationToken.None),
            "rotate" => () => store.MoveToBackAsync(
                "driver-1", "hyderabad-central", "cab", CancellationToken.None),
            "rotate-once" => () => store.MoveToBackOnceAsync(
                "driver-1", "hyderabad-central", "cab", "offer-1", CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await action());

        Assert.Equal("Cosmos returned a malformed queue entry resource.", exception.Message);
        VerifyReadOnly(container, response);
    }

    [Theory]
    [InlineData("hyderabad-north", "cab", "hyderabad-north|cab")]
    [InlineData("hyderabad-central", "bike", "hyderabad-central|bike")]
    [InlineData("hyderabad-central", "cab", "another-zone|cab")]
    public async Task QueueRotationRejectsResourceFromAnotherPartitionBeforeStartingReplaceBatch(
        string zoneId,
        string serviceClass,
        string zoneServiceKey)
    {
        var (container, response) = CreateResourceRead(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = zoneId,
            ServiceClass = serviceClass,
            ZoneServiceKey = zoneServiceKey,
            JoinedAt = DateTimeOffset.UtcNow,
            Sequence = 1,
            IsEligible = true
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).MoveToBackAsync(
                "driver-1", "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry resource.", exception.Message);
        VerifyReadOnly(container, response);
    }

    [Fact]
    public async Task QueueEligibilityReturnsValidatedCabQueueEntry()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "Hyderabad-Central",
            ServiceClass = "Cab",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.Parse("2026-11-23T00:00:00Z"),
            Sequence = 1,
            IsEligible = true
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var entries = await new CosmosQueueStore(container.Object).GetEligibleAsync(
            "hyderabad-central", "cab", CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal("driver-1", entry.DriverId);
        Assert.Equal("Hyderabad-Central", entry.ZoneId);
        Assert.Equal("Cab", entry.ServiceClass);
        Assert.True(entry.IsEligible);
        iterator.Verify(value => value.HasMoreResults, Times.Exactly(2));
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsNullQueryItemInsteadOfExposingDriver()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage<CosmosQueueStore.QueueEntryDocument>(new CosmosQueueStore.QueueEntryDocument[] { null! });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsQueryItemWhoseIdentityDoesNotMatchDocumentId()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "different-driver",
            DriverId = "driver-1",
            ZoneId = "hyderabad-central",
            ServiceClass = "cab",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.UtcNow,
            Sequence = 1,
            IsEligible = true
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsQueryItemForAnotherZoneOrServiceClass()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "hyderabad-central",
            ServiceClass = "Bike",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.UtcNow,
            Sequence = 1,
            IsEligible = true
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsQueryItemWithWrongPartitionKeyEvenWhenZoneAndServiceMatch()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "hyderabad-central",
            ServiceClass = "cab",
            ZoneServiceKey = "another-zone|cab",
            JoinedAt = DateTimeOffset.UtcNow,
            Sequence = 1,
            IsEligible = true
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsQueryItemThatIsNoLongerEligible()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "hyderabad-central",
            ServiceClass = "cab",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.UtcNow,
            Sequence = 1,
            IsEligible = false
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    public async Task QueueEligibilityRejectsMissingJoinTimestampOrInvalidSequence(
        bool useDefaultJoinedAt, long sequence)
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var joinedAt = useDefaultJoinedAt
            ? default
            : DateTimeOffset.Parse("2026-11-25T00:00:00Z");
        var page = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "hyderabad-central",
            ServiceClass = "cab",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = joinedAt,
            Sequence = sequence,
            IsEligible = true
        });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueEligibilityRejectsMalformedLaterPageInsteadOfReturningPartialCandidates()
    {
        var iterator = new Mock<FeedIterator<CosmosQueueStore.QueueEntryDocument>>();
        iterator.SetupSequence(value => value.HasMoreResults)
            .Returns(true)
            .Returns(true);
        var validPage = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-1",
            DriverId = "driver-1",
            ZoneId = "hyderabad-central",
            ServiceClass = "cab",
            ZoneServiceKey = "hyderabad-central|cab",
            JoinedAt = DateTimeOffset.Parse("2026-11-25T00:00:00Z"),
            Sequence = 1,
            IsEligible = true
        });
        var malformedPage = CreatePage(new CosmosQueueStore.QueueEntryDocument
        {
            Id = "driver-2",
            DriverId = "driver-2",
            ZoneId = "hyderabad-central",
            ServiceClass = "cab",
            ZoneServiceKey = "another-zone|cab",
            JoinedAt = DateTimeOffset.Parse("2026-11-25T00:01:00Z"),
            Sequence = 2,
            IsEligible = true
        });
        iterator.SetupSequence(value => value.ReadNextAsync(CancellationToken.None))
            .ReturnsAsync(validPage.Object)
            .ReturnsAsync(malformedPage.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetEligibleAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue entry query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Exactly(2));
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Exactly(2));
        container.Verify(value => value.GetItemQueryIterator<CosmosQueueStore.QueueEntryDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueAuditReadRejectsNullQueryItemInsteadOfReturningMalformedAuditHistory()
    {
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage<QueueAuditEvent>(new QueueAuditEvent[] { null! });
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned an empty queue audit event query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Theory]
    [InlineData("", "hyderabad-central|cab", "driver-1", "Joined")]
    [InlineData("event-1", "another-zone|cab", "driver-1", "Joined")]
    [InlineData("event-1", "hyderabad-central|cab", "", "Joined")]
    [InlineData("event-1", "hyderabad-central|cab", "driver-1", "")]
    public async Task QueueAuditReadRejectsMalformedOrCrossPartitionQueryItem(
        string eventId,
        string zoneServiceKey,
        string driverId,
        string eventType)
    {
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new QueueAuditEvent(
            eventId, zoneServiceKey, driverId, eventType, DateTimeOffset.UtcNow));
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue audit event query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueAuditReadAcceptsEverySupportedEventShape()
    {
        const string key = "hyderabad-central|cab";
        var occurredAt = DateTimeOffset.Parse("2026-11-25T00:00:00Z");
        var expected = new[]
        {
            new QueueAuditEvent("joined-1", key, "driver-1", QueueAuditEventTypes.Joined, occurredAt),
            new QueueAuditEvent("left-1", key, "driver-1", QueueAuditEventTypes.Left, occurredAt),
            new QueueAuditEvent("moved-1", key, "driver-1", QueueAuditEventTypes.MovedToBack, occurredAt),
            new QueueAuditEvent("rotated-1", key, "driver-1", QueueAuditEventTypes.RotatedToBack, occurredAt, OfferId: "offer-1"),
            new QueueAuditEvent(
                "exception-1", key, "driver-1", QueueAuditEventTypes.DispatchException, occurredAt,
                TraceId: "trace-1", ReasonCategory: QueueAuditReasonCategories.QueueReadFailed)
        };
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        var page = CreatePage(expected);
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var events = await new CosmosQueueStore(container.Object).GetAuditEventsAsync(
            "hyderabad-central", "cab", CancellationToken.None);

        Assert.Equal(expected, events);
        iterator.Verify(value => value.HasMoreResults, Times.Exactly(2));
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Theory]
    [InlineData("UnknownEvent", null, null, null)]
    [InlineData("Joined", "offer-1", null, null)]
    [InlineData("RotatedToBack", null, null, null)]
    [InlineData("DispatchException", null, null, null)]
    [InlineData("DispatchException", "offer-1", "trace-1", "QueueReadFailed")]
    [InlineData("Joined", null, "trace-1", "QueueReadFailed")]
    [InlineData("DispatchException", null, " ", "QueueReadFailed")]
    [InlineData("DispatchException", null, "trace/unsafe", "QueueReadFailed")]
    [InlineData("DispatchException", null, "trace-1", "UnknownReason")]
    public async Task QueueAuditReadRejectsUnknownEventTypesOrInvalidEventSpecificMetadata(
        string eventType, string? offerId, string? traceId, string? reasonCategory)
    {
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new QueueAuditEvent(
            "event-1",
            "hyderabad-central|cab",
            "driver-1",
            eventType,
            DateTimeOffset.Parse("2026-11-25T00:00:00Z"),
            offerId,
            traceId,
            reasonCategory));
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue audit event query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueAuditReadRejectsOverlongDispatchExceptionTraceId()
    {
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new QueueAuditEvent(
            "event-1",
            "hyderabad-central|cab",
            "driver-1",
            QueueAuditEventTypes.DispatchException,
            DateTimeOffset.Parse("2026-11-25T00:00:00Z"),
            TraceId: new string('a', 129),
            ReasonCategory: QueueAuditReasonCategories.QueueReadFailed));
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue audit event query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task QueueAuditReadRejectsMissingOrNonUtcTimestamp(bool useDefaultTimestamp)
    {
        var occurredAt = useDefaultTimestamp
            ? default
            : DateTimeOffset.Parse("2026-11-25T05:30:00+05:30");
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var page = CreatePage(new QueueAuditEvent(
            "event-1",
            "hyderabad-central|cab",
            "driver-1",
            QueueAuditEventTypes.Joined,
            occurredAt));
        iterator.Setup(value => value.ReadNextAsync(CancellationToken.None)).ReturnsAsync(page.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue audit event query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Once);
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Once);
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    [Fact]
    public async Task QueueAuditReadRejectsMalformedLaterPageInsteadOfReturningPartialHistory()
    {
        var iterator = new Mock<FeedIterator<QueueAuditEvent>>();
        iterator.SetupSequence(value => value.HasMoreResults)
            .Returns(true)
            .Returns(true);
        var validPage = CreatePage(new QueueAuditEvent(
            "event-1",
            "hyderabad-central|cab",
            "driver-1",
            "Joined",
            DateTimeOffset.Parse("2026-11-25T00:00:00Z")));
        var malformedPage = CreatePage(new QueueAuditEvent(
            "event-2",
            "another-zone|cab",
            "driver-2",
            "Joined",
            DateTimeOffset.Parse("2026-11-25T00:01:00Z")));
        iterator.SetupSequence(value => value.ReadNextAsync(CancellationToken.None))
            .ReturnsAsync(validPage.Object)
            .ReturnsAsync(malformedPage.Object);
        var container = CreateQueryContainer(iterator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CosmosQueueStore(container.Object).GetAuditEventsAsync(
                "hyderabad-central", "cab", CancellationToken.None));

        Assert.Equal("Cosmos returned a malformed queue audit event query item.", exception.Message);
        iterator.Verify(value => value.HasMoreResults, Times.Exactly(2));
        iterator.Verify(value => value.ReadNextAsync(CancellationToken.None), Times.Exactly(2));
        container.Verify(value => value.GetItemQueryIterator<QueueAuditEvent>(
            It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()), Times.Once);
    }

    private static Mock<Container> CreateQueryContainer<T>(Mock<FeedIterator<T>> iterator)
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.GetItemQueryIterator<T>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string?>(),
                It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return container;
    }

    private static Mock<FeedResponse<T>> CreatePage<T>(params T[] items)
    {
        var page = new Mock<FeedResponse<T>>(MockBehavior.Strict);
        page.As<IEnumerable<T>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => items.AsEnumerable().GetEnumerator());
        return page;
    }

    private static (Mock<Container> Container, Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>> Response)
        CreateResourceRead(CosmosQueueStore.QueueEntryDocument resource)
    {
        var response = new Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(resource);
        response.SetupGet(value => value.ETag).Returns("observed-queue-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        return (container, response);
    }

    private static (Mock<Container> Container, Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>> Response)
        CreateNullResourceRead()
    {
        var response = new Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource)
            .Returns((CosmosQueueStore.QueueEntryDocument)null!);
        response.SetupGet(value => value.ETag).Returns("observed-queue-etag");
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
                "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        return (container, response);
    }

    private static void VerifyReadOnly(
        Mock<Container> container,
        Mock<ItemResponse<CosmosQueueStore.QueueEntryDocument>> response)
    {
        container.Verify(value => value.ReadItemAsync<CosmosQueueStore.QueueEntryDocument>(
            "driver-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()), Times.Never);
        response.VerifyGet(value => value.Resource, Times.Once);
        container.VerifyNoOtherCalls();
        response.VerifyNoOtherCalls();
    }
}
