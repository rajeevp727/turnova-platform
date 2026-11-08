using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task OnConnectedAsyncAddsOnlyTheOwningRiderToTheRideGroup()
    {
        var rides = new ConcurrentDictionary<string, DemoRide>
        {
            ["ride-1"] = CreateRide("rider-1", "driver-1")
        };
        var ownerContext = CreateHubContext(CreatePrincipal("rider-1", "Rider"), "?rideId=ride-1");
        var ownerGroups = new RecordingGroupManager();
        var ownerHub = CreateHub(rides, ownerContext, ownerGroups);

        await ownerHub.OnConnectedAsync();

        Assert.False(ownerContext.IsAborted);
        var riderGroup = Assert.Single(ownerGroups.Added);
        Assert.Equal("connection-1", riderGroup.ConnectionId);
        Assert.Equal("ride-1", riderGroup.GroupName);

        var assignedDriverContext = CreateHubContext(CreatePrincipal("driver-1", "Driver"), "?rideId=ride-1");
        var assignedDriverGroups = new RecordingGroupManager();
        var assignedDriverHub = CreateHub(rides, assignedDriverContext, assignedDriverGroups);

        await assignedDriverHub.OnConnectedAsync();

        Assert.False(assignedDriverContext.IsAborted);
        var assignedDriverGroup = Assert.Single(assignedDriverGroups.Added);
        Assert.Equal("ride-1", assignedDriverGroup.GroupName);

        var otherDriverContext = CreateHubContext(CreatePrincipal("other-driver", "Driver"), "?rideId=ride-1");
        var otherDriverGroups = new RecordingGroupManager();
        var otherDriverHub = CreateHub(rides, otherDriverContext, otherDriverGroups);

        await otherDriverHub.OnConnectedAsync();

        Assert.True(otherDriverContext.IsAborted);
        Assert.Empty(otherDriverGroups.Added);

        var otherContext = CreateHubContext(CreatePrincipal("other-rider", "Rider"), "?rideId=ride-1");
        var otherGroups = new RecordingGroupManager();
        var otherHub = CreateHub(rides, otherContext, otherGroups);

        await otherHub.OnConnectedAsync();

        Assert.True(otherContext.IsAborted);
        Assert.Empty(otherGroups.Added);
    }

    [Fact]
    public async Task OnConnectedAsyncAddsOnlyTheAuthenticatedDriversOwnNotificationGroup()
    {
        var rides = new ConcurrentDictionary<string, DemoRide>();
        var ownerContext = CreateHubContext(CreatePrincipal("driver-1", "Driver"), "?driverId=driver-1");
        var ownerGroups = new RecordingGroupManager();
        var ownerHub = CreateHub(rides, ownerContext, ownerGroups);

        await ownerHub.OnConnectedAsync();

        Assert.False(ownerContext.IsAborted);
        var driverGroup = Assert.Single(ownerGroups.Added);
        Assert.Equal("connection-1", driverGroup.ConnectionId);
        Assert.Equal("driver:driver-1", driverGroup.GroupName);

        var otherContext = CreateHubContext(CreatePrincipal("driver-1", "Driver"), "?driverId=driver-2");
        var otherGroups = new RecordingGroupManager();
        var otherHub = CreateHub(rides, otherContext, otherGroups);

        await otherHub.OnConnectedAsync();

        Assert.True(otherContext.IsAborted);
        Assert.Empty(otherGroups.Added);
    }

    private static RideTrackingHub CreateHub(
        ConcurrentDictionary<string, DemoRide> rides,
        TestHubCallerContext context,
        RecordingGroupManager groups)
    {
        var hub = new RideTrackingHub(
            rides,
            new ServiceCollection().BuildServiceProvider(),
            new ConfigurationBuilder().Build())
        {
            Context = context,
            Groups = groups
        };
        return hub;
    }

    private static TestHubCallerContext CreateHubContext(ClaimsPrincipal user, string query)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString(query);
        return new TestHubCallerContext(user, httpContext);
    }

    private sealed class TestHubCallerContext : HubCallerContext
    {
        public TestHubCallerContext(ClaimsPrincipal user, HttpContext httpContext)
        {
            User = user;
            var features = new FeatureCollection();
            features.Set<IHttpContextFeature>(new TestHttpContextFeature { HttpContext = httpContext });
            Features = features;
        }

        public bool IsAborted { get; private set; }
        public override string ConnectionId => "connection-1";
        public override string? UserIdentifier => User?.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal? User { get; }
        public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; }
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => IsAborted = true;
    }

    private sealed class TestHttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
