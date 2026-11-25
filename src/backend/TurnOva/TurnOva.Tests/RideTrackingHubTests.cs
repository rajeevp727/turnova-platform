using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos;
using Moq;
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

    [Theory]
    [InlineData("rider-1", "Rider")]
    [InlineData("driver-1", "Driver")]
    public void RiderOrAssignedDriverCannotJoinWhenStoredRideIdDiffersFromRequestedGroup(
        string userId,
        string role)
    {
        var user = CreatePrincipal(userId, role);
        var mismatchedRide = CreateRide("rider-1", "driver-1", "different-ride");

        Assert.False(RideTrackingHubAuthorization.CanJoinRideGroup(user, "ride-1", mismatchedRide));
    }

    [Fact]
    public void DriverCanJoinOnlyTheirOwnSelectedBookingNotificationGroup()
    {
        var driver = CreatePrincipal("driver-1", "Driver");

        Assert.Equal("driver:driver-1", RideTrackingHubAuthorization.ResolveDriverNotificationGroup(driver, "driver-1", null));
        Assert.Null(RideTrackingHubAuthorization.ResolveDriverNotificationGroup(driver, "driver-2", null));
    }

    [Theory]
    [InlineData("Ops")]
    [InlineData("UnionManager")]
    public void NonRiderAndNonDriverCannotJoinRideOrDriverGroups(string role)
    {
        var nonDriver = CreatePrincipal($"{role.ToLowerInvariant()}-1", role);

        Assert.False(RideTrackingHubAuthorization.CanRequestRideGroup(nonDriver, "ride-1"));
        Assert.False(RideTrackingHubAuthorization.CanJoinRideGroup(nonDriver, "ride-1", CreateRide("rider-1", "driver-1")));
        Assert.Null(RideTrackingHubAuthorization.ResolveDriverNotificationGroup(nonDriver, "driver-1", null));
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
    public async Task OnConnectedAsyncAbortsWhenStoredRideIdDiffersFromRequestedGroup()
    {
        var rides = new ConcurrentDictionary<string, DemoRide>
        {
            ["requested-ride"] = CreateRide("rider-1", "driver-1", "different-stored-ride")
        };
        var context = CreateHubContext(CreatePrincipal("rider-1", "Rider"), "?rideId=requested-ride");
        var groups = new RecordingGroupManager();
        var hub = CreateHub(rides, context, groups);

        await hub.OnConnectedAsync();

        Assert.True(context.IsAborted);
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task OnConnectedAsyncUsesGuardedCosmosReadAndAddsOnlyTheOwningRider()
    {
        var ride = CreateRide("rider-1", "driver-1");
        var response = new Mock<ItemResponse<DemoRide>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource).Returns(ride);

        var container = new Mock<Container>(MockBehavior.Strict);
        using var connectionAborted = new CancellationTokenSource();
        container.Setup(value => value.ReadItemAsync<DemoRide>(
                "ride-1",
                It.Is<PartitionKey>(key => key.Equals(new PartitionKey("ride-1"))),
                It.IsAny<ItemRequestOptions?>(),
                It.Is<CancellationToken>(token => token == connectionAborted.Token)))
            .ReturnsAsync(response.Object);

        var cosmos = new Mock<CosmosClient>(MockBehavior.Strict);
        cosmos.Setup(value => value.GetContainer("TurnOvaDb", "Rides")).Returns(container.Object);
        using var services = CreateCosmosServices(cosmos.Object);
        var context = CreateHubContext(
            CreatePrincipal("rider-1", "Rider"), "?rideId=ride-1", connectionAborted.Token);
        var groups = new RecordingGroupManager();
        var hub = CreateHub(new ConcurrentDictionary<string, DemoRide>(), context, groups, services);

        await hub.OnConnectedAsync();

        Assert.False(context.IsAborted);
        Assert.Equal(("connection-1", "ride-1"), Assert.Single(groups.Added));
        container.Verify(value => value.ReadItemAsync<DemoRide>(
            "ride-1",
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("ride-1"))),
            It.IsAny<ItemRequestOptions?>(),
            It.Is<CancellationToken>(token => token == connectionAborted.Token)), Times.Once);
        response.VerifyGet(value => value.Resource, Times.Once);
        container.VerifyNoOtherCalls();
        response.VerifyNoOtherCalls();
        cosmos.Verify(value => value.GetContainer("TurnOvaDb", "Rides"), Times.Once);
        cosmos.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnConnectedAsyncAbortsWhenCosmosRideDoesNotExist()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var notFound = new CosmosException("not found", HttpStatusCode.NotFound, 0, "hub-read-not-found", 0);
        container.Setup(value => value.ReadItemAsync<DemoRide>(
                "ride-1",
                It.Is<PartitionKey>(key => key.Equals(new PartitionKey("ride-1"))),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<ItemResponse<DemoRide>>(notFound));

        var cosmos = new Mock<CosmosClient>(MockBehavior.Strict);
        cosmos.Setup(value => value.GetContainer("TurnOvaDb", "Rides")).Returns(container.Object);
        using var services = CreateCosmosServices(cosmos.Object);
        var context = CreateHubContext(CreatePrincipal("rider-1", "Rider"), "?rideId=ride-1");
        var groups = new RecordingGroupManager();
        var hub = CreateHub(new ConcurrentDictionary<string, DemoRide>(), context, groups, services);

        await hub.OnConnectedAsync();

        Assert.True(context.IsAborted);
        Assert.Empty(groups.Added);
        container.Verify(value => value.ReadItemAsync<DemoRide>(
            "ride-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
        cosmos.Verify(value => value.GetContainer("TurnOvaDb", "Rides"), Times.Once);
        cosmos.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnConnectedAsyncAbortsWhenCosmosReturnsMismatchedRideIdentity()
    {
        var response = new Mock<ItemResponse<DemoRide>>(MockBehavior.Strict);
        response.SetupGet(value => value.Resource)
            .Returns(CreateRide("rider-1", "driver-1", "different-ride"));
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<DemoRide>(
                "ride-1",
                It.Is<PartitionKey>(key => key.Equals(new PartitionKey("ride-1"))),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var cosmos = new Mock<CosmosClient>(MockBehavior.Strict);
        cosmos.Setup(value => value.GetContainer("TurnOvaDb", "Rides")).Returns(container.Object);
        using var services = CreateCosmosServices(cosmos.Object);
        var context = CreateHubContext(CreatePrincipal("rider-1", "Rider"), "?rideId=ride-1");
        var groups = new RecordingGroupManager();
        var hub = CreateHub(new ConcurrentDictionary<string, DemoRide>(), context, groups, services);

        await hub.OnConnectedAsync();

        Assert.True(context.IsAborted);
        Assert.Empty(groups.Added);
        response.VerifyGet(value => value.Resource, Times.Once);
        response.VerifyNoOtherCalls();
        container.Verify(value => value.ReadItemAsync<DemoRide>(
            "ride-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        container.VerifyNoOtherCalls();
        cosmos.Verify(value => value.GetContainer("TurnOvaDb", "Rides"), Times.Once);
        cosmos.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnConnectedAsyncStopsBeforeResourceHydrationWhenConnectionAbortsAfterCosmosRead()
    {
        using var connectionAborted = new CancellationTokenSource();
        var response = new Mock<ItemResponse<DemoRide>>(MockBehavior.Strict);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(value => value.ReadItemAsync<DemoRide>(
                "ride-1",
                It.Is<PartitionKey>(key => key.Equals(new PartitionKey("ride-1"))),
                It.IsAny<ItemRequestOptions?>(),
                It.Is<CancellationToken>(token => token == connectionAborted.Token)))
            .Returns(() =>
            {
                connectionAborted.Cancel();
                return Task.FromResult(response.Object);
            });

        var cosmos = new Mock<CosmosClient>(MockBehavior.Strict);
        cosmos.Setup(value => value.GetContainer("TurnOvaDb", "Rides")).Returns(container.Object);
        using var services = CreateCosmosServices(cosmos.Object);
        var context = CreateHubContext(
            CreatePrincipal("rider-1", "Rider"), "?rideId=ride-1", connectionAborted.Token);
        var groups = new RecordingGroupManager();
        var hub = CreateHub(new ConcurrentDictionary<string, DemoRide>(), context, groups, services);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hub.OnConnectedAsync());

        Assert.Empty(groups.Added);
        response.VerifyGet(value => value.Resource, Times.Never);
        response.VerifyNoOtherCalls();
        container.Verify(value => value.ReadItemAsync<DemoRide>(
            "ride-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(),
            It.Is<CancellationToken>(token => token == connectionAborted.Token)), Times.Once);
        container.VerifyNoOtherCalls();
        cosmos.Verify(value => value.GetContainer("TurnOvaDb", "Rides"), Times.Once);
        cosmos.VerifyNoOtherCalls();
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
        RecordingGroupManager groups,
        IServiceProvider? services = null)
    {
        var hub = new RideTrackingHub(
            rides,
            services ?? new ServiceCollection().BuildServiceProvider(),
            new ConfigurationBuilder().Build())
        {
            Context = context,
            Groups = groups
        };
        return hub;
    }

    private static ServiceProvider CreateCosmosServices(CosmosClient cosmosClient)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cosmosClient);
        return services.BuildServiceProvider();
    }

    private static TestHubCallerContext CreateHubContext(
        ClaimsPrincipal user,
        string query,
        CancellationToken connectionAborted = default)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString(query);
        return new TestHubCallerContext(user, httpContext, connectionAborted);
    }

    private sealed class TestHubCallerContext : HubCallerContext
    {
        public TestHubCallerContext(
            ClaimsPrincipal user,
            HttpContext httpContext,
            CancellationToken connectionAborted)
        {
            User = user;
            ConnectionAborted = connectionAborted;
            var features = new FeatureCollection();
            features.Set<IHttpContextFeature>(new TestHttpContextFeature { HttpContext = httpContext });
            Features = features;
        }

        public bool IsAborted { get; private set; }
        public override string ConnectionId => "connection-1";
        public override string? UserIdentifier => User?.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal? User { get; }
        private readonly IDictionary<object, object?> _items = new Dictionary<object, object?>();
        public override IDictionary<object, object?> Items => _items;
        public override IFeatureCollection Features { get; }
        public override CancellationToken ConnectionAborted { get; }
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

    private static DemoRide CreateRide(string riderId, string driverId, string rideId = "ride-1") => new(
        rideId, "Pickup", "Dropoff", 1, "Negotiating", DateTimeOffset.UtcNow,
        null, riderId, driverId, null, null, null, null, null, null);
}
