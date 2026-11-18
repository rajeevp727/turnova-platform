using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class RideLifecycleTests
{
    [Theory]
    [InlineData("Requested", "Accepted", true)]
    [InlineData("Negotiating", "AgreementConfirmed", true)]
    [InlineData("Negotiating", "Cancelled", true)]
    [InlineData("Negotiating", "Expired", true)]
    [InlineData("AgreementConfirmed", "Negotiating", true)]
    [InlineData("AgreementConfirmed", "Accepted", true)]
    [InlineData("AgreementConfirmed", "Cancelled", true)]
    [InlineData("AgreementConfirmed", "Expired", true)]
    [InlineData("Accepted", "Arrived", true)]
    [InlineData("Arrived", "InProgress", true)]
    [InlineData("InProgress", "Completed", true)]
    [InlineData("Requested", "Cancelled", true)]
    [InlineData("Accepted", "Cancelled", true)]
    [InlineData("Arrived", "Cancelled", true)]
    [InlineData("InProgress", "Cancelled", false)]
    [InlineData("Completed", "Cancelled", false)]
    [InlineData("Expired", "Accepted", false)]
    [InlineData("Cancelled", "Accepted", false)]
    [InlineData("Requested", "Completed", false)]
    [InlineData("Accepted", "InProgress", false)]
    public void CanTransition_EnforcesAllowedStateChanges(string current, string next, bool expected)
    {
        Assert.Equal(expected, RideLifecycle.CanTransition(current, next));
    }

    [Fact]
    public void AreCoordinatesValid_RejectsNonFiniteAndOutOfRangeValues()
    {
        Assert.True(RideLifecycle.AreCoordinatesValid(0, 0));
        Assert.True(RideLifecycle.AreCoordinatesValid(90, 180));
        Assert.True(RideLifecycle.AreCoordinatesValid(-90, -180));
        Assert.False(RideLifecycle.AreCoordinatesValid(double.NaN, 0));
        Assert.False(RideLifecycle.AreCoordinatesValid(0, double.PositiveInfinity));
        Assert.False(RideLifecycle.AreCoordinatesValid(double.NegativeInfinity, 0));
        Assert.False(RideLifecycle.AreCoordinatesValid(90.0001, 0));
        Assert.False(RideLifecycle.AreCoordinatesValid(0, -180.0001));
    }

    [Theory]
    [InlineData("Requested", false)]
    [InlineData("Negotiating", false)]
    [InlineData("AgreementConfirmed", false)]
    [InlineData("Accepted", true)]
    [InlineData("Arrived", true)]
    [InlineData("InProgress", true)]
    [InlineData("Completed", false)]
    [InlineData("Cancelled", false)]
    [InlineData("Expired", false)]
    public void IsLocationSharingActive_StopsForTerminalAndUnassignedRides(string status, bool expected)
    {
        Assert.Equal(expected, RideLifecycle.IsLocationSharingActive(status));
    }
}
