using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos;
using TurnOva.Infrastructure.Database;

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
            await Groups.AddToGroupAsync(Context.ConnectionId, driverGroup, Context.ConnectionAborted);
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
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "TurnOvaDb";
            try
            {
                // Use the shared point-read guard so a disconnected SignalR client cannot
                // continue with stale Cosmos work, and only the requested ride aggregate
                // can authorize membership in its tracking group.
                ride = await CosmosReadResponseGuard.ReadItemAsync<DemoRide>(
                    cosmos.GetContainer(databaseName, "Rides"),
                    rideId!,
                    new PartitionKey(rideId!),
                    item => string.Equals(item.RideId, rideId, StringComparison.Ordinal),
                    "ride",
                    Context.ConnectionAborted);
            }
            catch (InvalidOperationException)
            {
                // A null or identity-mismatched SDK resource is not a valid group target.
                Context.Abort();
                return;
            }
        }

        if (!RideTrackingHubAuthorization.CanJoinRideGroup(user, rideId, ride))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, rideId!, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
