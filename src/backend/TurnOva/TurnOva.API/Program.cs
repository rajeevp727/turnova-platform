using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos;
using Microsoft.IdentityModel.Tokens;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;
using TurnOva.Infrastructure.Database.Migrations;
using TurnOva.Infrastructure.Dispatch;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"];
var jwtAudience = builder.Configuration["Auth:Jwt:Audience"];
var jwtSigningKey = builder.Configuration["Auth:Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience) ||
    string.IsNullOrWhiteSpace(jwtSigningKey) || jwtSigningKey.Length < 32)
    throw new InvalidOperationException("Configure Auth:Jwt:Issuer, Auth:Jwt:Audience, and a 32+ character Auth:Jwt:SigningKey shared with TriSend.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/rides"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };\n    options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "role",
            NameClaimType = "name"
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ConcurrentDictionary<string, DemoRide>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, string>>();
builder.Services.AddHttpClient("TriSend", client =>
{
    var baseUrl = builder.Configuration["TriSend:BaseUrl"];
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException("TriSend:BaseUrl must be a valid HTTPS URL.");
    client.BaseAddress = uri;
    client.Timeout = TimeSpan.FromSeconds(15);
});


var cosmosEndpoint = builder.Configuration["CosmosDb:Endpoint"];
var cosmosKey = builder.Configuration["CosmosDb:Key"];
var databaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "TurnOvaDb";
var useCosmos = !string.IsNullOrWhiteSpace(cosmosEndpoint) || !string.IsNullOrWhiteSpace(cosmosKey);

if (useCosmos && (string.IsNullOrWhiteSpace(cosmosEndpoint) || string.IsNullOrWhiteSpace(cosmosKey)))
    throw new InvalidOperationException("Both CosmosDb:Endpoint and CosmosDb:Key must be configured.");

if (useCosmos)
{
    var cosmosClient = new CosmosClient(cosmosEndpoint!, cosmosKey!, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    });

    builder.Services.AddSingleton(cosmosClient);
    builder.Services.AddSingleton(new CosmosDbOptions
    {
        Endpoint = cosmosEndpoint!,
        Key = cosmosKey!,
        DatabaseName = databaseName
    });
    builder.Services.AddSingleton<ICosmosMigration, InitialSchemaMigration>();
    builder.Services.AddSingleton<CosmosMigrationRunner>();
    builder.Services.AddSingleton<IQueueStore>(sp =>
        new CosmosQueueStore(sp.GetRequiredService<CosmosClient>()
            .GetContainer(databaseName, "QueueEntries")));
}
else
{
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
}

builder.Services.AddScoped<QueueService>();

var app = builder.Build();

if (useCosmos)
{
    // Do not accept traffic until all pending schema migrations have completed.
    await app.Services.GetRequiredService<CosmosMigrationRunner>().RunAsync();
}

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<RideTrackingHub>("/hubs/rides").RequireAuthorization(policy => policy.RequireRole("Rider"));

var rides = app.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
var idempotencyKeys = app.Services.GetRequiredService<ConcurrentDictionary<string, string>>();
Container? rideContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "Rides") : null;
Container? idempotencyContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "RideIdempotency") : null;

async Task SaveRideAsync(DemoRide ride, CancellationToken ct)
{
    if (rideContainer is null) { rides[ride.RideId] = ride; return; }
    await rideContainer.UpsertItemAsync(ride, new PartitionKey(ride.RideId), cancellationToken: ct);
}

async Task<DemoRide?> FindRideAsync(string rideId, CancellationToken ct)
{
    if (rideContainer is null) return rides.TryGetValue(rideId, out var item) ? item : null;
    try { return (await rideContainer.ReadItemAsync<DemoRide>(rideId, new PartitionKey(rideId), cancellationToken: ct)).Resource; }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
}
var storageMode = useCosmos ? "cosmos-db" : "demo-in-memory";

app.MapGet("/", () => Results.Ok(new
{
    name = "TurnOva API",
    version = "v1",
    mode = storageMode,
    database = useCosmos ? databaseName : null,
    message = "Everyone's Turn Matters."
}));

app.MapGet("/health", () => Results.Ok(new
{
    service = "TurnOva.API",
    status = "healthy",
    environment = app.Environment.EnvironmentName,
    storage = storageMode,
    database = useCosmos ? databaseName : null
}));


var authGroup = app.MapGroup("/api/v1/auth");
authGroup.MapPost("/{role}/register", async (
    string role, PasswordRegisterRequest request, IHttpClientFactory clients,
    CancellationToken ct) =>
{
    if (role is not ("driver" or "rider"))
        return Results.NotFound();
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { message = "Email and password are required." });

    var payload = new
    {
        email = request.Email.Trim(),
        password = request.Password,
        name = request.Name?.Trim(),
        appId = "turnova",
        role = role == "driver" ? "Driver" : "Rider",
        replaceOldest = request.ReplaceOldest
    };
    return await ForwardAuthRequest(clients, HttpMethod.Post, "/auth/register", payload, null, role == "driver" ? "Driver" : "Rider", ct);
});
authGroup.MapPost("/{role}/login", async (
    string role, PasswordLoginRequest request, IHttpClientFactory clients,
    CancellationToken ct) =>
{
    if (role is not ("driver" or "rider"))
        return Results.NotFound();
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { message = "Email and password are required." });

    var payload = new
    {
        email = request.Email.Trim(),
        password = request.Password,
        appId = "turnova",
        replaceOldest = request.ReplaceOldest
    };
    return await ForwardAuthRequest(clients, HttpMethod.Post, "/auth/login", payload, null, role == "driver" ? "Driver" : "Rider", ct);
});
authGroup.MapPost("/refresh", async (
    RefreshTokenRequest request, IHttpClientFactory clients, CancellationToken ct) =>
    await ForwardAuthRequest(clients, HttpMethod.Post, "/auth/refresh",
        new { refreshToken = request.RefreshToken }, null, null, ct));
authGroup.MapPost("/logout", async (
    HttpContext context, IHttpClientFactory clients, CancellationToken ct) =>
    await ForwardAuthRequest(clients, HttpMethod.Post, "/auth/logout", null,
        context.Request.Headers.Authorization.ToString(), null, ct));
authGroup.MapGet("/me", async (HttpContext context, IHttpClientFactory clients, CancellationToken ct) =>
    await ForwardAuthRequest(clients, HttpMethod.Get, "/auth/me", null,
        context.Request.Headers.Authorization.ToString(), null, ct));

static async Task<IResult> ForwardAuthRequest(
    IHttpClientFactory clients, HttpMethod method, string path, object? body,
    string? authorization, string? expectedRole, CancellationToken ct)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null)
        request.Content = JsonContent.Create(body);
    if (!string.IsNullOrWhiteSpace(authorization) &&
        AuthenticationHeaderValue.TryParse(authorization, out var header))
        request.Headers.Authorization = header;

    try
    {
        using var response = await clients.CreateClient("TriSend").SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode && expectedRole is not null)
        {
            using var json = JsonDocument.Parse(content);
            var actualRole = json.RootElement.GetProperty("user").GetProperty("role").GetString();
            if (!string.Equals(actualRole, expectedRole, StringComparison.Ordinal))
            {
                var accessToken = json.RootElement.GetProperty("accessToken").GetString();
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    using var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
                    logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    using var ignored = await clients.CreateClient("TriSend").SendAsync(logout, ct);
                }
                return Results.Problem($"This account is registered as {actualRole}, not {expectedRole}.", statusCode: 403);
            }
        }
        return Results.Content(content, "application/json", Encoding.UTF8, (int)response.StatusCode);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Authentication service is temporarily unavailable.", statusCode: 503);
    }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested)
    {
        return Results.Problem("Authentication service timed out.", statusCode: 504);
    }
}

app.MapGet("/api/v1/zones", () => Results.Ok(new[]
{
    new { id = "hyderabad-central", name = "Hyderabad Central", serviceClasses = new[] { "auto" } }
})).RequireAuthorization();

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
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapGet("/api/v1/zones/{zoneId}/queues/{serviceClass}/me", async (
    string zoneId, string serviceClass, ClaimsPrincipal user, QueueService service, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId))
        return Results.Forbid();

    var entries = await service.GetPositionedQueueAsync(zoneId, serviceClass, ct);
    var position = entries.ToList().FindIndex(entry => entry.DriverId == driverId);
    return Results.Ok(new { isQueued = position >= 0, position = position >= 0 ? position + 1 : (int?)null });
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapDelete("/api/v1/zones/{zoneId}/queues/{serviceClass}/me", async (
    string zoneId, string serviceClass, ClaimsPrincipal user, QueueService service, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId))
        return Results.Forbid();

    await service.LeaveAsync(driverId, zoneId, serviceClass, ct);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Driver"));


app.MapPost("/api/v1/queues/join", async (
    ClaimsPrincipal user, JoinQueueRequest request, QueueService service, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId) || string.IsNullOrWhiteSpace(request.ZoneId))
        return Results.BadRequest(new { message = "A valid driver identity and zone are required." });

    try
    {
        var serviceClass = string.IsNullOrWhiteSpace(request.ServiceClass) ? "auto" : request.ServiceClass.Trim();
        await service.JoinAsync(new DriverQueueEntry(
            driverId, request.ZoneId.Trim(), serviceClass,
            DateTimeOffset.UtcNow, 0, true), ct);
        var entries = await service.GetPositionedQueueAsync(request.ZoneId.Trim(), serviceClass, ct);
        var position = entries.ToList().FindIndex(x => x.DriverId == driverId) + 1;
        return Results.Ok(new { message = "You joined the queue.", driverId, position });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/request", async (HttpContext context, ClaimsPrincipal user, CreateRideRequest request, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Pickup) || string.IsNullOrWhiteSpace(request.Dropoff))
        return Results.BadRequest(new { message = "Pickup and drop-off are required." });
    if (request.Passengers is < 1 or > 6)
        return Results.BadRequest(new { message = "Passenger count must be between 1 and 6." });
    if (request.PickupLatitude is null or < -90 or > 90 || request.PickupLongitude is null or < -180 or > 180)
        return Results.BadRequest(new { message = "Valid pickup coordinates are required." });
    var riderId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var key = context.Request.Headers["Idempotency-Key"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(riderId)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
        return Results.BadRequest(new { message = "A valid Idempotency-Key header (1-128 characters) is required." });

    var keyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    var rideId = $"RIDE-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    var reservation = new RideIdempotencyRecord(keyId, riderId, rideId);
    if (idempotencyContainer is null)
    {
        if (!idempotencyKeys.TryAdd($"{riderId}:{keyId}", rideId))
        {
            var existingId = idempotencyKeys[$"{riderId}:{keyId}"];
            var existingRide = await FindRideAsync(existingId, ct);
            return existingRide is null ? Results.Conflict(new { message = "The original request is still being processed. Retry shortly." }) : Results.Ok(existingRide);
        }
    }
    else
    {
        try { await idempotencyContainer.CreateItemAsync(reservation, new PartitionKey(riderId), cancellationToken: ct); }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var existing = await idempotencyContainer.ReadItemAsync<RideIdempotencyRecord>(keyId, new PartitionKey(riderId), cancellationToken: ct);
            var existingRide = await FindRideAsync(existing.Resource.RideId, ct);
            return existingRide is null ? Results.Conflict(new { message = "The original request is still being processed. Retry shortly." }) : Results.Ok(existingRide);
        }
    }

    var ride = new DemoRide(rideId, request.Pickup.Trim(), request.Dropoff.Trim(), request.Passengers,
        "Requested", DateTimeOffset.UtcNow, 85m, riderId, null, request.PickupLatitude, request.PickupLongitude,
        null, null, null, null);
    try { await SaveRideAsync(ride, ct); }
    catch
    {
        if (idempotencyContainer is null) idempotencyKeys.TryRemove($"{riderId}:{keyId}", out _);
        else await idempotencyContainer.DeleteItemAsync<RideIdempotencyRecord>(keyId, new PartitionKey(riderId), cancellationToken: ct);
        throw;
    }
    return Results.Created($"/api/v1/rides/{ride.RideId}", ride);
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

app.MapGet("/api/v1/driver/rides/available", async (CancellationToken ct) =>
{
    if (rideContainer is null) return Results.Ok(rides.Values.Where(x => x.Status == "Requested"));
    var query = new QueryDefinition("SELECT * FROM c WHERE c.status = @status").WithParameter("@status", "Requested");
    var iterator = rideContainer.GetItemQueryIterator<DemoRide>(query);
    var results = new List<DemoRide>();
    while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(ct));
    return Results.Ok(results);
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/{rideId}/accept", async (ClaimsPrincipal user, string rideId, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    if (rideContainer is null)
    {
        if (!rides.TryGetValue(rideId, out var current) || current.Status != "Requested")
            return Results.Conflict(new { message = "This ride is no longer available." });
        var acceptedInMemory = current with { Status = "Accepted", DriverId = driverId, AcceptedAt = DateTimeOffset.UtcNow, LocationSharingActive = true };
        return rides.TryUpdate(rideId, acceptedInMemory, current)
            ? Results.Ok(acceptedInMemory)
            : Results.Conflict(new { message = "Another driver accepted this ride." });
    }
    try
    {
        var response = await rideContainer.ReadItemAsync<DemoRide>(rideId, new PartitionKey(rideId), cancellationToken: ct);
        if (response.Resource.Status != "Requested")
            return Results.Conflict(new { message = "This ride is no longer available." });
        var accepted = response.Resource with { Status = "Accepted", DriverId = driverId, AcceptedAt = DateTimeOffset.UtcNow, LocationSharingActive = true };
        await rideContainer.ReplaceItemAsync(accepted, rideId, new PartitionKey(rideId),
            new ItemRequestOptions { IfMatchEtag = response.ETag }, ct);
        return Results.Ok(accepted);
    }
    catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
    {
        return Results.Conflict(new { message = "This ride is no longer available." });
    }
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/{rideId}/location", async (ClaimsPrincipal user, string rideId, DriverLocationRequest location, IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    if (location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180)
        return Results.BadRequest(new { message = "Invalid coordinates." });
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ride = await FindRideAsync(rideId, ct);
    if (ride is null || ride.DriverId != driverId || !ride.LocationSharingActive || !RideLifecycle.IsLocationSharingActive(ride.Status))
        return Results.NotFound(new { message = "Active ride not found." });
    var updated = ride with { DriverLatitude = location.Latitude, DriverLongitude = location.Longitude, LocationUpdatedAt = DateTimeOffset.UtcNow };
    if (!await TryReplaceRideAsync(ride, updated, ct))
        return Results.Conflict(new { message = "Ride state changed. Refresh before sending another location update." });
    await hub.Clients.Group(rideId).SendAsync("DriverLocationUpdated", new
    {
        rideId,
        latitude = updated.DriverLatitude,
        longitude = updated.DriverLongitude,
        updated.LocationUpdatedAt
    }, ct);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/{rideId}/arrive", (ClaimsPrincipal user, string rideId, CancellationToken ct) =>
    TransitionRideAsync(user, rideId, "Accepted", "Arrived", ct))
    .RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/{rideId}/start", (ClaimsPrincipal user, string rideId, CancellationToken ct) =>
    TransitionRideAsync(user, rideId, "Arrived", "InProgress", ct))
    .RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/{rideId}/complete", (ClaimsPrincipal user, string rideId, CancellationToken ct) =>
    TransitionRideAsync(user, rideId, "InProgress", "Completed", ct))
    .RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/{rideId}/cancel", async (ClaimsPrincipal user, string rideId, IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    var riderId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ride = await FindRideAsync(rideId, ct);
    if (ride is null || ride.RiderId != riderId) return Results.NotFound();
    if (ride.Status is not ("Requested" or "Accepted" or "Arrived"))
        return Results.Conflict(new { message = "A ride can only be cancelled before it starts." });
    var updated = await TryTransitionAsync(ride, "Cancelled", ct);
    if (updated is null) return Results.Conflict(new { message = "Ride state changed. Refresh and try again." });
    await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = updated.Status }, ct);
    return Results.Ok(updated);
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

async Task<IResult> TransitionRideAsync(ClaimsPrincipal user, string rideId, string expected, string next, CancellationToken ct)
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ride = await FindRideAsync(rideId, ct);
    if (ride is null || ride.DriverId != driverId) return Results.NotFound();
    if (ride.Status != expected) return Results.Conflict(new { message = $"Cannot move ride from {ride.Status} to {next}." });
    var updated = await TryTransitionAsync(ride, next, ct);
    if (updated is null) return Results.Conflict(new { message = "Ride state changed. Refresh and try again." });
    await app.Services.GetRequiredService<IHubContext<RideTrackingHub>>().Clients.Group(rideId)
        .SendAsync("RideStatusChanged", new { rideId, status = updated.Status }, ct);
    return Results.Ok(updated);
}

async Task<DemoRide?> TryTransitionAsync(DemoRide current, string next, CancellationToken ct)
{
    if (!RideLifecycle.CanTransition(current.Status, next)) return null;
    var updated = current with { Status = next, LocationSharingActive = RideLifecycle.IsLocationSharingActive(next) };
    return await TryReplaceRideAsync(current, updated, ct) ? updated : null;
}

async Task<bool> TryReplaceRideAsync(DemoRide current, DemoRide updated, CancellationToken ct)
{
    if (rideContainer is null)
        return rides.TryUpdate(current.RideId, updated, current);
    try
    {
        var response = await rideContainer.ReadItemAsync<DemoRide>(current.RideId, new PartitionKey(current.RideId), cancellationToken: ct);
        if (response.Resource.Status != current.Status || response.Resource.DriverId != current.DriverId)
            return false;
        await rideContainer.ReplaceItemAsync(updated, current.RideId, new PartitionKey(current.RideId),
            new ItemRequestOptions { IfMatchEtag = response.ETag }, ct);
        return true;
    }
    catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
    { return false; }
}

app.MapGet("/api/v1/rides/{rideId}", async (ClaimsPrincipal user, string rideId, CancellationToken ct) =>
{
    var ride = await FindRideAsync(rideId, ct);
    var riderId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    return ride is not null && ride.RiderId == riderId
        ? Results.Ok(ride)
        : Results.NotFound(new { message = "Ride not found." });
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

app.Run();

public sealed record JoinQueueRequest(string ZoneId, string? ServiceClass);
public sealed record CreateRideRequest(string Pickup, string Dropoff, int Passengers = 1, double? PickupLatitude = null, double? PickupLongitude = null);
public sealed record DriverLocationRequest(double Latitude, double Longitude);
public sealed record RideIdempotencyRecord(string id, string riderId, string rideId);
public sealed record PasswordRegisterRequest(string Email, string Password, string? Name, bool ReplaceOldest = false);
public sealed record PasswordLoginRequest(string Email, string Password, bool ReplaceOldest = false);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record DemoRide(
    string RideId, string Pickup, string Dropoff, int Passengers,
    string Status, DateTimeOffset RequestedAt, decimal EstimatedFareInr, string RiderId, string? DriverId, double? PickupLatitude, double? PickupLongitude, double? DriverLatitude, double? DriverLongitude, DateTimeOffset? AcceptedAt, DateTimeOffset? LocationUpdatedAt, bool LocationSharingActive = false);

public partial class Program;
