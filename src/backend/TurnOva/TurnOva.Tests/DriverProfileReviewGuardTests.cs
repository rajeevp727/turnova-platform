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
            ProfilePhotoOperationsReviewActorId = "ops-actor-42"
        };

        Assert.False(DriverProfileReviewGuard.IsSameRejectedSelfieReview(
            unionManagerRejection, unionManagerRejection, reason));
        Assert.True(DriverProfileReviewGuard.IsSameRejectedSelfieReview(
            operationsRejection, operationsRejection, reason));
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
