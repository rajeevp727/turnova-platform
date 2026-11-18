using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos;

public static class RideTrackingHubAuthorization
{
    public static string? ResolveDriverNotificationGroup(ClaimsPrincipal? user, string? requestedDriverId, string? rideId)
    {
        var userId = GetUserId(user);
        return user?.IsInRole("Driver") == true &&
            !string.IsNullOrWhiteSpace(userId) &&
            string.Equals(requestedDriverId, userId, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(rideId)
                ? $"driver:{userId}"
                : null;
    }

    public static bool CanRequestRideGroup(ClaimsPrincipal? user, string? rideId)
    {
        var userId = GetUserId(user);
        return (user?.IsInRole("Rider") == true || user?.IsInRole("Driver") == true) &&
            !string.IsNullOrWhiteSpace(userId) &&
            !string.IsNullOrWhiteSpace(rideId);
    }

    public static bool CanJoinRideGroup(ClaimsPrincipal? user, string? rideId, DemoRide? ride)
    {
        if (!CanRequestRideGroup(user, rideId) || ride is null) return false;

        // Bind authorization to the exact aggregate that will back the requested SignalR group.
        // Owner checks alone are insufficient if a stale or inconsistent store entry is returned.
        if (!string.Equals(ride.RideId, rideId, StringComparison.Ordinal)) return false;

        var userId = GetUserId(user)!;
        if (user?.IsInRole("Rider") == true &&
            !string.Equals(ride.RiderId, userId, StringComparison.Ordinal)) return false;
        if (user?.IsInRole("Driver") == true &&
            !string.Equals(ride.DriverId, userId, StringComparison.Ordinal)) return false;

        return true;
    }

    private static string? GetUserId(ClaimsPrincipal? user) =>
        user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
}

public sealed class RideTrackingHub(
    ConcurrentDictionary<string, DemoRide> rides,
    IServiceProvider services,
    IConfiguration configuration) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var rideId = httpContext?.Request.Query["rideId"].ToString();
        var requestedDriverId = httpContext?.Request.Query["driverId"].ToString();
        var user = Context.User;

        var driverGroup = RideTrackingHubAuthorization.ResolveDriverNotificationGroup(user, requestedDriverId, rideId);
        if (driverGroup is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, driverGroup);
            await base.OnConnectedAsync();
            return;
        }

        if (!RideTrackingHubAuthorization.CanRequestRideGroup(user, rideId))
        {
            Context.Abort();
            return;
        }

        DemoRide? ride;
        var cosmos = services.GetService<CosmosClient>();
        if (cosmos is null)
        {
            rides.TryGetValue(rideId!, out ride);
        }
        else
        {
            try
            {
                var databaseName = configuration["CosmosDb:DatabaseName"] ?? "TurnOvaDb";
                ride = (await cosmos.GetContainer(databaseName, "Rides")
                    .ReadItemAsync<DemoRide>(rideId!, new PartitionKey(rideId))).Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                ride = null;
            }
        }

        if (!RideTrackingHubAuthorization.CanJoinRideGroup(user, rideId, ride))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, rideId!);
        await base.OnConnectedAsync();
    }
}
