using TurnOva.API;

namespace TurnOva.Tests;

public sealed class StorageReadinessProbeTests
{
    [Fact]
    public async Task CheckAsyncCompletesWhenStorageProbeSucceeds()
    {
        var probe = new StorageReadinessProbe(_ => Task.CompletedTask, TimeSpan.FromSeconds(1));

        await probe.CheckAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CheckAsyncMapsProbeTimeoutToReadinessFailure()
    {
        var probe = new StorageReadinessProbe(
            ct => Task.Delay(Timeout.InfiniteTimeSpan, ct),
            TimeSpan.FromMilliseconds(20));

        var exception = await Assert.ThrowsAsync<StorageReadinessException>(
            () => probe.CheckAsync(CancellationToken.None));

        Assert.Equal("timeout", exception.FailureKind);
    }

    [Fact]
    public async Task CheckAsyncPropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probe = new StorageReadinessProbe(
            ct => Task.FromCanceled(ct),
            TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.CheckAsync(cancellation.Token));
    }
}
