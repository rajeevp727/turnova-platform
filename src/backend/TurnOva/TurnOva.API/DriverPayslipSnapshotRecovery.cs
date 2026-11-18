using TurnOva.Domain.Dispatch;

namespace TurnOva.API;

public static class DriverPayslipSnapshotRecovery
{
    private const int MaximumReadAttempts = 3;

    public static async Task<DriverMonthlyPayslip> ReadCanonicalAfterCreateConflictAsync(
        Func<CancellationToken, Task<DriverMonthlyPayslip?>> readCanonical,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readCanonical);
        delayAsync ??= Task.Delay;

        for (var attempt = 0; attempt < MaximumReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var canonical = await readCanonical(cancellationToken);
            if (canonical is not null)
                return canonical;

            if (attempt < MaximumReadAttempts - 1)
                await delayAsync(TimeSpan.FromMilliseconds(50 * (attempt + 1)), cancellationToken);
        }

        throw new InvalidOperationException(
            "A monthly payslip already exists, but its canonical persisted snapshot could not be read. " +
            "Refusing to return locally calculated metrics.");
    }
}
