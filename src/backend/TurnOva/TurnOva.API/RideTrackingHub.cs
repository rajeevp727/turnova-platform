using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos;

public sealed class RideTrackingHub(
    ConcurrentDictionary<string, DemoRide> rides,
    IServiceProvider services,
    IConfiguration configuration) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var riderId = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
        var rideId = Context.GetHttpContext()?.Request.Query["rideId"].ToString();

        if (user?.IsInRole("Rider") != true || string.IsNullOrWhiteSpace(riderId) ||
            string.IsNullOrWhiteSpace(rideId))
        {
            Context.Abort();
            return;
        }

        DemoRide? ride;
        var cosmos = services.GetService<CosmosClient>();
        if (cosmos is null)
        {
            rides.TryGetValue(rideId, out ride);
        }
        else
        {
            try
            {
                var databaseName = configuration["CosmosDb:DatabaseName"] ?? "TurnOvaDb";
                ride = (await cosmos.GetContainer(databaseName, "Rides")
                    .ReadItemAsync<DemoRide>(rideId, new PartitionKey(rideId))).Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                ride = null;
            }
        }

        if (ride is null || ride.RiderId != riderId)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, rideId);
        await base.OnConnectedAsync();
    }
}
