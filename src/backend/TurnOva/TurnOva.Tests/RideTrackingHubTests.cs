using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class RideTrackingHubTests
{
    [Fact]
    public async Task RiderCanJoinOnlyTheirOwnRideGroup()
    {
        var ride = CreateRide();
        var (hub, caller, groups) = CreateHub("rider-1", "Rider", "?rideId=ride-1", ride);

        await hub.OnConnectedAsync();

        Assert.False(caller.Aborted);
        Assert.Equal(["ride-1"], groups.AddedGroups);
    }

    [Theory]
    [InlineData("other-rider", "Rider")]
    [InlineData("other-driver", "Driver")]
    public async Task NonOwnerCannotJoinRideGroup(string userId, string role)
    {
        var (hub, caller, groups) = CreateHub(userId, role, "?rideId=ride-1", CreateRide());

        await hub.OnConnectedAsync();

        Assert.True(caller.Aborted);
        Assert.Empty(groups.AddedGroups);
    }

    [Fact]
    public async Task DriverCanJoinTheirOwnSelectedBookingNotificationGroup()
    {
        var (hub, caller, groups) = CreateHub("driver-1", "Driver", "?driverId=driver-1");

        await hub.OnConnectedAsync();

        Assert.False(caller.Aborted);
        Assert.Equal(["driver:driver-1"], groups.AddedGroups);
    }

    [Fact]
    public async Task DriverCannotJoinAnotherDriversNotificationGroup()
    {
        var (hub, caller, groups) = CreateHub("driver-1", "Driver", "?driverId=driver-2");

        await hub.OnConnectedAsync();

        Assert.True(caller.Aborted);
        Assert.Empty(groups.AddedGroups);
    }

    private static DemoRide CreateRide() => new(
        "ride-1", "Pickup", "Dropoff", 1, "Negotiating", DateTimeOffset.UtcNow,
        null, "rider-1", "driver-1", null, null, null, null, null, null);

    private static (RideTrackingHub Hub, TestHubCallerContext Caller, RecordingGroupManager Groups) CreateHub(
        string userId, string role, string query, DemoRide? ride = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString(query);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        ], "test"));
        httpContext.Features.Set<IHttpContextFeature>(new TestHttpContextFeature(httpContext));

        var rides = new ConcurrentDictionary<string, DemoRide>();
        if (ride is not null) rides[ride.RideId] = ride;
        var services = new ServiceCollection().BuildServiceProvider();
        var hub = new RideTrackingHub(rides, services, new ConfigurationBuilder().Build());
        var caller = new TestHubCallerContext(httpContext);
        var groups = new RecordingGroupManager();
        hub.Context = caller;
        hub.Groups = groups;
        return (hub, caller, groups);
    }

    private sealed class TestHttpContextFeature(HttpContext httpContext) : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }

    private sealed class TestHubCallerContext(HttpContext httpContext) : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId => "test-connection";
        public override string? UserIdentifier => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal? User => httpContext.User;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => httpContext.Features;
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<string> AddedGroups { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            AddedGroups.Add(groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            AddedGroups.Remove(groupName);
            return Task.CompletedTask;
        }
    }
}
