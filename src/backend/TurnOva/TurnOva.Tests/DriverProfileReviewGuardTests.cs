using TurnOva.API;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class DriverProfileReviewGuardTests
{
    [Fact]
    public void ChangedDocumentReferenceOrStatusRejectsStaleReviewSnapshot()
    {
        var snapshot = CreateProfile();
        var changedReference = snapshot with
        {
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", "private/licence-v2"),
                new DriverDocumentReference("VehicleRegistration", "private/vehicle-registration")
            ]
        };
        var changedDocumentStatus = snapshot with
        {
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", "private/licence", "Approved"),
                new DriverDocumentReference("VehicleRegistration", "private/vehicle-registration")
            ]
        };

        Assert.False(DriverProfileReviewGuard.Matches(changedReference, snapshot));
        Assert.False(DriverProfileReviewGuard.Matches(changedDocumentStatus, snapshot));
    }

    [Fact]
    public void ChangedVehicleIdentityOrReviewStatusRejectsStaleReviewSnapshot()
    {
        var snapshot = CreateProfile();

        Assert.False(DriverProfileReviewGuard.Matches(
            snapshot with { VehicleRegistration = "TS09AB9999" }, snapshot));
        Assert.False(DriverProfileReviewGuard.Matches(
            snapshot with { Status = "Approved" }, snapshot));
    }

    [Fact]
    public void UnrelatedAvailabilityAndLocationChangesDoNotInvalidateDocumentReviewSnapshot()
    {
        var snapshot = CreateProfile();
        var latest = snapshot with
        {
            AvailabilityStatus = "Offline",
            CurrentLatitude = 17.4,
            CurrentLongitude = 78.4,
            LocationUpdatedAtUtc = DateTimeOffset.UtcNow
        };

        Assert.True(DriverProfileReviewGuard.Matches(latest, snapshot));
    }

    [Theory]
    [InlineData("  selfie does not meet policy  ", "selfie does not meet policy")]
    [InlineData("Reason", "Reason")]
    public void RejectionReasonIsTrimmedToOneStableValue(string supplied, string expected)
    {
        Assert.Equal(expected, DriverProfileReviewGuard.NormalizeRejectionReason(supplied));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectionReasonRejectsNullOrWhitespace(string? supplied)
    {
        Assert.Null(DriverProfileReviewGuard.NormalizeRejectionReason(supplied));
    }

    [Fact]
    public void RejectionReasonRejectsMoreThanThreeHundredCharactersAndAcceptsTheLimit()
    {
        Assert.Null(DriverProfileReviewGuard.NormalizeRejectionReason(new string('x', 301)));
        Assert.Equal(new string('x', 300),
            DriverProfileReviewGuard.NormalizeRejectionReason(new string('x', 300)));
    }

    [Theory]
    [InlineData("Contact operator@example.com for details")]
    [InlineData("Call 123-456-7890 for follow-up")]
    [InlineData("Use +91 98765 43210 for clarification")]
    public void UnionAuditReasonsRejectObviousContactDetails(string supplied)
    {
        Assert.Null(DriverProfileReviewGuard.NormalizeRejectionReason(supplied));
        Assert.Null(DriverUnionAuditReason.Normalize(supplied));
    }

    [Fact]
    public void UnionAuditReasonAllowsOrdinaryDatesAndNonContactText()
    {
        Assert.Equal("Reviewed on 2026-11-13",
            DriverUnionAuditReason.Normalize(" Reviewed on 2026-11-13 "));
    }

    [Fact]
    public void OperationsRetryRequiresAnOperationsReviewMarker()
    {
        const string reason = "Image is unclear";
        var unionManagerRejection = CreateProfile() with
        {
            ProfilePhotoBase64 = "selfie",
            ProfilePhotoCapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoReviewedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ProfilePhotoReviewReason = reason,
            ProfilePhotoOperationsReviewActorId = null
        };
        var operationsRejection = unionManagerRejection with
        {
            ProfilePhotoOperationsReviewActorId = "ops-actor-42",
            ProfilePhotoOperationsReviewUnionId = "union-1",
            ProfilePhotoOperationsReviewUnionName = "Test Union"
        };

        Assert.False(DriverProfileReviewGuard.IsSameRejectedSelfieReview(
            unionManagerRejection, unionManagerRejection, reason));
        Assert.True(DriverProfileReviewGuard.IsSameRejectedSelfieReview(
            operationsRejection, operationsRejection, reason));
    }

    [Fact]
    public void OperationsReviewRecoveryRequiresCompletePrivateSnapshot()
    {
        var incomplete = CreateProfile() with
        {
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoReviewedAtUtc = DateTimeOffset.UtcNow,
            ProfilePhotoReviewReason = "Image is unclear",
            ProfilePhotoOperationsReviewActorId = "ops-actor"
        };
        var complete = incomplete with
        {
            ProfilePhotoBase64 = "selfie",
            ProfilePhotoOperationsReviewUnionId = "union-1",
            ProfilePhotoOperationsReviewUnionName = "Test Union"
        };

        Assert.True(DriverProfileReviewGuard.HasOperationsReviewMetadata(incomplete));
        Assert.False(DriverProfileReviewGuard.HasCompleteOperationsReviewSnapshot(incomplete));
        Assert.True(DriverProfileReviewGuard.HasCompleteOperationsReviewSnapshot(complete));
    }

    [Fact]
    public void UnionReviewSnapshotRequiresOriginalActorUnionAndReviewTimestamp()
    {
        var incomplete = CreateProfile() with
        {
            ProfilePhotoBase64 = "selfie",
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoReviewedAtUtc = DateTimeOffset.Parse("2026-11-12T10:00:00Z"),
            ProfilePhotoReviewReason = "Image is unclear",
            ProfilePhotoUnionReviewActorId = "manager-original",
            ProfilePhotoUnionReviewUnionId = "union-1"
        };
        var complete = incomplete with { ProfilePhotoUnionReviewUnionName = "Test Union" };

        Assert.True(DriverProfileReviewGuard.HasUnionReviewMetadata(incomplete));
        Assert.False(DriverProfileReviewGuard.HasCompleteUnionReviewSnapshot(incomplete));
        Assert.True(DriverProfileReviewGuard.HasCompleteUnionReviewSnapshot(complete));
        Assert.True(DriverProfileReviewGuard.IsSameUnionSelfieReview(
            complete, "Rejected", "union-1", "Image is unclear"));
        Assert.False(DriverProfileReviewGuard.IsSameUnionSelfieReview(
            complete, "Rejected", "another-union", "Image is unclear"));
        Assert.False(DriverProfileReviewGuard.IsSameUnionSelfieReview(
            complete, "Rejected", "union-1", "Different reason"));
    }

    [Fact]
    public void LegacyReviewedSelfieWithoutRecoverySnapshotIsDetected()
    {
        var legacy = CreateProfile() with
        {
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoReviewedAtUtc = DateTimeOffset.Parse("2026-11-12T10:00:00Z"),
            ProfilePhotoReviewReason = "Image is unclear"
        };

        Assert.True(DriverProfileReviewGuard.HasLegacyReviewedSelfieWithoutRecoverySnapshot(legacy));
        Assert.False(DriverProfileReviewGuard.HasLegacyReviewedSelfieWithoutRecoverySnapshot(
            legacy with { ProfilePhotoUnionReviewActorId = "manager-original" }));
    }

    [Fact]
    public void BlankButPresentUnionReviewMarkersAreTreatedAsIncompleteMetadata()
    {
        var profile = CreateProfile() with
        {
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoReviewedAtUtc = DateTimeOffset.UtcNow,
            ProfilePhotoReviewReason = "Image is unclear",
            ProfilePhotoUnionReviewActorId = "",
            ProfilePhotoUnionReviewUnionId = "",
            ProfilePhotoUnionReviewUnionName = ""
        };

        Assert.True(DriverProfileReviewGuard.HasUnionReviewMetadata(profile));
        Assert.False(DriverProfileReviewGuard.HasCompleteUnionReviewSnapshot(profile));
    }

    [Fact]
    public void BlankButPresentOperationsReviewMarkersAreTreatedAsIncompleteMetadata()
    {
        var profile = CreateProfile() with
        {
            ProfilePhotoBase64 = "selfie",
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoReviewedAtUtc = DateTimeOffset.UtcNow,
            ProfilePhotoReviewReason = "Image is unclear",
            ProfilePhotoOperationsReviewActorId = "",
            ProfilePhotoOperationsReviewUnionId = "",
            ProfilePhotoOperationsReviewUnionName = ""
        };

        Assert.True(DriverProfileReviewGuard.HasOperationsReviewMetadata(profile));
        Assert.False(DriverProfileReviewGuard.HasCompleteOperationsReviewSnapshot(profile));
    }

    [Fact]
    public void SameOperationsRetryMustBelongToTheExpectedCurrentUnion()
    {
        var reviewed = CreateProfile() with
        {
            ProfilePhotoBase64 = "selfie",
            ProfilePhotoCapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoReviewedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ProfilePhotoReviewReason = "Image is unclear",
            ProfilePhotoOperationsReviewActorId = "ops-actor-42",
            ProfilePhotoOperationsReviewUnionId = "union-1",
            ProfilePhotoOperationsReviewUnionName = "Test Union"
        };

        Assert.True(DriverProfileReviewGuard.IsSameRejectedSelfieReview(
            reviewed, reviewed, "Image is unclear", "union-1"));
        Assert.False(DriverProfileReviewGuard.IsSameRejectedSelfieReview(
            reviewed, reviewed, "Image is unclear", "another-union"));
    }

    private static DriverOnboardingProfile CreateProfile() => new(
        "driver-1", "driver-1", "zone-1", "Test Driver", "+919876543210",
        "Maruti", "Dzire", "TS09AB1234", "cab", 3,
        [
            new DriverDocumentReference("DrivingLicence", "private/licence"),
            new DriverDocumentReference("VehicleRegistration", "private/vehicle-registration")
        ],
        "Pending", new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero));
}
