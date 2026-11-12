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
        DateTimeOffset reviewedAtUtc,
        string selfieContent,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(unionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(selfieContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var capturedAt = selfieCapturedAtUtc?.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture)
            ?? "legacy";
        var reviewedAt = reviewedAtUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture);
        var selfieDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selfieContent)));
        var material = new StringBuilder("turnova-union-selfie-rejection-v1;");
        AppendField(material, driverId);
        AppendField(material, unionId);
        AppendField(material, capturedAt);
        AppendField(material, reviewedAt);
        AppendField(material, selfieDigest);
        AppendField(material, reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    public static string CreateUnionSelfieReviewEventId(
        string driverId,
        string unionId,
        string eventType,
        DateTimeOffset reviewedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(unionId);
        if (eventType is not ("SelfieApprovedByUnion" or "SelfieRejectedByUnion"))
            throw new ArgumentOutOfRangeException(nameof(eventType), "Only Union selfie review events have this identity.");

        var material = new StringBuilder("turnova-union-selfie-review-v1;");
        AppendField(material, driverId);
        AppendField(material, unionId);
        AppendField(material, eventType);
        AppendField(material, reviewedAtUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));
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
    public static string Resolve(ClaimsPrincipal? principal) =>
        ResolveForRole(principal, "Ops", "Operations");

    public static string ResolveUnionManager(ClaimsPrincipal? principal) =>
        ResolveForRole(principal, "UnionManager", "UnionManager");

    private static string ResolveForRole(ClaimsPrincipal? principal, string requiredRole, string fallback)
    {
        if (principal?.Identity?.IsAuthenticated != true || !principal.IsInRole(requiredRole))
            return fallback;

        var subject = principal.FindFirst("sub")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 128 || subject.Any(char.IsControl))
            return fallback;

        var containsNonIdentifierCharacters = subject.Any(character =>
            !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':'));
        var containsOnlyPhoneCharacters = subject.All(character =>
            char.IsDigit(character) || character is '-' or '_' or '.' or ':');
        var phoneDigitCount = subject.Count(char.IsDigit);
        var looksLikePhoneNumber = containsOnlyPhoneCharacters && phoneDigitCount >= 8;
        return containsNonIdentifierCharacters || looksLikePhoneNumber
            ? fallback
            : subject;
    }
}

public static class DriverUnionAuditSnapshotRecovery
{
    private const int MaximumReadAttempts = 3;

    public static bool Matches(DriverUnionAuditEvent expected, DriverUnionAuditEvent actual)
    {
        var sameEvent = string.Equals(expected.EventId, actual.EventId, StringComparison.Ordinal) &&
            string.Equals(expected.DriverId, actual.DriverId, StringComparison.Ordinal) &&
            string.Equals(expected.PreviousUnionId, actual.PreviousUnionId, StringComparison.Ordinal) &&
            string.Equals(expected.PreviousUnionName, actual.PreviousUnionName, StringComparison.Ordinal) &&
            string.Equals(expected.NewUnionId, actual.NewUnionId, StringComparison.Ordinal) &&
            string.Equals(expected.NewUnionName, actual.NewUnionName, StringComparison.Ordinal) &&
            string.Equals(expected.EventType, actual.EventType, StringComparison.Ordinal) &&
            string.Equals(expected.Reason, actual.Reason, StringComparison.Ordinal) &&
            expected.OccurredAtUtc == actual.OccurredAtUtc;
        if (!sameEvent) return false;

        // UnionManager review identity is the review itself, not the manager who retries it.
        // Preserve the first committed actor when another authorised manager repeats the same review.
        if (expected.EventType is "SelfieApprovedByUnion" or "SelfieRejectedByUnion")
            return !string.IsNullOrWhiteSpace(actual.Actor);

        return string.Equals(expected.Actor, actual.Actor, StringComparison.Ordinal);
    }

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
