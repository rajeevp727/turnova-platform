namespace TurnOva.Domain.Dispatch;

public sealed record DriverWorkActivity(
    string ActivityId,
    string DriverId,
    string RideId,
    string ActivityType,
    DateTimeOffset OccurredAtUtc)
{
    public string id => ActivityId;
}

public sealed record DriverMonthlyPayslip(
    string PayslipId,
    string DriverId,
    string DriverName,
    string UnionId,
    string UnionName,
    int Year,
    int Month,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    int DaysWorked,
    int RidesAccepted,
    int RidesCompleted,
    int RidesRejected,
    DateTimeOffset GeneratedAtUtc)
{
    public string id => PayslipId;
}