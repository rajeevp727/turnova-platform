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

    public static string CreateAssignmentEventId(
        string driverId,
        string? previousUnionId,
        string? previousUnionName,
        string newUnionId,
        string newUnionName,
        string eventType,
        string reason,
        string actor,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newUnionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newUnionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 300)
            throw new ArgumentOutOfRangeException(nameof(reason), "Union assignment reasons cannot exceed 300 characters.");
        if (eventType is not ("Assigned" or "Transferred"))
            throw new ArgumentOutOfRangeException(nameof(eventType), "Only Union assignment and transfer events have this identity.");
        if (!DriverUnionAuditActor.IsSafeOpaqueActor(actor))
            throw new ArgumentException("Union assignment audit actors must be safe opaque identifiers.", nameof(actor));
        if (eventType == "Assigned" && (previousUnionId is not null || previousUnionName is not null))
            throw new ArgumentException("An initial assignment cannot include a previous Union.", nameof(previousUnionId));
        if (eventType == "Transferred" &&
            (string.IsNullOrWhiteSpace(previousUnionId) || string.IsNullOrWhiteSpace(previousUnionName)))
            throw new ArgumentException("A transfer must include the previous Union identity.", nameof(previousUnionId));

        var material = new StringBuilder("turnova-union-assignment-v1;");
        AppendField(material, driverId);
        AppendField(material, previousUnionId ?? "");
        AppendField(material, previousUnionName ?? "");
        AppendField(material, newUnionId);
        AppendField(material, newUnionName);
        AppendField(material, eventType);
        AppendField(material, reason);
        AppendField(material, actor);
        AppendField(material, occurredAtUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    public static string CreateAssignmentOperationKey(string driverId, string newUnionId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newUnionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 300)
            throw new ArgumentOutOfRangeException(nameof(reason), "Union assignment reasons cannot exceed 300 characters.");

        var material = new StringBuilder("turnova-union-assignment-operation-v1;");
        AppendField(material, driverId);
        AppendField(material, newUnionId);
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
    public static string Resolve(ClaimsPrincipal? principal) =>
        ResolveForRole(principal, "Ops", "Operations");

    public static string ResolveUnionManager(ClaimsPrincipal? principal) =>
        ResolveForRole(principal, "UnionManager", "UnionManager");

    public static bool IsSafeOpaqueActor(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 128 || subject.Any(char.IsControl))
            return false;

        var containsNonIdentifierCharacters = subject.Any(character =>
            !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':'));
        var containsOnlyPhoneCharacters = subject.All(character =>
            char.IsDigit(character) || character is '-' or '_' or '.' or ':');
        var phoneDigitCount = subject.Count(char.IsDigit);
        var looksLikePhoneNumber = containsOnlyPhoneCharacters && phoneDigitCount >= 8;
        return !containsNonIdentifierCharacters && !looksLikePhoneNumber;
    }

    private static string ResolveForRole(ClaimsPrincipal? principal, string requiredRole, string fallback)
    {
        if (principal?.Identity?.IsAuthenticated != true || !principal.IsInRole(requiredRole))
            return fallback;

        var subject = principal.FindFirst("sub")?.Value?.Trim();
        return IsSafeOpaqueActor(subject) ? subject! : fallback;
    }
}

public static class DriverUnionAssignmentAuditSnapshot
{
    public static DriverUnionAuditEvent? CreateExpectedEvent(DriverUnionAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var hasRecoveryMetadata = assignment.AuditEventId is not null ||
            assignment.LastAssignmentOperationKey is not null ||
            assignment.AuditSnapshotPending ||
            assignment.AuditEventType is not null ||
            assignment.AuditActor is not null ||
            assignment.AuditPreviousUnionId is not null ||
            assignment.AuditPreviousUnionName is not null ||
            assignment.AuditOccurredAtUtc is not null;
        if (!hasRecoveryMetadata) return null;

        if (string.IsNullOrWhiteSpace(assignment.DriverId) ||
            string.IsNullOrWhiteSpace(assignment.UnionId) ||
            string.IsNullOrWhiteSpace(assignment.AssignmentReason) ||
            assignment.AssignmentReason.Length > 300 ||
            string.IsNullOrWhiteSpace(assignment.AuditEventId) ||
            assignment.AuditEventId.Length != 64 ||
            assignment.AuditEventId.Any(character => !char.IsAsciiHexDigit(character)) ||
            string.IsNullOrWhiteSpace(assignment.LastAssignmentOperationKey) ||
            !string.Equals(
                assignment.LastAssignmentOperationKey,
                DriverUnionAuditIdentity.CreateAssignmentOperationKey(
                    assignment.DriverId, assignment.UnionId, assignment.AssignmentReason),
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The Union assignment operation identity is incomplete or does not match the canonical assignment fields.");

        var hasPrivateSnapshotFields = assignment.AuditEventType is not null ||
            assignment.AuditActor is not null ||
            assignment.AuditPreviousUnionId is not null ||
            assignment.AuditPreviousUnionName is not null ||
            assignment.AuditOccurredAtUtc is not null;

        if (!assignment.AuditSnapshotPending)
        {
            if (hasPrivateSnapshotFields)
                throw new InvalidOperationException(
                    "A completed Union assignment audit snapshot must not retain private recovery fields.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(assignment.AuditEventType) ||
            !DriverUnionAuditActor.IsSafeOpaqueActor(assignment.AuditActor) ||
            string.IsNullOrWhiteSpace(assignment.UnionName) ||
            assignment.AuditOccurredAtUtc is null)
            throw new InvalidOperationException(
                "The pending Union assignment audit snapshot is incomplete or contains an unsafe actor. Preserve the assignment and resolve its audit metadata before continuing.");

        if (assignment.AuditEventType == "Assigned" &&
            (assignment.AuditPreviousUnionId is not null || assignment.AuditPreviousUnionName is not null))
            throw new InvalidOperationException("An initial Union assignment audit snapshot cannot contain a previous Union.");
        if (assignment.AuditEventType == "Transferred" &&
            (string.IsNullOrWhiteSpace(assignment.AuditPreviousUnionId) ||
             string.IsNullOrWhiteSpace(assignment.AuditPreviousUnionName)))
            throw new InvalidOperationException("A Union transfer audit snapshot must retain its previous Union identity.");

        var expectedEventId = DriverUnionAuditIdentity.CreateAssignmentEventId(
            assignment.DriverId,
            assignment.AuditPreviousUnionId,
            assignment.AuditPreviousUnionName,
            assignment.UnionId,
            assignment.UnionName,
            assignment.AuditEventType,
            assignment.AssignmentReason,
            assignment.AuditActor!,
            assignment.AuditOccurredAtUtc.Value);
        if (!string.Equals(assignment.AuditEventId, expectedEventId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The Union assignment audit snapshot identity does not match its canonical fields. Refusing to reconstruct the event.");

        return new DriverUnionAuditEvent(
            assignment.AuditEventId,
            assignment.DriverId,
            assignment.AuditPreviousUnionId,
            assignment.AuditPreviousUnionName,
            assignment.UnionId,
            assignment.UnionName,
            assignment.AuditEventType,
            assignment.AssignmentReason,
            assignment.AuditActor!,
            assignment.AuditOccurredAtUtc.Value);
    }

    public static bool IsIdempotentRetry(DriverUnionAssignment assignment, string unionId, string reason)
    {
        _ = CreateExpectedEvent(assignment);
        return string.Equals(assignment.Status, "Active", StringComparison.Ordinal) &&
            string.Equals(assignment.UnionId, unionId, StringComparison.Ordinal) &&
            string.Equals(assignment.AssignmentReason, reason, StringComparison.Ordinal) &&
            string.Equals(
                assignment.LastAssignmentOperationKey,
                DriverUnionAuditIdentity.CreateAssignmentOperationKey(assignment.DriverId, unionId, reason),
                StringComparison.Ordinal);
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

        // The profile's private review snapshot carries the original actor across retries.
        // A canonical event with any different actor is therefore a mismatch and must fail closed.
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
