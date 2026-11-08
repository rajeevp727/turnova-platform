using TurnOva.API;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class DriverPayslipSnapshotRecoveryTests
{
    [Fact]
    public async Task CreateConflictReturnsCanonicalPersistedSnapshotImmediately()
    {
        var canonical = CreatePayslip("canonical", 4);
        var reads = 0;
        var delays = new List<TimeSpan>();

        var result = await DriverPayslipSnapshotRecovery.ReadCanonicalAfterCreateConflictAsync(
            _ =>
            {
                reads++;
                return Task.FromResult<DriverMonthlyPayslip?>(canonical);
            },
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Same(canonical, result);
        Assert.Equal(1, reads);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task CreateConflictRetriesCanonicalPointReadWhenSnapshotIsNotImmediatelyVisible()
    {
        var canonical = CreatePayslip("canonical-after-retry", 7);
        var reads = 0;
        var delays = new List<TimeSpan>();

        var result = await DriverPayslipSnapshotRecovery.ReadCanonicalAfterCreateConflictAsync(
            _ =>
            {
                reads++;
                return Task.FromResult<DriverMonthlyPayslip?>(reads < 3 ? null : canonical);
            },
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Same(canonical, result);
        Assert.Equal(3, reads);
        Assert.Equal(
            new[] { TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100) },
            delays);
    }

    [Fact]
    public async Task MissingCanonicalSnapshotFailsClosedInsteadOfReturningLocalCandidate()
    {
        var reads = 0;
        var delays = new List<TimeSpan>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DriverPayslipSnapshotRecovery.ReadCanonicalAfterCreateConflictAsync(
                _ =>
                {
                    reads++;
                    return Task.FromResult<DriverMonthlyPayslip?>(null);
                },
                (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }));

        Assert.Contains("Refusing to return locally calculated metrics", error.Message);
        Assert.Equal(3, reads);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task CanonicalReadFailureIsNotConvertedIntoAComputedSnapshot()
    {
        var reads = 0;

        await Assert.ThrowsAsync<IOException>(() =>
            DriverPayslipSnapshotRecovery.ReadCanonicalAfterCreateConflictAsync(_ =>
            {
                reads++;
                throw new IOException("Cosmos point read unavailable.");
            }));

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task CancellationDuringCanonicalReadRecoveryPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var reads = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DriverPayslipSnapshotRecovery.ReadCanonicalAfterCreateConflictAsync(
                _ =>
                {
                    reads++;
                    return Task.FromResult<DriverMonthlyPayslip?>(null);
                },
                (_, _) =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled(cancellation.Token);
                },
                cancellation.Token));

        Assert.Equal(1, reads);
    }

    private static DriverMonthlyPayslip CreatePayslip(string id, int completedDays) =>
        new(
            id, "driver-1", "Driver One", "union-1", "Union One", 2026, 10,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(5.5)),
            new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.FromHours(5.5)),
            completedDays, 8, 7, 1, DateTimeOffset.UtcNow);
}
