using System.Security.Claims;

public sealed class RideTrackingHubTests
{
    [Fact]
    public void RiderCanJoinOnlyTheirOwnRideGroup()
    {
        var rider = CreatePrincipal("rider-1", "Rider");

        Assert.True(RideTrackingHubAuthorization.CanRequestRideGroup(rider, "ride-1"));
        Assert.True(RideTrackingHubAuthorization.CanJoinRideGroup(rider, "ride-1", CreateRide("rider-1", "driver-1")));
        Assert.False(RideTrackingHubAuthorization.CanJoinRideGroup(rider, "ride-1", CreateRide("other-rider", "driver-1")));
    }

    [Fact]
    public void AssignedDriverCanJoinRideGroupButAnotherDriverCannot()
    {
        var driver = CreatePrincipal("driver-1", "Driver");

        Assert.True(RideTrackingHubAuthorization.CanJoinRideGroup(driver, "ride-1", CreateRide("rider-1", "driver-1")));
        Assert.False(RideTrackingHubAuthorization.CanJoinRideGroup(driver, "ride-1", CreateRide("rider-1", "driver-2")));
    }

    [Fact]
    public void DriverCanJoinOnlyTheirOwnSelectedBookingNotificationGroup()
    {
        var driver = CreatePrincipal("driver-1", "Driver");

        Assert.Equal("driver:driver-1", RideTrackingHubAuthorization.ResolveDriverNotificationGroup(driver, "driver-1", null));
        Assert.Null(RideTrackingHubAuthorization.ResolveDriverNotificationGroup(driver, "driver-2", null));
    }

    [Fact]
    public void NonRiderAndNonDriverCannotJoinRideOrDriverGroups()
    {
        var unionManager = CreatePrincipal("union-manager-1", "UnionManager");

        Assert.False(RideTrackingHubAuthorization.CanRequestRideGroup(unionManager, "ride-1"));
        Assert.False(RideTrackingHubAuthorization.CanJoinRideGroup(unionManager, "ride-1", CreateRide("rider-1", "driver-1")));
        Assert.Null(RideTrackingHubAuthorization.ResolveDriverNotificationGroup(unionManager, "driver-1", null));
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        ], "test"));

    private static DemoRide CreateRide(string riderId, string driverId) => new(
        "ride-1", "Pickup", "Dropoff", 1, "Negotiating", DateTimeOffset.UtcNow,
        null, riderId, driverId, null, null, null, null, null, null);
}
