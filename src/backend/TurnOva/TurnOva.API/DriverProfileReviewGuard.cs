namespace TurnOva.API;

using TurnOva.Domain.Dispatch;

public static class DriverProfileReviewGuard
{
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
