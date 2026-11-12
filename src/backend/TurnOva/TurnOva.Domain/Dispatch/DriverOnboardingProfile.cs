namespace TurnOva.Domain.Dispatch;

public sealed record DriverDocumentReference(
    string DocumentType,
    string StorageReference,
    string Status = "Pending");

public sealed record DriverOnboardingProfile(
    string Id,
    string DriverId,
    string ZoneId,
    string FullName,
    string PhoneNumber,
    string VehicleMake,
    string VehicleModel,
    string VehicleRegistration,
    string ServiceClass,
    int PassengerCapacity,
    IReadOnlyList<DriverDocumentReference> Documents,
    string Status,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? ReviewedAtUtc = null,
    string? ReviewReason = null,
    string? ProfilePhotoBase64 = null,
    string? ProfilePhotoContentType = null,
    DateTimeOffset? ProfilePhotoCapturedAtUtc = null,
    string ProfilePhotoApprovalStatus = "Pending",
    string? ProfilePhotoApprovedByUnionId = null,
    DateTimeOffset? ProfilePhotoReviewedAtUtc = null,
    string? ProfilePhotoReviewReason = null,
    string AvailabilityStatus = "Offline",
    double? CurrentLatitude = null,
    double? CurrentLongitude = null,
    DateTimeOffset? LocationUpdatedAtUtc = null,
    // Monotonic GPS capture watermark; retained while Offline to reject delayed fixes.
    DateTimeOffset? LocationCapturedAtUtc = null,
    // Private recovery metadata; API driver responses explicitly redact this actor identity.
    string? ProfilePhotoOperationsReviewActorId = null,
    // Private canonical audit context, cleared whenever the reviewed selfie/membership changes.
    string? ProfilePhotoOperationsReviewUnionId = null,
    string? ProfilePhotoOperationsReviewUnionName = null,
    // Private UnionManager review snapshot used only to recover the original audit actor after a partial write.
    string? ProfilePhotoUnionReviewActorId = null,
    string? ProfilePhotoUnionReviewUnionId = null,
    string? ProfilePhotoUnionReviewUnionName = null);
