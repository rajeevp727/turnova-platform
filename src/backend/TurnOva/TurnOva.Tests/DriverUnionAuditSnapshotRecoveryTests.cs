using System.Security.Claims;
using TurnOva.API;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class DriverUnionAuditSnapshotRecoveryTests
{
    [Fact]
    public void SelfieRejectionEventIdentityIsStableAndDoesNotExposeInputs()
    {
        var capturedAt = new DateTimeOffset(2026, 11, 12, 10, 30, 0, TimeSpan.FromHours(5.5));

        var reviewedAt = new DateTimeOffset(2026, 11, 12, 11, 0, 0, TimeSpan.FromHours(5.5));
        var first = DriverUnionAuditIdentity.CreateSelfieRejectionEventId(
            "driver-private-123", "union-private-456", capturedAt, reviewedAt, "private-selfie-base64", "Image is unclear");
        var retry = DriverUnionAuditIdentity.CreateSelfieRejectionEventId(
            "driver-private-123", "union-private-456", capturedAt.ToUniversalTime(), reviewedAt.ToUniversalTime(),
            "private-selfie-base64", "Image is unclear");

        Assert.Equal(first, retry);
        Assert.Equal(64, first.Length);
        Assert.False(first.Contains("driver-private", StringComparison.OrdinalIgnoreCase));
        Assert.False(first.Contains("union-private", StringComparison.OrdinalIgnoreCase));
        Assert.False(first.Contains("private-selfie-base64", StringComparison.Ordinal));
        Assert.False(first.Contains("Image is unclear", StringComparison.Ordinal));
    }

    [Fact]
    public void SelfieRejectionEventIdentityChangesWhenPhotoOrReasonOrReviewTimestampChanges()
    {
        var capturedAt = DateTimeOffset.Parse("2026-11-12T10:30:00+05:30");
        var reviewedAt = DateTimeOffset.Parse("2026-11-12T11:00:00Z");
        var original = DriverUnionAuditIdentity.CreateSelfieRejectionEventId(
            "driver-1", "union-1", capturedAt, reviewedAt, "selfie-a", "Image is unclear");
        var differentPhoto = DriverUnionAuditIdentity.CreateSelfieRejectionEventId(
            "driver-1", "union-1", capturedAt.AddSeconds(1), reviewedAt, "selfie-a", "Image is unclear");
        var differentReason = DriverUnionAuditIdentity.CreateSelfieRejectionEventId(
            "driver-1", "union-1", capturedAt, reviewedAt, "selfie-a", "Registration is not visible");
        var differentSelfie = DriverUnionAuditIdentity.CreateSelfieRejectionEventId(
            "driver-1", "union-1", capturedAt, reviewedAt, "selfie-b", "Image is unclear");
        var differentReviewTimestamp = DriverUnionAuditIdentity.CreateSelfieRejectionEventId(
            "driver-1", "union-1", capturedAt, reviewedAt.AddSeconds(1), "selfie-a", "Image is unclear");

        Assert.NotEqual(original, differentPhoto);
        Assert.NotEqual(original, differentReason);
        Assert.NotEqual(original, differentSelfie);
        Assert.NotEqual(original, differentReviewTimestamp);
    }

    [Fact]
    public void AuditActorUsesOpaqueSubjectInsteadOfEmailLikeIdentityName()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "operator@example.com"),
            new Claim("sub", "ops-actor-opaque-42"),
            new Claim("role", "Ops")
        ], "test", ClaimTypes.Name, "role");
        var principal = new ClaimsPrincipal(identity);

        Assert.Equal("ops-actor-opaque-42", DriverUnionAuditActor.Resolve(principal));
    }

    [Theory]
    [InlineData("operator@example.com")]
    [InlineData("123-456-7890")]
    [InlineData("123456789012")]
    public void AuditActorFallsBackWhenSubjectLooksLikePersonalContactData(string subject)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", subject), new Claim("role", "Ops")], "test", ClaimTypes.Name, "role"));

        Assert.Equal("Operations", DriverUnionAuditActor.Resolve(principal));
    }

    [Fact]
    public void AuditActorFallsBackWhenSubjectPrincipalIsNotOperations()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "rider-actor-42"), new Claim("role", "Rider")],
            "test", ClaimTypes.Name, "role"));

        Assert.Equal("Operations", DriverUnionAuditActor.Resolve(principal));
    }

    [Fact]
    public void AuditActorFallsBackWhenOnlyEmailLikeNameIsAvailable()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "operator@example.com")], "test");

        Assert.Equal("Operations", DriverUnionAuditActor.Resolve(new ClaimsPrincipal(identity)));
        Assert.Equal("Operations", DriverUnionAuditActor.Resolve(new ClaimsPrincipal()));
        Assert.Equal("Operations", DriverUnionAuditActor.Resolve(null));
    }

    [Fact]
    public async Task ConflictRecoveryAcceptsAnExactCanonicalEvent()
    {
        var expected = CreateEvent();
        var reads = 0;

        await DriverUnionAuditSnapshotRecovery.EnsureCanonicalAsync(
            expected,
            (_, _, _) =>
            {
                reads++;
                return Task.FromResult<DriverUnionAuditEvent?>(expected);
            },
            static (_, _) => Task.CompletedTask);

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task ConflictRecoveryReadsByExactEventIdAndDriverPartition()
    {
        var expected = CreateEvent();
        string? readDriverId = null;
        string? readEventId = null;

        await DriverUnionAuditSnapshotRecovery.EnsureCanonicalAsync(
            expected,
            (driverId, eventId, _) =>
            {
                readDriverId = driverId;
                readEventId = eventId;
                return Task.FromResult<DriverUnionAuditEvent?>(expected);
            },
            static (_, _) => Task.CompletedTask);

        Assert.Equal(expected.DriverId, readDriverId);
        Assert.Equal(expected.EventId, readEventId);
    }

    [Fact]
    public async Task ConflictRecoveryFailsClosedWhenCanonicalActorDiffers()
    {
        var expected = CreateEvent();
        var mismatched = expected with { Actor = "different-operator-subject" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DriverUnionAuditSnapshotRecovery.EnsureCanonicalAsync(
                expected,
                (_, _, _) => Task.FromResult<DriverUnionAuditEvent?>(mismatched),
                static (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task ConflictRecoveryRetriesPointReadUntilCanonicalEventAppears()
    {
        var expected = CreateEvent();
        var reads = 0;
        var delays = 0;

        await DriverUnionAuditSnapshotRecovery.EnsureCanonicalAsync(
            expected,
            (_, _, _) => Task.FromResult<DriverUnionAuditEvent?>(
                ++reads == 1 ? null : expected),
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        Assert.Equal(2, reads);
        Assert.Equal(1, delays);
    }

    [Fact]
    public async Task ConflictRecoveryFailsClosedWhenCanonicalEventDiffers()
    {
        var expected = CreateEvent();
        var mismatched = expected with { Reason = "Different reason" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DriverUnionAuditSnapshotRecovery.EnsureCanonicalAsync(
                expected,
                (_, _, _) => Task.FromResult<DriverUnionAuditEvent?>(mismatched),
                static (_, _) => Task.CompletedTask));

        Assert.Contains("content differs", error.Message);
    }

    [Fact]
    public async Task ConflictRecoveryFailsWhenCanonicalEventNeverBecomesReadable()
    {
        var expected = CreateEvent();
        var reads = 0;
        var delays = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DriverUnionAuditSnapshotRecovery.EnsureCanonicalAsync(
                expected,
                (_, _, _) =>
                {
                    reads++;
                    return Task.FromResult<DriverUnionAuditEvent?>(null);
                },
                (_, _) =>
                {
                    delays++;
                    return Task.CompletedTask;
                }));

        Assert.Equal(3, reads);
        Assert.Equal(2, delays);
        Assert.Contains("could not be read", error.Message);
    }

    [Fact]
    public async Task ConflictRecoveryPropagatesCancellationBeforeReading()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reads = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DriverUnionAuditSnapshotRecovery.EnsureCanonicalAsync(
                CreateEvent(),
                (_, _, _) =>
                {
                    reads++;
                    return Task.FromResult<DriverUnionAuditEvent?>(null);
                },
                static (_, _) => Task.CompletedTask,
                cancellation.Token));

        Assert.Equal(0, reads);
    }

    private static DriverUnionAuditEvent CreateEvent() => new(
        "event-1",
        "driver-1",
        "union-previous",
        "Previous Union",
        "union-current",
        "Current Union",
        "SelfieRejectedByOperations",
        "Image is unclear",
        "Operations",
        DateTimeOffset.Parse("2026-11-12T10:30:00Z"));
}
