using System.Collections;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class FifoQueueTests
{
    [Fact]
    public void PeekNext_ReturnsEligibleDriverWithLowestSequence()
    {
        var queue = new FifoQueue();
        queue.Enqueue(Entry("driver-b", sequence: 2));
        queue.Enqueue(Entry("driver-a", sequence: 1));
        queue.Enqueue(Entry("driver-c", sequence: 0, eligible: false));

        Assert.Equal("driver-a", queue.PeekNext()?.DriverId);
        Assert.Equal(2, queue.Entries.Count);
    }

    [Fact]
    public void Enqueue_RejectsDuplicateDriverInSameQueue()
    {
        var queue = new FifoQueue();
        var entry = Entry("driver-a");
        queue.Enqueue(entry);

        Assert.Throws<InvalidOperationException>(() => queue.Enqueue(entry));
    }

    [Fact]
    public void PeekNext_ReturnsNull_WhenQueueIsEmpty()
    {
        Assert.Null(new FifoQueue().PeekNext());
    }

    [Fact]
    public void Entries_ExcludesIneligibleDrivers()
    {
        var queue = new FifoQueue();
        queue.Enqueue(Entry("eligible", sequence: 1));
        queue.Enqueue(Entry("ineligible", sequence: 0, eligible: false));

        Assert.Single(queue.Entries);
        Assert.Equal("eligible", queue.PeekNext()?.DriverId);
    }

    [Fact]
    public void Entries_UsesJoinTimeAsTieBreakerForEqualSequence()
    {
        var queue = new FifoQueue();
        var joinedAt = DateTimeOffset.Parse("2026-01-01T10:00:00Z");
        queue.Enqueue(Entry("later", sequence: 1, joinedAt: joinedAt.AddMinutes(1)));
        queue.Enqueue(Entry("earlier", sequence: 1, joinedAt: joinedAt));

        Assert.Equal("earlier", queue.PeekNext()?.DriverId);
    }

    [Fact]
    public void Enqueue_AllowsSameDriverInDifferentZones()
    {
        var queue = new FifoQueue();
        queue.Enqueue(Entry("driver-a", zoneId: "zone-a"));
        queue.Enqueue(Entry("driver-a", zoneId: "zone-b"));

        Assert.Equal(2, queue.Entries.Count);
    }

    [Fact]
    public void Enqueue_AllowsSameDriverInDifferentServiceClasses()
    {
        var queue = new FifoQueue();
        queue.Enqueue(Entry("driver-a", serviceClass: "auto"));
        queue.Enqueue(Entry("driver-a", serviceClass: "sedan"));

        Assert.Equal(2, queue.Entries.Count);
    }

    [Fact]
    public void Enqueue_RejectsDuplicateDriverOnlyWithinSameZoneAndClass()
    {
        var queue = new FifoQueue();
        queue.Enqueue(Entry("driver-a", zoneId: "zone-a", serviceClass: "auto"));

        Assert.Throws<InvalidOperationException>(() =>
            queue.Enqueue(Entry("driver-a", zoneId: "zone-a", serviceClass: "auto")));
    }

    [Fact]
    public void Remove_RemovesMatchingDriver()
    {
        var queue = new FifoQueue();
        queue.Enqueue(Entry("driver-a"));
        queue.Enqueue(Entry("driver-b"));

        Assert.True(queue.Remove("driver-a"));
        Assert.Equal("driver-b", queue.PeekNext()?.DriverId);
    }

    [Fact]
    public void Remove_ReturnsFalse_WhenDriverDoesNotExist()
    {
        Assert.False(new FifoQueue().Remove("missing"));
    }

    [Fact]
    public void EnqueueHonorsCancellationBeforeMutation()
    {
        var queue = new FifoQueue();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            queue.Enqueue(Entry("driver-a"), cancellation.Token));

        Assert.Empty(queue.Entries);
    }

    [Fact]
    public void RemoveHonorsCancellationBeforeMutation()
    {
        var queue = new FifoQueue();
        queue.Enqueue(Entry("driver-a"));
        queue.Enqueue(Entry("driver-b"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            queue.Remove("driver-a", cancellation.Token));

        Assert.Equal(new[] { "driver-a", "driver-b" }, queue.Entries.Select(entry => entry.DriverId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EligibleSnapshotHonorsCancellationDuringOrAfterEnumeration(bool yieldSecond)
    {
        using var cancellation = new CancellationTokenSource();
        var first = Entry("driver-a", sequence: 1);
        var second = yieldSecond ? Entry("driver-b", sequence: 2) : null;
        var entries = new CancellingEntries(cancellation, first, second);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            FifoQueueSnapshots.GetEligible(entries, cancellation.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FindEligibleHonorsCancellationDuringOrAfterEnumeration(bool yieldSecond)
    {
        using var cancellation = new CancellationTokenSource();
        var first = Entry("driver-a", sequence: 1);
        var second = yieldSecond ? Entry("driver-b", sequence: 2) : null;
        var entries = new CancellingEntries(cancellation, first, second);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            FifoQueueSnapshots.FindEligible(entries, "missing-driver", cancellation.Token));
    }

    private static DriverQueueEntry Entry(
        string driverId,
        string zoneId = "zone-a",
        string serviceClass = "auto",
        long sequence = 1,
        bool eligible = true,
        DateTimeOffset? joinedAt = null) =>
        new(driverId, zoneId, serviceClass, joinedAt ?? DateTimeOffset.Parse("2026-01-01T10:00:00Z"), sequence, eligible);

    private sealed class CancellingEntries(
        CancellationTokenSource cancellation,
        DriverQueueEntry first,
        DriverQueueEntry? second) : IEnumerable<DriverQueueEntry>
    {
        public IEnumerator<DriverQueueEntry> GetEnumerator()
        {
            yield return first;
            cancellation.Cancel();
            if (second is not null)
                yield return second;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
