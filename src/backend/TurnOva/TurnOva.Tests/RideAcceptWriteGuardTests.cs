using System.Net;
using Microsoft.Azure.Cosmos;
using TurnOva.API;

namespace TurnOva.Tests;

public sealed class RideAcceptWriteGuardTests
{
    [Fact]
    public async Task ConditionalConflictReleasesReservationBeforeSurfacingCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var released = false;
        CancellationToken releaseToken = default;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RideAcceptWriteGuard.ExecuteAsync(
                () =>
                {
                    cancellation.Cancel();
                    return Task.FromException<string>(
                        new CosmosException("conditional ride write lost", HttpStatusCode.PreconditionFailed, 0, "ride-accept", 0));
                },
                token =>
                {
                    released = true;
                    releaseToken = token;
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.True(released);
        Assert.False(releaseToken.CanBeCanceled);
    }

    [Fact]
    public async Task ConditionalConflictReleasesReservationAndPreservesConflictWithoutCancellation()
    {
        var failure = new CosmosException(
            "ride disappeared", HttpStatusCode.NotFound, 0, "ride-accept", 0);
        var released = false;
        CancellationToken releaseToken = default;

        var thrown = await Assert.ThrowsAsync<CosmosException>(() =>
            RideAcceptWriteGuard.ExecuteAsync(
                () => Task.FromException<string>(failure),
                token =>
                {
                    released = true;
                    releaseToken = token;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.True(released);
        Assert.False(releaseToken.CanBeCanceled);
    }

    [Fact]
    public async Task OtherProviderFailureTakesPrecedenceOverCancellationWithoutReleasingReservation()
    {
        using var cancellation = new CancellationTokenSource();
        var released = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RideAcceptWriteGuard.ExecuteAsync(
                () =>
                {
                    cancellation.Cancel();
                    return Task.FromException<string>(new InvalidOperationException("provider unavailable"));
                },
                _ =>
                {
                    released = true;
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.False(released);
    }

    [Fact]
    public async Task SuccessfulCommittedWriteRemainsAuthoritativeWhenCancellationArrivesAfterCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var released = false;

        var result = await RideAcceptWriteGuard.ExecuteAsync(
            () =>
            {
                cancellation.Cancel();
                return Task.FromResult("accepted");
            },
            _ =>
            {
                released = true;
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.Equal("accepted", result);
        Assert.False(released);
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotStartWriteOrReleaseReservation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var writeCalled = false;
        var releaseCalled = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RideAcceptWriteGuard.ExecuteAsync(
                () =>
                {
                    writeCalled = true;
                    return Task.FromResult("accepted");
                },
                _ =>
                {
                    releaseCalled = true;
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.False(writeCalled);
        Assert.False(releaseCalled);
    }
}
