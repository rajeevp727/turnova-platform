using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var queueStore = new InMemoryQueueStore();
var demoDrivers = new[]
{
    new DriverQueueEntry("DRV-1001", "hyderabad-central", "auto", DateTimeOffset.UtcNow.AddMinutes(-28), 0, true),
    new DriverQueueEntry("DRV-1002", "hyderabad-central", "auto", DateTimeOffset.UtcNow.AddMinutes(-18), 0, true),
    new DriverQueueEntry("DRV-1003", "hyderabad-central", "auto", DateTimeOffset.UtcNow.AddMinutes(-9), 0, true)
};
foreach (var driver in demoDrivers)
    await queueStore.AddAsync(driver, CancellationToken.None);

builder.Services.AddSingleton<IQueueStore>(queueStore);
builder.Services.AddScoped<QueueService>();

var app = builder.Build();
app.UseCors();
app.UseHttpsRedirection();

var rides = new ConcurrentDictionary<string, DemoRide>();

app.MapGet("/", () => Results.Ok(new
{
    name = "TurnOva API",
    version = "v1",
    mode = "demo",
    message = "Everyone's Turn Matters."
}));

app.MapGet("/health", () => Results.Ok(new
{
    service = "TurnOva.API",
    status = "healthy",
    environment = app.Environment.EnvironmentName,
    mode = "demo"
}));

app.MapGet("/api/v1/zones", () => Results.Ok(new[]
{
    new { id = "hyderabad-central", name = "Hyderabad Central", serviceClasses = new[] { "auto" } }
}));

app.MapGet("/api/v1/zones/{zoneId}/queues/{serviceClass}", async (
    string zoneId, string serviceClass, QueueService service, CancellationToken ct) =>
{
    var entries = await service.GetPositionedQueueAsync(zoneId, serviceClass, ct);
    return Results.Ok(entries.Select((entry, index) => new
    {
        entry.DriverId,
        entry.ZoneId,
        entry.ServiceClass,
        position = index + 1,
        entry.JoinedAt
    }));
});

app.MapPost("/api/v1/queues/join", async (
    JoinQueueRequest request, QueueService service, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.DriverId) || string.IsNullOrWhiteSpace(request.ZoneId))
        return Results.BadRequest(new { message = "Driver ID and zone are required." });

    try
    {
        await service.JoinAsync(new DriverQueueEntry(
            request.DriverId.Trim(), request.ZoneId.Trim(),
            string.IsNullOrWhiteSpace(request.ServiceClass) ? "auto" : request.ServiceClass.Trim(),
            DateTimeOffset.UtcNow, 0, true), ct);
        var entries = await service.GetPositionedQueueAsync(request.ZoneId, request.ServiceClass ?? "auto", ct);
        var position = entries.ToList().FindIndex(x => x.DriverId == request.DriverId.Trim()) + 1;
        return Results.Ok(new { message = "You joined the queue.", driverId = request.DriverId, position });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
});

app.MapPost("/api/v1/rides/request", (CreateRideRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Pickup) || string.IsNullOrWhiteSpace(request.Dropoff))
        return Results.BadRequest(new { message = "Pickup and drop-off are required." });

    var ride = new DemoRide(
        $"RIDE-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
        request.Pickup.Trim(), request.Dropoff.Trim(),
        Math.Max(1, request.Passengers), "Requested",
        DateTimeOffset.UtcNow, 85m);
    rides[ride.RideId] = ride;
    return Results.Created($"/api/v1/rides/{ride.RideId}", ride);
});

app.MapGet("/api/v1/rides/{rideId}", (string rideId) =>
    rides.TryGetValue(rideId, out var ride)
        ? Results.Ok(ride)
        : Results.NotFound(new { message = "Ride not found." }));

app.Run();

public sealed record JoinQueueRequest(string DriverId, string ZoneId, string? ServiceClass);
public sealed record CreateRideRequest(string Pickup, string Dropoff, int Passengers = 1);
public sealed record DemoRide(
    string RideId, string Pickup, string Dropoff, int Passengers,
    string Status, DateTimeOffset RequestedAt, decimal EstimatedFareInr);

public partial class Program;
