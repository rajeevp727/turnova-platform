namespace TurnOva.API;

using TurnOva.Domain.Dispatch;

public static class DriverProfileReviewGuard
{
    public static string? NormalizeRejectionReason(string? reason)
    {
        var normalized = reason?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 300
            ? null
            : normalized;
    }

    public static bool HasOperationsReviewMetadata(DriverOnboardingProfile profile) =>
        // Presence, not non-whitespace content, marks an attempted Operations review.
        // Empty/corrupt marker values must fail closed rather than be silently cleared.
        profile.ProfilePhotoOperationsReviewActorId is not null ||
        profile.ProfilePhotoOperationsReviewUnionId is not null ||
        profile.ProfilePhotoOperationsReviewUnionName is not null;

    public static bool HasCompleteOperationsReviewSnapshot(DriverOnboardingProfile profile) =>
        profile.ProfilePhotoApprovalStatus == "Rejected" &&
        profile.ProfilePhotoApprovedByUnionId is null &&
        profile.ProfilePhotoReviewedAtUtc is not null &&
        !string.IsNullOrWhiteSpace(profile.ProfilePhotoReviewReason) &&
        !string.IsNullOrWhiteSpace(profile.ProfilePhotoBase64) &&
        !string.IsNullOrWhiteSpace(profile.ProfilePhotoOperationsReviewActorId) &&
        !string.IsNullOrWhiteSpace(profile.ProfilePhotoOperationsReviewUnionId) &&
        !string.IsNullOrWhiteSpace(profile.ProfilePhotoOperationsReviewUnionName);

    public static bool HasUnionReviewMetadata(DriverOnboardingProfile profile) =>
        profile.ProfilePhotoUnionReviewActorId is not null ||
        profile.ProfilePhotoUnionReviewUnionId is not null ||
        profile.ProfilePhotoUnionReviewUnionName is not null;

    public static bool HasConflictingReviewMetadata(DriverOnboardingProfile profile) =>
        HasOperationsReviewMetadata(profile) && HasUnionReviewMetadata(profile);

    public static bool HasLegacyReviewedSelfieWithoutRecoverySnapshot(DriverOnboardingProfile profile) =>
        !HasOperationsReviewMetadata(profile) &&
        !HasUnionReviewMetadata(profile) &&
        profile.ProfilePhotoReviewedAtUtc is not null &&
        profile.ProfilePhotoApprovalStatus is "Approved" or "Rejected";

    public static bool HasCompleteUnionReviewSnapshot(DriverOnboardingProfile profile)
    {
        if (profile.ProfilePhotoApprovalStatus is not ("Approved" or "Rejected") ||
            profile.ProfilePhotoReviewedAtUtc is null ||
            string.IsNullOrWhiteSpace(profile.ProfilePhotoBase64) ||
            string.IsNullOrWhiteSpace(profile.ProfilePhotoUnionReviewActorId) ||
            string.IsNullOrWhiteSpace(profile.ProfilePhotoUnionReviewUnionId) ||
            string.IsNullOrWhiteSpace(profile.ProfilePhotoUnionReviewUnionName) ||
            profile.ProfilePhotoOperationsReviewActorId is not null ||
            profile.ProfilePhotoOperationsReviewUnionId is not null ||
            profile.ProfilePhotoOperationsReviewUnionName is not null)
            return false;

        return profile.ProfilePhotoApprovalStatus == "Approved"
            ? string.Equals(profile.ProfilePhotoApprovedByUnionId, profile.ProfilePhotoUnionReviewUnionId, StringComparison.Ordinal) &&
              profile.ProfilePhotoReviewReason is null
            : profile.ProfilePhotoApprovedByUnionId is null &&
              !string.IsNullOrWhiteSpace(profile.ProfilePhotoReviewReason);
    }

    public static bool IsSameUnionSelfieReview(
        DriverOnboardingProfile profile,
        string status,
        string unionId,
        string? reason) =>
        HasCompleteUnionReviewSnapshot(profile) &&
        string.Equals(profile.ProfilePhotoApprovalStatus, status, StringComparison.Ordinal) &&
        string.Equals(profile.ProfilePhotoUnionReviewUnionId, unionId, StringComparison.Ordinal) &&
        string.Equals(profile.ProfilePhotoReviewReason, reason, StringComparison.Ordinal) &&
        string.Equals(profile.ProfilePhotoApprovedByUnionId,
            status == "Approved" ? unionId : null, StringComparison.Ordinal);

    public static bool IsSameRejectedSelfieReview(
        DriverOnboardingProfile current,
        DriverOnboardingProfile reviewedSnapshot,
        string reason,
        string? expectedUnionId = null)
    {
        return HasCompleteOperationsReviewSnapshot(current) &&
            current.Id == reviewedSnapshot.Id &&
            current.DriverId == reviewedSnapshot.DriverId &&
            current.ProfilePhotoBase64 == reviewedSnapshot.ProfilePhotoBase64 &&
            current.ProfilePhotoCapturedAtUtc == reviewedSnapshot.ProfilePhotoCapturedAtUtc &&
            string.Equals(current.ProfilePhotoReviewReason, reason, StringComparison.Ordinal) &&
            (expectedUnionId is null ||
                string.Equals(current.ProfilePhotoOperationsReviewUnionId, expectedUnionId, StringComparison.Ordinal));
    }

    public static bool Matches(DriverOnboardingProfile current, DriverOnboardingProfile reviewedSnapshot)
    {
        return current.Id == reviewedSnapshot.Id &&
            current.DriverId == reviewedSnapshot.DriverId &&
            current.ZoneId == reviewedSnapshot.ZoneId &&
            current.FullName == reviewedSnapshot.FullName &&
            current.PhoneNumber == reviewedSnapshot.PhoneNumber &&
            current.VehicleMake == reviewedSnapshot.VehicleMake &&
            current.VehicleModel == reviewedSnapshot.VehicleModel &&
            current.VehicleRegistration == reviewedSnapshot.VehicleRegistration &&
            current.ServiceClass == reviewedSnapshot.ServiceClass &&
            current.PassengerCapacity == reviewedSnapshot.PassengerCapacity &&
            current.Status == reviewedSnapshot.Status &&
            current.SubmittedAtUtc == reviewedSnapshot.SubmittedAtUtc &&
            current.ReviewedAtUtc == reviewedSnapshot.ReviewedAtUtc &&
            current.ReviewReason == reviewedSnapshot.ReviewReason &&
            current.Documents is not null &&
            reviewedSnapshot.Documents is not null &&
            current.Documents.SequenceEqual(reviewedSnapshot.Documents);
    }
}
