using TurnOva.API;
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
        };
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
builder.Services.AddSignalR();
builder.Services.AddSingleton<ConcurrentDictionary<string, DemoRide>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, string>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverOnboardingProfile>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverUnion>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverUnionAssignment>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverWorkActivity>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverMonthlyPayslip>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverRideReservation>>();
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
    var cosmosOptions = new CosmosDbOptions
    {
        Endpoint = cosmosEndpoint!,
        Key = cosmosKey!,
        DatabaseName = databaseName
    };
    cosmosOptions.Validate();

    var cosmosClient = new CosmosClient(cosmosOptions.Endpoint, cosmosOptions.Key, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    });

    builder.Services.AddSingleton(cosmosClient);
    builder.Services.AddSingleton(cosmosOptions);
    builder.Services.AddSingleton<ICosmosMigration, InitialSchemaMigration>();
    builder.Services.AddSingleton<ICosmosMigration, RideIdempotencyMigration>();
    builder.Services.AddSingleton<ICosmosMigration, UnionManagementMigration>();
    builder.Services.AddSingleton<ICosmosMigration, DriverPayslipsMigration>();
    builder.Services.AddSingleton<ICosmosMigration, RideOffersMigration>();
    builder.Services.AddSingleton<ICosmosMigration, DriverRideReservationsMigration>();
    builder.Services.AddSingleton<IDriverRideReservationStore>(sp => new CosmosDriverRideReservationStore(sp.GetRequiredService<CosmosClient>().GetContainer(databaseName, "DriverRideReservations")));
    builder.Services.AddSingleton<IRideOfferStore>(sp =>
        new CosmosRideOfferStore(sp.GetRequiredService<CosmosClient>().GetContainer(databaseName, "RideOffers")));
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
    builder.Services.AddSingleton<IRideOfferStore, InMemoryRideOfferStore>();
    builder.Services.AddSingleton<IDriverRideReservationStore, InMemoryDriverRideReservationStore>();
}

builder.Services.AddSingleton<IRideOfferAdvancer>(sp => new RideOfferAdvancer(
    sp.GetRequiredService<IRideOfferStore>(), sp.GetRequiredService<IQueueStore>(),
    sp.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>(),
    sp.GetRequiredService<ConcurrentDictionary<string, DemoRide>>(),
    useCosmos ? sp.GetRequiredService<CosmosClient>().GetContainer(databaseName, "Drivers") : null,
    useCosmos ? sp.GetRequiredService<CosmosClient>().GetContainer(databaseName, "Rides") : null,
    sp.GetRequiredService<IHubContext<RideTrackingHub>>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRideOfferExpiryProcessor, RideOfferExpiryProcessor>();
builder.Services.Configure<DispatchOfferExpiryOptions>(builder.Configuration.GetSection("Dispatch:OfferExpiry"));
builder.Services.AddHostedService<RideOfferExpiryWorker>();
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
app.MapHub<RideTrackingHub>("/hubs/rides").RequireAuthorization();

var rides = app.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
var idempotencyKeys = app.Services.GetRequiredService<ConcurrentDictionary<string, string>>();
Container? rideContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "Rides") : null;
var driverRideReservations = app.Services.GetRequiredService<IDriverRideReservationStore>();
Container? idempotencyContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "RideIdempotency") : null;
Container? driverContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "Drivers") : null;
var driverProfiles = app.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
var unions = app.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>();
var unionAssignments = app.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>();
var unionAudit = app.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
Container? unionContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "Unions") : null;
Container? unionAssignmentContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "DriverUnionAssignments") : null;
Container? unionAuditContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "DriverUnionAudit") : null;
var workActivities = app.Services.GetRequiredService<ConcurrentDictionary<string, DriverWorkActivity>>();
var monthlyPayslips = app.Services.GetRequiredService<ConcurrentDictionary<string, DriverMonthlyPayslip>>();
Container? workActivityContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "DriverWorkActivities") : null;
Container? payslipContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "DriverPayslips") : null;

async Task RecordWorkActivityAsync(string driverId, string rideId, string activityType, DateTimeOffset occurredAtUtc, CancellationToken ct)
{
    var activityId = $"{rideId}:{activityType}";
    var activity = new DriverWorkActivity(activityId, driverId, rideId, activityType, occurredAtUtc);
    if (workActivityContainer is null)
    {
        workActivities.TryAdd($"{driverId}|{activityId}", activity);
        return;
    }
    try { await workActivityContainer.CreateItemAsync(activity, new PartitionKey(driverId), cancellationToken: ct); }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { }
}

async Task<IReadOnlyList<DriverWorkActivity>> GetWorkActivitiesAsync(string driverId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
{
    if (workActivityContainer is null)
        return workActivities.Values.Where(item => item.DriverId == driverId && item.OccurredAtUtc >= start && item.OccurredAtUtc < end).ToArray();
    var query = new QueryDefinition("SELECT * FROM c WHERE c.driverId = @driverId AND c.occurredAtUtc >= @start AND c.occurredAtUtc < @end")
        .WithParameter("@driverId", driverId).WithParameter("@start", start).WithParameter("@end", end);
    var results = new List<DriverWorkActivity>();
    using var iterator = workActivityContainer.GetItemQueryIterator<DriverWorkActivity>(query,
        requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(driverId) });
    while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(ct));
    return results;
}

async Task<DriverMonthlyPayslip?> FindPayslipAsync(string driverId, string payslipId, CancellationToken ct)
{
    if (payslipContainer is null)
        return monthlyPayslips.TryGetValue($"{driverId}|{payslipId}", out var value) ? value : null;
    try { return (await payslipContainer.ReadItemAsync<DriverMonthlyPayslip>(payslipId, new PartitionKey(driverId), cancellationToken: ct)).Resource; }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
}

async Task<bool> HasActiveRideAsync(Container container, QueryDefinition query, CancellationToken ct)
{
    using var iterator = container.GetItemQueryIterator<DemoRide>(query, requestOptions: new QueryRequestOptions { MaxItemCount = 1 });
    while (iterator.HasMoreResults)
        if ((await iterator.ReadNextAsync(ct)).Count > 0) return true;
    return false;
}

async Task SavePayslipIfAbsentAsync(DriverMonthlyPayslip payslip, CancellationToken ct)
{
    if (payslipContainer is null)
    {
        monthlyPayslips.TryAdd($"{payslip.DriverId}|{payslip.PayslipId}", payslip);
        return;
    }
    try { await payslipContainer.CreateItemAsync(payslip, new PartitionKey(payslip.DriverId), cancellationToken: ct); }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { }
}

async Task<IReadOnlyList<DriverUnionAuditEvent>> GetUnionAuditForPeriodAsync(string driverId, DateTimeOffset end, CancellationToken ct)
{
    var all = await GetUnionAuditAsync(driverId, ct);
    return all.Where(item => item.OccurredAtUtc < end).OrderBy(item => item.OccurredAtUtc).ToArray();
}

bool IsValidDriverSelfie(string? base64)
{
    if (string.IsNullOrWhiteSpace(base64) || base64.Length > 410_000) return false;
    try
    {
        var bytes = Convert.FromBase64String(base64);
        return bytes.Length is > 0 and <= 300_000 &&
            bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
    }
    catch (FormatException) { return false; }
}

bool IsOpsAuthorized(HttpContext context)
{
    var configuredKey = app.Configuration["DriverOnboarding:AdminApiKey"];
    var suppliedKey = context.Request.Headers["X-Driver-Admin-Key"].ToString();
    if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Length < 32) return false;
    var expected = Encoding.UTF8.GetBytes(configuredKey);
    var supplied = Encoding.UTF8.GetBytes(suppliedKey);
    return expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied);
}

async Task<DriverUnion?> FindUnionAsync(string unionId, CancellationToken ct)
{
    if (unionContainer is null) return unions.TryGetValue(unionId, out var value) ? value : null;
    try { return (await unionContainer.ReadItemAsync<DriverUnion>(unionId, new PartitionKey(unionId), cancellationToken: ct)).Resource; }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
}

async Task SaveUnionAsync(DriverUnion union, CancellationToken ct)
{
    if (unionContainer is null) { unions[union.UnionId] = union; return; }
    await unionContainer.UpsertItemAsync(union, new PartitionKey(union.UnionId), cancellationToken: ct);
}

async Task<DriverUnionAssignment?> FindUnionAssignmentAsync(string driverId, CancellationToken ct)
{
    if (unionAssignmentContainer is null) return unionAssignments.TryGetValue(driverId, out var value) ? value : null;
    try { return (await unionAssignmentContainer.ReadItemAsync<DriverUnionAssignment>(driverId, new PartitionKey(driverId), cancellationToken: ct)).Resource; }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
}

async Task SaveUnionAssignmentAsync(DriverUnionAssignment assignment, CancellationToken ct)
{
    if (unionAssignmentContainer is null) { unionAssignments[assignment.DriverId] = assignment; return; }
    await unionAssignmentContainer.UpsertItemAsync(assignment, new PartitionKey(assignment.DriverId), cancellationToken: ct);
}

async Task SaveUnionAuditAsync(DriverUnionAuditEvent audit, CancellationToken ct)
{
    if (unionAuditContainer is null) { unionAudit[audit.DriverId + "|" + audit.EventId] = audit; return; }
    await unionAuditContainer.CreateItemAsync(audit, new PartitionKey(audit.DriverId), cancellationToken: ct);
}

async Task<IReadOnlyList<DriverUnion>> ListUnionsAsync(CancellationToken ct)
{
    if (unionContainer is null) return unions.Values.OrderBy(item => item.Name).ToArray();
    var results = new List<DriverUnion>();
    using var iterator = unionContainer.GetItemQueryIterator<DriverUnion>(new QueryDefinition("SELECT * FROM c"));
    while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(ct));
    return results.OrderBy(item => item.Name).ToArray();
}

async Task<IReadOnlyList<DriverUnionAuditEvent>> GetUnionAuditAsync(string driverId, CancellationToken ct)
{
    if (unionAuditContainer is null) return unionAudit.Values.Where(item => item.DriverId == driverId).OrderByDescending(item => item.OccurredAtUtc).ToArray();
    var query = new QueryDefinition("SELECT * FROM c WHERE c.driverId = @driverId").WithParameter("@driverId", driverId);
    var results = new List<DriverUnionAuditEvent>();
    using var iterator = unionAuditContainer.GetItemQueryIterator<DriverUnionAuditEvent>(query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(driverId) });
    while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(ct));
    return results.OrderByDescending(item => item.OccurredAtUtc).ToArray();
}

async Task<DriverOnboardingProfile?> FindDriverProfileAsync(string driverId, CancellationToken ct)
{
    if (driverContainer is null)
        return driverProfiles.TryGetValue(driverId, out var profile) ? profile : null;
    try
    {
        return (await driverContainer.ReadItemAsync<DriverOnboardingProfile>(driverId,
            new PartitionKey(driverId), cancellationToken: ct)).Resource;
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return null;
    }
}

async Task SaveDriverProfileAsync(DriverOnboardingProfile profile, CancellationToken ct)
{
    if (driverContainer is null)
    {
        driverProfiles[profile.DriverId] = profile;
        return;
    }
    await driverContainer.UpsertItemAsync(profile, new PartitionKey(profile.DriverId), cancellationToken: ct);
}

async Task<DriverOnboardingProfile?> GetApprovedDriverProfileAsync(string driverId, CancellationToken ct)
{
    var profile = await FindDriverProfileAsync(driverId, ct);
    return profile?.Status == "Approved" ? profile : null;
}

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
string? ResolveDispatchZone(double latitude, double longitude)
{
    var zones = app.Configuration.GetSection("Dispatch:Zones").Get<DispatchZoneConfig[]>() ?? [];
    return zones.FirstOrDefault(zone =>
        latitude >= zone.MinLatitude && latitude <= zone.MaxLatitude &&
        longitude >= zone.MinLongitude && longitude <= zone.MaxLongitude)?.ZoneId;
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

app.MapGet("/health/ready", async (CancellationToken ct) =>
{
    if (!useCosmos)
        return Results.Ok(new { service = "TurnOva.API", status = "ready", storage = storageMode });

    try
    {
        await app.Services.GetRequiredService<CosmosClient>()
            .GetDatabase(databaseName).ReadAsync(cancellationToken: ct);
        return Results.Ok(new { service = "TurnOva.API", status = "ready", storage = storageMode, database = databaseName });
    }
    catch (CosmosException ex)
    {
        app.Logger.LogWarning(ex, "Cosmos DB readiness check failed.");
        return Results.Problem(
            "The configured Cosmos DB database is not reachable.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Storage dependency unavailable");
    }
});


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

var onboarding = app.MapGroup("/api/v1/driver/onboarding").RequireAuthorization(policy => policy.RequireRole("Driver"));
onboarding.MapGet("/me", async (ClaimsPrincipal user, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var profile = await FindDriverProfileAsync(driverId, ct);
    return profile is null ? Results.NoContent() : Results.Ok(profile);
});
onboarding.MapPut("/selfie", async (ClaimsPrincipal user, DriverSelfieRequest request, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    if (!IsValidDriverSelfie(request.ProfilePhotoBase64))
        return Results.BadRequest(new { message = "Provide a valid JPEG selfie no larger than 300 KB." });
    var profile = await FindDriverProfileAsync(driverId, ct);
    if (profile is null) return Results.NotFound(new { message = "Complete driver onboarding before adding a profile selfie." });
    var updated = profile with
    {
        ProfilePhotoBase64 = request.ProfilePhotoBase64,
        ProfilePhotoContentType = "image/jpeg",
        ProfilePhotoCapturedAtUtc = DateTimeOffset.UtcNow,
        ProfilePhotoApprovalStatus = "Pending",
        ProfilePhotoApprovedByUnionId = null,
        ProfilePhotoReviewedAtUtc = null,
        ProfilePhotoReviewReason = null
    };
    await SaveDriverProfileAsync(updated, ct);
    return Results.Ok(updated);
});

onboarding.MapPost("/", async (ClaimsPrincipal user, DriverOnboardingRequest request, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    if (!IsValidDriverSelfie(request.ProfilePhotoBase64))
        return Results.BadRequest(new { message = "A valid JPEG selfie is required. Capture it in the app; maximum decoded size is 300 KB." });
    if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 120 ||
        string.IsNullOrWhiteSpace(request.ZoneId) || request.ZoneId.Trim().Length > 80 ||
        string.IsNullOrWhiteSpace(request.PhoneNumber) || request.PhoneNumber.Any(c => !char.IsDigit(c) && c != '+') ||
        request.PhoneNumber.Length is < 10 or > 16 ||
        string.IsNullOrWhiteSpace(request.VehicleMake) || request.VehicleMake.Trim().Length > 60 ||
        string.IsNullOrWhiteSpace(request.VehicleModel) || request.VehicleModel.Trim().Length > 60 ||
        string.IsNullOrWhiteSpace(request.VehicleRegistration) || request.VehicleRegistration.Trim().Length > 20 ||
        request.PassengerCapacity is < 1 or > 8 ||
        request.ServiceClass is not ("scooty" or "bike" or "auto" or "cab") ||
        request.Documents is null || request.Documents.Count is < 1 or > 8 ||
        request.Documents.Any(document => document is null ||
            document.DocumentType is not ("DrivingLicence" or "VehicleRegistration" or "Insurance" or "Permit" or "ProfilePhoto") ||
            string.IsNullOrWhiteSpace(document.StorageReference) || document.StorageReference.Length > 200 ||
            document.StorageReference.Contains("..", StringComparison.Ordinal) ||
            Uri.TryCreate(document.StorageReference, UriKind.Absolute, out _)))
        return Results.BadRequest(new { message = "Provide valid driver, vehicle and document metadata. Upload files to private object storage and submit opaque storage references only." });

    var existing = await FindDriverProfileAsync(driverId, ct);
    if (existing?.Status is "Approved" or "Suspended")
        return Results.Conflict(new { message = "An approved or suspended driver profile cannot be replaced through onboarding." });

    var profile = new DriverOnboardingProfile(
        driverId, driverId, request.ZoneId.Trim(), request.FullName.Trim(), request.PhoneNumber.Trim(),
        request.VehicleMake.Trim(), request.VehicleModel.Trim(), request.VehicleRegistration.Trim().ToUpperInvariant(),
        request.ServiceClass, request.PassengerCapacity,
        request.Documents.Select(document => new DriverDocumentReference(document.DocumentType, document.StorageReference)).ToArray(),
        "Pending", existing?.SubmittedAtUtc ?? DateTimeOffset.UtcNow,
        ProfilePhotoBase64: request.ProfilePhotoBase64,
        ProfilePhotoContentType: "image/jpeg",
        ProfilePhotoCapturedAtUtc: DateTimeOffset.UtcNow,
        ProfilePhotoApprovalStatus: "Pending");
    await SaveDriverProfileAsync(profile, ct);
    return Results.Accepted("/api/v1/driver/onboarding/me", profile);
});

app.MapGet("/api/v1/ops/drivers/{driverId}", async (HttpContext context, string driverId, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    var profile = await FindDriverProfileAsync(driverId, ct);
    if (profile is null) return Results.NotFound(new { message = "Driver profile not found." });
    var assignment = await FindUnionAssignmentAsync(driverId, ct);
    return Results.Ok(new { driver = profile, unionAssignment = assignment });
});

app.MapPost("/api/v1/ops/drivers/{driverId}/review", async (
    HttpContext context, string driverId, DriverReviewRequest request, QueueService queueService, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context))
    {
        if (string.IsNullOrWhiteSpace(app.Configuration["DriverOnboarding:AdminApiKey"]) ||
            app.Configuration["DriverOnboarding:AdminApiKey"]!.Length < 32)
            return Results.Problem("Operations review requires a configured admin key of at least 32 characters.", statusCode: 503);
        return Results.Unauthorized();
    }
    if (request.Status is not ("Approved" or "Rejected" or "Suspended") ||
        (request.Status != "Approved" && string.IsNullOrWhiteSpace(request.Reason)))
        return Results.BadRequest(new { message = "Status must be Approved, Rejected or Suspended; a reason is required unless approving." });

    var profile = await FindDriverProfileAsync(driverId, ct);
    if (profile is null) return Results.NotFound();
    if (profile.Status == "Suspended" && request.Status == "Approved")
        return Results.Conflict(new { message = "A suspended driver cannot be re-approved through this endpoint." });
    if (request.Status == "Approved" &&
        (!profile.Documents.Any(document => document.DocumentType == "DrivingLicence") ||
         !profile.Documents.Any(document => document.DocumentType == "VehicleRegistration")))
        return Results.Conflict(new { message = "A driving licence and vehicle registration reference are required before approval." });
    var reviewed = profile with
    {
        Status = request.Status,
        Documents = profile.Documents.Select(document => document with { Status = request.Status }).ToArray(),
        ReviewedAtUtc = DateTimeOffset.UtcNow,
        ReviewReason = request.Reason?.Trim()
    };
    await SaveDriverProfileAsync(reviewed, ct);
    if (request.Status is "Rejected" or "Suspended")
        await queueService.LeaveAsync(driverId, profile.ZoneId, profile.ServiceClass, ct);
    return Results.Ok(reviewed);
});


var opsUnions = app.MapGroup("/api/v1/ops/unions");
opsUnions.MapPost("/", async (HttpContext context, CreateUnionRequest request, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120 ||
        string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 24 ||
        request.ZoneIds is null || request.ZoneIds.Count == 0 ||
        request.ZoneIds.Any(zone => string.IsNullOrWhiteSpace(zone) || zone.Trim().Length > 80) ||
        request.ServiceClasses is null || request.ServiceClasses.Count == 0 ||
        request.ServiceClasses.Any(serviceClass => serviceClass is not ("scooty" or "bike" or "auto" or "cab")))
        return Results.BadRequest(new { message = "Provide a name, unique code, at least one service zone and supported service class." });

    var code = request.Code.Trim().ToUpperInvariant();
    var existing = await ListUnionsAsync(ct);
    if (existing.Any(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { message = "A union with this code already exists." });

    var union = new DriverUnion(Guid.NewGuid().ToString("N"), request.Name.Trim(), code,
        request.Location?.Trim(), request.ManagerName?.Trim(), request.ManagerPhone?.Trim(),
        request.ZoneIds.Select(zone => zone.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        request.ServiceClasses.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    await SaveUnionAsync(union, ct);
    return Results.Created($"/api/v1/ops/unions/{union.UnionId}", union);
});

opsUnions.MapGet("/", async (HttpContext context, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    return Results.Ok(await ListUnionsAsync(ct));
});

opsUnions.MapGet("/{unionId}", async (HttpContext context, string unionId, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    var union = await FindUnionAsync(unionId, ct);
    return union is null ? Results.NotFound() : Results.Ok(union);
});

opsUnions.MapPut("/{unionId}/status", async (HttpContext context, string unionId, UpdateUnionStatusRequest request, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    if (request.Status is not ("Active" or "Inactive" or "Suspended"))
        return Results.BadRequest(new { message = "Status must be Active, Inactive or Suspended." });
    var union = await FindUnionAsync(unionId, ct);
    if (union is null) return Results.NotFound();
    var updated = union with { Status = request.Status, UpdatedAtUtc = DateTimeOffset.UtcNow };
    await SaveUnionAsync(updated, ct);
    return Results.Ok(updated);
});

opsUnions.MapGet("/{unionId}/drivers", async (HttpContext context, string unionId, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    var union = await FindUnionAsync(unionId, ct);
    if (union is null) return Results.NotFound();
    IReadOnlyList<DriverUnionAssignment> assignments;
    if (unionAssignmentContainer is null)
        assignments = unionAssignments.Values.Where(item => item.UnionId == unionId && item.Status == "Active").ToArray();
    else
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.unionId = @unionId AND c.status = 'Active'").WithParameter("@unionId", unionId);
        var results = new List<DriverUnionAssignment>();
        using var iterator = unionAssignmentContainer.GetItemQueryIterator<DriverUnionAssignment>(query);
        while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(ct));
        assignments = results;
    }

    var drivers = new List<object>();
    foreach (var assignment in assignments)
    {
        var profile = await FindDriverProfileAsync(assignment.DriverId, ct);
        if (profile is null) continue;
        drivers.Add(new
        {
            assignment.DriverId, assignment.UnionId, assignment.UnionName, assignment.Status,
            assignment.AssignedAtUtc, assignment.UpdatedAtUtc, assignment.AssignmentReason,
            Driver = new
            {
                profile.DriverId, profile.FullName, profile.PhoneNumber, profile.ZoneId,
                profile.VehicleMake, profile.VehicleModel, profile.VehicleRegistration,
                profile.ServiceClass, profile.PassengerCapacity, profile.Status,
                ProfilePhotoBase64 = profile.ProfilePhotoApprovalStatus == "Approved" && profile.ProfilePhotoApprovedByUnionId == unionId ? profile.ProfilePhotoBase64 : null,
                profile.ProfilePhotoContentType, profile.ProfilePhotoCapturedAtUtc,
                profile.ProfilePhotoApprovalStatus
            }
        });
    }
    return Results.Ok(drivers);
});

opsUnions.MapPut("/{unionId}/drivers/{driverId}/selfie-review", async (
    HttpContext context, string unionId, string driverId, DriverSelfieReviewRequest request, CancellationToken ct) =>
{
    // Interim authorization: the union-scoped review endpoint is protected by the existing operations admin key.
    // Replace this with authenticated union-manager RBAC before enabling self-service union administration.
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    if (request.Status is not ("Approved" or "Rejected"))
        return Results.BadRequest(new { message = "Selfie review status must be Approved or Rejected." });
    if (request.Status == "Rejected" && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 300))
        return Results.BadRequest(new { message = "A rejection reason of 1–300 characters is required." });

    var union = await FindUnionAsync(unionId, ct);
    if (union is null) return Results.NotFound(new { message = "Union not found." });
    var assignment = await FindUnionAssignmentAsync(driverId, ct);
    if (assignment is null || assignment.UnionId != unionId || assignment.Status != "Active")
        return Results.Conflict(new { message = "Only the driver's currently assigned union can review this selfie." });
    var profile = await FindDriverProfileAsync(driverId, ct);
    if (profile is null || string.IsNullOrWhiteSpace(profile.ProfilePhotoBase64))
        return Results.NotFound(new { message = "Driver selfie not found." });

    var updated = profile with
    {
        ProfilePhotoApprovalStatus = request.Status,
        ProfilePhotoApprovedByUnionId = request.Status == "Approved" ? unionId : null,
        ProfilePhotoReviewedAtUtc = DateTimeOffset.UtcNow,
        ProfilePhotoReviewReason = request.Status == "Rejected" ? request.Reason!.Trim() : null
    };
    await SaveDriverProfileAsync(updated, ct);
    return Results.Ok(new
    {
        driverId,
        unionId,
        status = updated.ProfilePhotoApprovalStatus,
        reviewedAtUtc = updated.ProfilePhotoReviewedAtUtc,
        reason = updated.ProfilePhotoReviewReason
    });
});

opsUnions.MapGet("/{unionId}/drivers/{driverId}", async (HttpContext context, string unionId, string driverId, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    var union = await FindUnionAsync(unionId, ct);
    if (union is null) return Results.NotFound(new { message = "Union not found." });
    var assignment = await FindUnionAssignmentAsync(driverId, ct);
    if (assignment is null || assignment.UnionId != unionId || assignment.Status != "Active")
        return Results.NotFound(new { message = "Driver is not an active member of this union." });
    var profile = await FindDriverProfileAsync(driverId, ct);
    if (profile is null) return Results.NotFound(new { message = "Driver profile not found." });
    return Results.Ok(new
    {
        assignment,
        driver = new
        {
            profile.DriverId, profile.FullName, profile.PhoneNumber, profile.ZoneId,
            profile.VehicleMake, profile.VehicleModel, profile.VehicleRegistration,
            profile.ServiceClass, profile.PassengerCapacity, profile.Status,
            profile.ProfilePhotoBase64, profile.ProfilePhotoContentType, profile.ProfilePhotoCapturedAtUtc,
            profile.ProfilePhotoApprovalStatus, profile.ProfilePhotoApprovedByUnionId,
            profile.ProfilePhotoReviewedAtUtc, profile.ProfilePhotoReviewReason
        }
    });
});

app.MapPost("/api/v1/ops/drivers/{driverId}/union-assignment", async (
    HttpContext context, string driverId, AssignDriverUnionRequest request, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 300)
        return Results.BadRequest(new { message = "A transfer/assignment reason of 1–300 characters is required." });
    var profile = await FindDriverProfileAsync(driverId, ct);
    if (profile is null) return Results.NotFound(new { message = "Driver onboarding profile not found." });
    if (profile.Status != "Approved") return Results.Conflict(new { message = "Only approved drivers can be assigned to a union." });
    if (string.IsNullOrWhiteSpace(profile.ProfilePhotoBase64))
        return Results.Conflict(new { message = "Driver must capture a union-joining selfie before assignment." });
    var union = await FindUnionAsync(request.UnionId, ct);
    if (union is null) return Results.NotFound(new { message = "Union not found." });
    if (union.Status != "Active") return Results.Conflict(new { message = "Drivers can only be assigned to an active union." });
    if (!union.ZoneIds.Contains(profile.ZoneId, StringComparer.OrdinalIgnoreCase) ||
        !union.ServiceClasses.Contains(profile.ServiceClass, StringComparer.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "The union does not support this driver's approved zone and service class." });

    var previous = await FindUnionAssignmentAsync(driverId, ct);
    if (previous?.UnionId == union.UnionId && previous.Status == "Active")
        return Results.Conflict(new { message = "The driver is already assigned to this union." });

    var now = DateTimeOffset.UtcNow;
    var assignment = new DriverUnionAssignment(driverId, union.UnionId, union.Name, "Active",
        previous?.AssignedAtUtc ?? now, now, request.Reason.Trim());
    await SaveUnionAssignmentAsync(assignment, ct);
    await SaveUnionAuditAsync(new DriverUnionAuditEvent(Guid.NewGuid().ToString("N"), driverId,
        previous?.UnionId, previous?.UnionName, union.UnionId, union.Name,
        previous is null ? "Assigned" : "Transferred", request.Reason.Trim(),
        context.User.Identity?.Name ?? "Operations", now), ct);
    return Results.Ok(assignment);
});

app.MapGet("/api/v1/ops/drivers/{driverId}/union-history", async (
    HttpContext context, string driverId, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    return Results.Ok(await GetUnionAuditAsync(driverId, ct));
});


app.MapGet("/api/v1/driver/payslips/{year:int}/{month:int}.pdf", async (
    ClaimsPrincipal user, int year, int month, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    if (year < 2020 || year > DateTime.UtcNow.Year || month is < 1 or > 12)
        return Results.BadRequest(new { message = "A valid payslip year and month are required." });
    var indiaTimeZoneOffset = TimeSpan.FromMinutes(330);
    var periodStartLocal = new DateTimeOffset(year, month, 1, 0, 0, 0, indiaTimeZoneOffset);
    var periodEndLocal = periodStartLocal.AddMonths(1);
    var periodStart = periodStartLocal.ToUniversalTime();
    var periodEnd = periodEndLocal.ToUniversalTime();
    if (periodStart >= DateTimeOffset.UtcNow)
        return Results.BadRequest(new { message = "Payslips are available only for completed months." });

    var payslipId = $"{driverId}-{year:D4}-{month:D2}";
    var payslip = await FindPayslipAsync(driverId, payslipId, ct);
    if (payslip is null)
    {
        var assignmentHistory = await GetUnionAuditForPeriodAsync(driverId, periodEnd, ct);
        if (assignmentHistory.Any(item => item.OccurredAtUtc >= periodStart))
            return Results.Conflict(new { message = "Union assignment changes within a pay period are not supported yet. Ask Operations to reconcile the period before requesting this PDF." });
        var employerEvent = assignmentHistory.LastOrDefault();
        if (employerEvent is null)
            return Results.Conflict(new { message = "No union assignment was recorded for this pay period." });
        var union = await FindUnionAsync(employerEvent.NewUnionId, ct);
        if (union is null)
            return Results.Conflict(new { message = "The employer union record for this pay period is unavailable." });
        var profile = await FindDriverProfileAsync(driverId, ct);
        if (profile is null) return Results.Conflict(new { message = "Driver profile is unavailable." });
        var activities = await GetWorkActivitiesAsync(driverId, periodStart, periodEnd, ct);
        var completedActivities = activities.Where(item => item.ActivityType == "Completed").ToArray();
        payslip = new DriverMonthlyPayslip(
            payslipId, driverId, profile.FullName, union.UnionId, union.Name,
            year, month, periodStartLocal, periodEndLocal.AddTicks(-1),
            completedActivities.Select(item => item.OccurredAtUtc.ToOffset(indiaTimeZoneOffset).Date).Distinct().Count(),
            activities.Count(item => item.ActivityType == "Accepted"),
            completedActivities.Length,
            activities.Count(item => item.ActivityType == "Rejected"),
            DateTimeOffset.UtcNow);
        await SavePayslipIfAbsentAsync(payslip, ct);
        payslip = await FindPayslipAsync(driverId, payslipId, ct) ?? payslip;
    }
    var pdf = DriverPayslipPdf.Generate(payslip);
    return Results.File(pdf, "application/pdf", $"TurnOva-Payslip-{year:D4}-{month:D2}.pdf");
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapGet("/api/v1/driver/availability", async (ClaimsPrincipal user, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var profile = await FindDriverProfileAsync(driverId, ct);
    var status = profile?.Status == "Approved" ? profile.AvailabilityStatus : "Offline";
    return Results.Ok(new { availabilityStatus = status, isOnline = status == "Online" });
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/driver/availability", async (
    ClaimsPrincipal user, DriverAvailabilityRequest request, QueueService queueService, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var profile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (profile is null) return Results.Conflict(new { message = "Only an approved driver can change availability." });

    if (request.IsOnline)
    {
        await SaveDriverProfileAsync(profile with { AvailabilityStatus = "Online" }, ct);
        try
        {
            var current = await queueService.GetPositionedQueueAsync(profile.ZoneId, profile.ServiceClass, ct);
            if (!current.Any(entry => entry.DriverId == driverId))
                await queueService.JoinAsync(new DriverQueueEntry(driverId, profile.ZoneId, profile.ServiceClass, DateTimeOffset.UtcNow, 0, true), ct);
        }
        catch
        {
            await SaveDriverProfileAsync(profile with { AvailabilityStatus = "Offline" }, ct);
            throw;
        }
        var queue = await queueService.GetPositionedQueueAsync(profile.ZoneId, profile.ServiceClass, ct);
        var position = queue.ToList().FindIndex(entry => entry.DriverId == driverId) + 1;
        return Results.Ok(new { availabilityStatus = "Online", isOnline = true, isQueued = position > 0, position });
    }

    var activeRideQuery = new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.driverId = @driverId AND c.status IN ('Accepted', 'Arrived', 'InProgress')")
        .WithParameter("@driverId", driverId);
    var hasActiveRide = rideContainer is null
        ? rides.Values.Any(ride => ride.DriverId == driverId && (ride.Status is "Accepted" or "Arrived" or "InProgress"))
        : await HasActiveRideAsync(rideContainer, activeRideQuery, ct);
    if (hasActiveRide) return Results.Conflict(new { message = "Complete your active ride before going offline." });

    await queueService.LeaveAsync(driverId, profile.ZoneId, profile.ServiceClass, ct);
    await SaveDriverProfileAsync(profile with { AvailabilityStatus = "Offline" }, ct);
    return Results.Ok(new { availabilityStatus = "Offline", isOnline = false, isQueued = false, position = (int?)null });
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapGet("/api/v1/driver/union", async (ClaimsPrincipal user, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var assignment = await FindUnionAssignmentAsync(driverId, ct);
    return assignment is null || assignment.Status != "Active" ? Results.NoContent() : Results.Ok(assignment);
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapGet("/api/v1/zones", () => Results.Ok(new[]
{
    new { id = "hyderabad-central", name = "Hyderabad Central", serviceClasses = new[] { "scooty", "bike", "auto", "cab" } }
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

    var approvedProfile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (approvedProfile is null)
        return Results.Conflict(new { message = "Driver onboarding must be approved before joining a queue." });
    if (approvedProfile.AvailabilityStatus != "Online")
        return Results.Conflict(new { message = "Go online before joining the FIFO queue." });

    try
    {
        var serviceClass = string.IsNullOrWhiteSpace(request.ServiceClass) ? "auto" : request.ServiceClass.Trim();
        if (!string.Equals(serviceClass, approvedProfile.ServiceClass, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.ZoneId.Trim(), approvedProfile.ZoneId, StringComparison.OrdinalIgnoreCase))
            return Results.Forbid();
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

app.MapPost("/api/v1/rides/request", async (HttpContext context, ClaimsPrincipal user, CreateRideRequest request, QueueService queueService, IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Pickup) || string.IsNullOrWhiteSpace(request.Dropoff))
        return Results.BadRequest(new { message = "Pickup and drop-off are required." });
    if (request.Passengers is < 1 or > 6)
        return Results.BadRequest(new { message = "Passenger count must be between 1 and 6." });
    if (request.PickupLatitude is null or < -90 or > 90 || request.PickupLongitude is null or < -180 or > 180)
        return Results.BadRequest(new { message = "Valid pickup coordinates are required." });
    var serviceClass = request.ServiceClass?.Trim().ToLowerInvariant();
    if (serviceClass is not ("scooty" or "bike" or "auto" or "cab"))
        return Results.BadRequest(new { message = "Choose a valid vehicle type: scooty, bike, auto or cab." });
    var dispatchZoneId = ResolveDispatchZone(request.PickupLatitude.Value, request.PickupLongitude.Value);
    if (string.IsNullOrWhiteSpace(dispatchZoneId))
        return Results.BadRequest(new { message = "TurnOva is not available in this pickup zone yet." });
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
        null, null, null, null, ServiceClass: serviceClass, DispatchZoneId: dispatchZoneId);
    try { await SaveRideAsync(ride, ct); }
    catch
    {
        if (idempotencyContainer is null) idempotencyKeys.TryRemove($"{riderId}:{keyId}", out _);
        else await idempotencyContainer.DeleteItemAsync<RideIdempotencyRecord>(keyId, new PartitionKey(riderId), cancellationToken: ct);
        throw;
    }

    // Persist the initial offer before notifying the FIFO head. Repeated rider requests
    // return through the idempotency path above and cannot create a second offer.
    var eligibleQueue = await queueService.GetPositionedQueueAsync(dispatchZoneId, serviceClass, ct);
    var candidate = eligibleQueue.FirstOrDefault();
    if (candidate is not null)
    {
        var profile = await GetApprovedDriverProfileAsync(candidate.DriverId, ct);
        if (profile is not null && profile.AvailabilityStatus == "Online" &&
            profile.PassengerCapacity >= ride.Passengers &&
            string.Equals(profile.ZoneId, dispatchZoneId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.ServiceClass, serviceClass, StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            var lifetimeSeconds = Math.Clamp(app.Configuration.GetValue<int?>("Dispatch:OfferLifetimeSeconds") ?? 30, 10, 120);
            var offerId = Guid.NewGuid().ToString("N");
            var expiresAt = now.AddSeconds(lifetimeSeconds);
            var attempt = new RideOfferAttempt(offerId, candidate.DriverId, now, expiresAt, RideOfferStatuses.Pending);
            var offer = new RideOfferState(ride.RideId, dispatchZoneId, serviceClass, offerId,
                candidate.DriverId, now, expiresAt, RideOfferStatuses.Pending, [attempt]);
            var store = app.Services.GetRequiredService<IRideOfferStore>();
            if (await store.TryCreateAsync(offer, ct))
                await hub.Clients.Group($"driver:{candidate.DriverId}").SendAsync(
                    "RideOfferReceived", new { rideId = ride.RideId, offerId, expiresAtUtc = expiresAt }, ct);
        }
    }
    return Results.Created($"/api/v1/rides/{ride.RideId}", ride);
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

app.MapGet("/api/v1/driver/offers/pending", async (ClaimsPrincipal user, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var store = app.Services.GetRequiredService<IRideOfferStore>();
    var offer = await store.GetPendingForDriverAsync(driverId, ct);
    if (offer is null) return Results.NoContent();
    if (offer.IsExpiredAt(TimeProvider.System.GetUtcNow()))
        return Results.Conflict(new { message = "The offer has expired." });
    return Results.Ok(new
    {
        offer.RideId,
        offer.CurrentOfferId,
        offer.ZoneId,
        offer.ServiceClass,
        offer.OfferedAtUtc,
        offer.ExpiresAtUtc,
        offer.Status
    });
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapGet("/api/v1/driver/rides/available", async (ClaimsPrincipal user, QueueService queueService, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var approvedProfile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (approvedProfile is null || approvedProfile.AvailabilityStatus != "Online") return Results.Forbid();

    // Only the current FIFO head may see actionable ride requests.
    var queue = await queueService.GetPositionedQueueAsync(approvedProfile.ZoneId, approvedProfile.ServiceClass, ct);
    if (queue.FirstOrDefault()?.DriverId != driverId)
        return Results.Ok(Array.Empty<DemoRide>());

    if (rideContainer is null) return Results.Ok(rides.Values.Where(x =>
        x.Status == "Requested" && x.Passengers <= approvedProfile.PassengerCapacity &&
        x.DispatchZoneId == approvedProfile.ZoneId &&
        string.Equals(x.ServiceClass, approvedProfile.ServiceClass, StringComparison.OrdinalIgnoreCase)));
    var query = new QueryDefinition("SELECT * FROM c WHERE c.status = @status").WithParameter("@status", "Requested");
    var iterator = rideContainer.GetItemQueryIterator<DemoRide>(query);
    var results = new List<DemoRide>();
    while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(ct));
    return Results.Ok(results.Where(ride =>
        ride.Passengers <= approvedProfile.PassengerCapacity &&
        string.Equals(ride.DispatchZoneId, approvedProfile.ZoneId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ride.ServiceClass, approvedProfile.ServiceClass, StringComparison.OrdinalIgnoreCase)));
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/{rideId}/accept", async (ClaimsPrincipal user, string rideId, QueueService queueService, IHubContext<RideTrackingHub> hub, IRideOfferStore offerStore, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var approvedProfile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (approvedProfile is null || approvedProfile.AvailabilityStatus != "Online") return Results.Forbid();
    var pendingOffer = await offerStore.GetByRideAsync(rideId, ct);
    if (pendingOffer is not null &&
        (!pendingOffer.IsPending || pendingOffer.DriverId != driverId ||
         pendingOffer.IsExpiredAt(TimeProvider.System.GetUtcNow())))
        return Results.Conflict(new { message = "This ride offer is no longer assigned to you or has expired." });
    var queue = await queueService.GetPositionedQueueAsync(approvedProfile.ZoneId, approvedProfile.ServiceClass, ct);
    if (queue.FirstOrDefault()?.DriverId != driverId)
        return Results.Conflict(new { message = "Only the driver at the front of the eligible FIFO queue can accept a ride." });
    if (rideContainer is null)
    {
        if (!rides.TryGetValue(rideId, out var current) || current.Status != "Requested")
            return Results.Conflict(new { message = "This ride is no longer available." });
        if (!string.Equals(current.DispatchZoneId, approvedProfile.ZoneId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.ServiceClass, approvedProfile.ServiceClass, StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { message = "This ride belongs to a different dispatch zone or vehicle queue." });
        if (current.Passengers > approvedProfile.PassengerCapacity)
            return Results.Conflict(new { message = "This ride exceeds your approved vehicle passenger capacity." });
        if (!await driverRideReservations.TryAcquireAsync(driverId, rideId, ct))
            return Results.Conflict(new { message = "You already have an active ride. Complete or cancel it before accepting another ride." });
        var acceptedInMemory = current with { Status = "Accepted", DriverId = driverId, AcceptedAt = DateTimeOffset.UtcNow, LocationSharingActive = true };
        if (!rides.TryUpdate(rideId, acceptedInMemory, current))
        {
            await driverRideReservations.TryReleaseAsync(driverId, rideId, ct);
            return Results.Conflict(new { message = "Another driver accepted this ride." });
        }
        if (pendingOffer is not null && !string.IsNullOrWhiteSpace(pendingOffer.ETag))
        {
            var resolvedAttempts = pendingOffer.Attempts.Select(attempt =>
                attempt.OfferId == pendingOffer.CurrentOfferId
                    ? attempt with { Status = RideOfferStatuses.Accepted, ResolvedAtUtc = TimeProvider.System.GetUtcNow() }
                    : attempt).ToArray();
            await offerStore.TryReplaceAsync(pendingOffer with
                { Status = RideOfferStatuses.Accepted, Attempts = resolvedAttempts }, pendingOffer.ETag, ct);
        }
        await RecordWorkActivityAsync(driverId, rideId, "Accepted", acceptedInMemory.AcceptedAt!.Value, ct);
        await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = acceptedInMemory.Status }, ct);
        return Results.Ok(acceptedInMemory);
    }
    try
    {
        var response = await rideContainer.ReadItemAsync<DemoRide>(rideId, new PartitionKey(rideId), cancellationToken: ct);
        if (response.Resource.Status != "Requested")
            return Results.Conflict(new { message = "This ride is no longer available." });
        if (!string.Equals(response.Resource.DispatchZoneId, approvedProfile.ZoneId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.Resource.ServiceClass, approvedProfile.ServiceClass, StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { message = "This ride belongs to a different dispatch zone or vehicle queue." });
        if (response.Resource.Passengers > approvedProfile.PassengerCapacity)
            return Results.Conflict(new { message = "This ride exceeds your approved vehicle passenger capacity." });
        if (!await driverRideReservations.TryAcquireAsync(driverId, rideId, ct))
            return Results.Conflict(new { message = "You already have an active ride. Complete or cancel it before accepting another ride." });
        var accepted = response.Resource with { Status = "Accepted", DriverId = driverId, AcceptedAt = DateTimeOffset.UtcNow, LocationSharingActive = true };
        await rideContainer.ReplaceItemAsync(accepted, rideId, new PartitionKey(rideId),
            new ItemRequestOptions { IfMatchEtag = response.ETag }, ct);
        if (pendingOffer is not null && !string.IsNullOrWhiteSpace(pendingOffer.ETag))
        {
            var resolvedAttempts = pendingOffer.Attempts.Select(attempt =>
                attempt.OfferId == pendingOffer.CurrentOfferId
                    ? attempt with { Status = RideOfferStatuses.Accepted, ResolvedAtUtc = TimeProvider.System.GetUtcNow() }
                    : attempt).ToArray();
            await offerStore.TryReplaceAsync(pendingOffer with
                { Status = RideOfferStatuses.Accepted, Attempts = resolvedAttempts }, pendingOffer.ETag, ct);
        }
        await RecordWorkActivityAsync(driverId, rideId, "Accepted", accepted.AcceptedAt!.Value, ct);
        await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = accepted.Status }, ct);
        return Results.Ok(accepted);
    }
    catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
    {
        await driverRideReservations.TryReleaseAsync(driverId, rideId, ct);
        return Results.Conflict(new { message = "This ride is no longer available." });
    }
}).RequireAuthorization(policy => policy.RequireRole("Driver"));


app.MapPost("/api/v1/rides/{rideId}/reject", async (ClaimsPrincipal user, string rideId, QueueService queueService, IRideOfferStore offerStore, IRideOfferAdvancer offerAdvancer, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var profile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (profile is null || profile.AvailabilityStatus != "Online") return Results.Forbid();
    var ride = await FindRideAsync(rideId, ct);
    if (ride is null || ride.Status != "Requested" || ride.Passengers > profile.PassengerCapacity ||
        !string.Equals(ride.DispatchZoneId, profile.ZoneId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(ride.ServiceClass, profile.ServiceClass, StringComparison.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "This ride is not available to reject." });

    var offer = await offerStore.GetByRideAsync(rideId, ct);
    if (offer is not null)
    {
        if (offer.DriverId != driverId)
            return Results.Conflict(new { message = "This ride offer is not assigned to you." });
        if (offer.Status == RideOfferStatuses.Rejected)
            return Results.NoContent();
        if (!offer.IsPending || offer.IsExpiredAt(TimeProvider.System.GetUtcNow()))
            return Results.Conflict(new { message = "This ride offer is no longer pending." });
    }

    var queue = await queueService.GetPositionedQueueAsync(profile.ZoneId, profile.ServiceClass, ct);
    // A persisted offer proves assignment ownership. Skip the head check on retries because
    // the first attempt may already have rotated this driver before the offer update failed.
    if (offer is null && queue.FirstOrDefault()?.DriverId != driverId)
        return Results.Conflict(new { message = "Only the driver at the front of the eligible FIFO queue can reject this ride." });

    // Offer-ID idempotency makes a client retry safe if the process stops after queue rotation.
    var rotated = offer is null
        ? await queueService.MoveToBackAsync(driverId, profile.ZoneId, profile.ServiceClass, ct)
        : await app.Services.GetRequiredService<IQueueStore>().MoveToBackOnceAsync(
            driverId, profile.ZoneId, profile.ServiceClass, offer.CurrentOfferId, ct);
    if (!rotated)
        return Results.Conflict(new { message = "Queue membership changed. Refresh and try again." });

    if (offer is not null && !string.IsNullOrWhiteSpace(offer.ETag))
    {
        var resolvedAt = TimeProvider.System.GetUtcNow();
        var attempts = offer.Attempts.Select(attempt =>
            attempt.OfferId == offer.CurrentOfferId && attempt.Status == RideOfferStatuses.Pending
                ? attempt with { Status = RideOfferStatuses.Rejected, ResolvedAtUtc = resolvedAt }
                : attempt).ToArray();
        if (!await offerStore.TryReplaceAsync(
            offer with { Status = RideOfferStatuses.Rejected, Attempts = attempts },
            offer.ETag, ct))
            return Results.Conflict(new { message = "The offer changed while rejection was being saved. Retry to confirm the result." });
    }

    await RecordWorkActivityAsync(driverId, rideId, "Rejected", DateTimeOffset.UtcNow, ct);
    if (offer is not null) await offerAdvancer.AdvanceAsync(rideId, ct);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/queues/{zoneId}/{serviceClass}/offer-timeout", async (
    string zoneId, string serviceClass, ClaimsPrincipal user, QueueService queueService, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();

    var profile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (profile is null || profile.AvailabilityStatus != "Online") return Results.Forbid();
    if (!string.Equals(profile.ZoneId, zoneId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(profile.ServiceClass, serviceClass, StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    var queue = await queueService.GetPositionedQueueAsync(zoneId, serviceClass, ct);
    if (queue.FirstOrDefault()?.DriverId != driverId)
        return Results.Conflict(new { message = "Only the driver currently at the front of this queue can time out an offer." });

    if (!await queueService.MoveToBackAsync(driverId, zoneId, serviceClass, ct))
        return Results.Conflict(new { message = "Queue membership changed. Refresh and try again." });

    var nextQueue = await queueService.GetPositionedQueueAsync(zoneId, serviceClass, ct);
    return Results.Ok(new
    {
        message = "Offer timed out. Driver moved to the back of the queue.",
        nextDriverId = nextQueue.FirstOrDefault()?.DriverId,
        driverPosition = nextQueue.ToList().FindIndex(entry => entry.DriverId == driverId) + 1
    });
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
    if (!string.IsNullOrWhiteSpace(updated.DriverId))
        await driverRideReservations.TryReleaseAsync(updated.DriverId, rideId, ct);
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
    if (next == "Completed")
    {
        await RecordWorkActivityAsync(driverId!, rideId, "Completed", DateTimeOffset.UtcNow, ct);
        await driverRideReservations.TryReleaseAsync(driverId!, rideId, ct);
    }
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
public sealed record DriverAvailabilityRequest(bool IsOnline);
public sealed record DriverDocumentRequest(string DocumentType, string StorageReference);
public sealed record DriverOnboardingRequest(string ZoneId, string FullName, string PhoneNumber, string VehicleMake, string VehicleModel, string VehicleRegistration, string ServiceClass, int PassengerCapacity, IReadOnlyList<DriverDocumentRequest> Documents, string? ProfilePhotoBase64);
public sealed record DriverSelfieRequest(string ProfilePhotoBase64);
public sealed record DriverSelfieReviewRequest(string Status, string? Reason);
public sealed record DriverReviewRequest(string Status, string? Reason);
public sealed record CreateUnionRequest(string Name, string Code, string? Location, string? ManagerName, string? ManagerPhone, IReadOnlyList<string> ZoneIds, IReadOnlyList<string> ServiceClasses);
public sealed record UpdateUnionStatusRequest(string Status);
public sealed record AssignDriverUnionRequest(string UnionId, string Reason);
public sealed record CreateRideRequest(string Pickup, string Dropoff, int Passengers = 1, double? PickupLatitude = null, double? PickupLongitude = null, string ServiceClass = "auto");
public sealed record DriverLocationRequest(double Latitude, double Longitude);
public sealed record RideIdempotencyRecord(string id, string riderId, string rideId);
public sealed record PasswordRegisterRequest(string Email, string Password, string? Name, bool ReplaceOldest = false);
public sealed record PasswordLoginRequest(string Email, string Password, bool ReplaceOldest = false);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record DemoRide(
    string RideId, string Pickup, string Dropoff, int Passengers,
    string Status, DateTimeOffset RequestedAt, decimal EstimatedFareInr, string RiderId, string? DriverId, double? PickupLatitude, double? PickupLongitude, double? DriverLatitude, double? DriverLongitude, DateTimeOffset? AcceptedAt, DateTimeOffset? LocationUpdatedAt, bool LocationSharingActive = false, string ServiceClass = "auto", string DispatchZoneId = "hyderabad-central");

public sealed record DispatchZoneConfig(string ZoneId, double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude);

public partial class Program;
