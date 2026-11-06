using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class RideLifecycleTests
{
    [Theory]
    [InlineData("Requested", "Accepted", true)]
    [InlineData("DriverSelected", "Negotiating", true)]
    [InlineData("DriverSelected", "Cancelled", true)]
    [InlineData("Negotiating", "Cancelled", true)]
    [InlineData("DriverSelected", "Declined", true)]
    [InlineData("Negotiating", "Declined", true)]
    [InlineData("DriverSelected", "Expired", true)]
    [InlineData("Negotiating", "Expired", true)]
    [InlineData("Accepted", "Arrived", true)]
    [InlineData("Arrived", "InProgress", true)]
    [InlineData("InProgress", "Completed", true)]
    [InlineData("Requested", "Cancelled", true)]
    [InlineData("Accepted", "Cancelled", true)]
    [InlineData("Arrived", "Cancelled", true)]
    [InlineData("InProgress", "Cancelled", false)]
    [InlineData("Completed", "Cancelled", false)]
    [InlineData("Cancelled", "Accepted", false)]
    [InlineData("Declined", "Negotiating", false)]
    [InlineData("Expired", "Negotiating", false)]
    [InlineData("Negotiating", "Accepted", false)]
    [InlineData("Requested", "Completed", false)]
    [InlineData("Accepted", "InProgress", false)]
    public void CanTransition_EnforcesAllowedStateChanges(string current, string next, bool expected)
    {
        Assert.Equal(expected, RideLifecycle.CanTransition(current, next));
    }

    [Theory]
    [InlineData("Requested", false)]
    [InlineData("DriverSelected", false)]
    [InlineData("Negotiating", false)
    ]
    [InlineData("Accepted", true)]
    [InlineData("Arrived", true)]
    [InlineData("InProgress", true)]
    [InlineData("Completed", false)]
    [InlineData("Cancelled", false)]
    public void IsLocationSharingActive_StopsForTerminalAndUnassignedRides(string status, bool expected)
    {
        Assert.Equal(expected, RideLifecycle.IsLocationSharingActive(status));
    }
}
