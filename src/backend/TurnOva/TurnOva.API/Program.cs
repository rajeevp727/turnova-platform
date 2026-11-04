using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        options.TokenValidationParameters = new TokenValidationParameters
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

var rides = new ConcurrentDictionary<string, DemoRide>();
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
        context.Request.Headers.Authorization.ToString(), ct));

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

app.MapPost("/api/v1/rides/request", (ClaimsPrincipal user, CreateRideRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Pickup) || string.IsNullOrWhiteSpace(request.Dropoff))
        return Results.BadRequest(new { message = "Pickup and drop-off are required." });

    var ride = new DemoRide(
        $"RIDE-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
        request.Pickup.Trim(), request.Dropoff.Trim(),
        Math.Clamp(request.Passengers, 1, 6), "Requested",
        DateTimeOffset.UtcNow, 85m, user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? string.Empty);
    rides[ride.RideId] = ride;
    return Results.Created($"/api/v1/rides/{ride.RideId}", new
    {
        ride.RideId, ride.Pickup, ride.Dropoff, ride.Passengers,
        ride.Status, ride.RequestedAt, ride.EstimatedFareInr
    });
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

app.MapGet("/api/v1/rides/{rideId}", (ClaimsPrincipal user, string rideId) =>
    rides.TryGetValue(rideId, out var ride) &&
    ride.RiderId == (user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"))
        ? Results.Ok(new { ride.RideId, ride.Pickup, ride.Dropoff, ride.Passengers, ride.Status, ride.RequestedAt, ride.EstimatedFareInr })
        : Results.NotFound(new { message = "Ride not found." }))
    .RequireAuthorization(policy => policy.RequireRole("Rider"));

app.Run();

public sealed record JoinQueueRequest(string DriverId, string ZoneId, string? ServiceClass);
public sealed record CreateRideRequest(string Pickup, string Dropoff, int Passengers = 1);
public sealed record PasswordRegisterRequest(string Email, string Password, string? Name, bool ReplaceOldest = false);
public sealed record PasswordLoginRequest(string Email, string Password, bool ReplaceOldest = false);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record DemoRide(
    string RideId, string Pickup, string Dropoff, int Passengers,
    string Status, DateTimeOffset RequestedAt, decimal EstimatedFareInr, string RiderId);

public partial class Program;
