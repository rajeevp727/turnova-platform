using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class FifoQueueTests
{
    [Fact]
    public void PeekNext_ReturnsEligibleDriverWithLowestSequence()
    {
        var queue = new FifoQueue();
        queue.Enqueue(new("driver-b", "zone-a", "standard", DateTimeOffset.UtcNow, 2, true));
        queue.Enqueue(new("driver-a", "zone-a", "standard", DateTimeOffset.UtcNow, 1, true));
        queue.Enqueue(new("driver-c", "zone-a", "standard", DateTimeOffset.UtcNow, 0, false));

        Assert.Equal("driver-a", queue.PeekNext()?.DriverId);
        Assert.Equal(2, queue.Entries.Count);
    }

    [Fact]
    public void Enqueue_RejectsDuplicateDriverInSameQueue()
    {
        var queue = new FifoQueue();
        var entry = new DriverQueueEntry("driver-a", "zone-a", "standard", DateTimeOffset.UtcNow, 1, true);
        queue.Enqueue(entry);

        Assert.Throws<InvalidOperationException>(() => queue.Enqueue(entry));
    }
}