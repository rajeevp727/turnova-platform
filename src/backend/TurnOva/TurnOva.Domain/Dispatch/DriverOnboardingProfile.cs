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
    DateTimeOffset? ProfilePhotoCapturedAtUtc = null);
