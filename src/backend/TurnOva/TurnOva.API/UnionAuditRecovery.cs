using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TurnOva.Domain.Dispatch;

namespace TurnOva.API;

public static class DriverUnionAuditIdentity
{
    public static string CreateSelfieRejectionEventId(
        string driverId,
        string unionId,
        DateTimeOffset? selfieCapturedAtUtc,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(unionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var capturedAt = selfieCapturedAtUtc?.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture)
            ?? "legacy";
        var material = new StringBuilder("turnova-union-selfie-rejection-v1;");
        AppendField(material, driverId);
        AppendField(material, unionId);
        AppendField(material, capturedAt);
        AppendField(material, reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    private static void AppendField(StringBuilder target, string value) =>
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');
}

public static class DriverUnionAuditActor
{
    public static string Resolve(ClaimsPrincipal? principal)
    {
        var subject = principal?.FindFirst("sub")?.Value?.Trim();
        return !string.IsNullOrWhiteSpace(subject) && subject.Length <= 128
            ? subject
            : "Operations";
    }
}

public static class DriverUnionAuditSnapshotRecovery
{
    private const int MaximumReadAttempts = 3;

    public static bool Matches(DriverUnionAuditEvent expected, DriverUnionAuditEvent actual) =>
        string.Equals(expected.EventId, actual.EventId, StringComparison.Ordinal) &&
        string.Equals(expected.DriverId, actual.DriverId, StringComparison.Ordinal) &&
        string.Equals(expected.PreviousUnionId, actual.PreviousUnionId, StringComparison.Ordinal) &&
        string.Equals(expected.PreviousUnionName, actual.PreviousUnionName, StringComparison.Ordinal) &&
        string.Equals(expected.NewUnionId, actual.NewUnionId, StringComparison.Ordinal) &&
        string.Equals(expected.NewUnionName, actual.NewUnionName, StringComparison.Ordinal) &&
        string.Equals(expected.EventType, actual.EventType, StringComparison.Ordinal) &&
        string.Equals(expected.Reason, actual.Reason, StringComparison.Ordinal) &&
        string.Equals(expected.Actor, actual.Actor, StringComparison.Ordinal) &&
        expected.OccurredAtUtc == actual.OccurredAtUtc;

    public static async Task EnsureCanonicalAsync(
        DriverUnionAuditEvent expected,
        Func<string, string, CancellationToken, Task<DriverUnionAuditEvent?>> readCanonical,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(readCanonical);
        delayAsync ??= Task.Delay;

        for (var attempt = 0; attempt < MaximumReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var canonical = await readCanonical(expected.DriverId, expected.EventId, cancellationToken);
            if (canonical is not null)
            {
                if (!Matches(expected, canonical))
                {
                    throw new InvalidOperationException(
                        "A Union audit event already exists for this deterministic identity, but its persisted content differs. Refusing to treat the write as successful.");
                }

                return;
            }

            if (attempt < MaximumReadAttempts - 1)
                await delayAsync(TimeSpan.FromMilliseconds(50 * (attempt + 1)), cancellationToken);
        }

        throw new InvalidOperationException(
            "A Union audit event create conflicted, but its canonical persisted event could not be read. Refusing to report the audit write as successful.");
    }
}
