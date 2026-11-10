using TurnOva.API;
using TurnOva.API.Assistance;
using TurnOva.API.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
builder.Services.AddHealthChecks();
builder.Services.AddAiAssistance(builder.Configuration);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    // Read configuration when the options are built. This keeps host/test configuration
    // overrides visible and validates the exact origins used by the running application.
    var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    foreach (var origin in corsAllowedOrigins)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) ||
            (parsedOrigin.Scheme != Uri.UriSchemeHttps &&
             !(builder.Environment.IsDevelopment() && parsedOrigin.Scheme == Uri.UriSchemeHttp)) ||
            parsedOrigin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(parsedOrigin.UserInfo) ||
            !string.IsNullOrEmpty(parsedOrigin.Query) ||
            !string.IsNullOrEmpty(parsedOrigin.Fragment))
            throw new InvalidOperationException(
                "Cors:AllowedOrigins must contain explicit HTTPS origins (HTTP is allowed only in Development); wildcards and URL paths are not allowed.");
    }

    policy.AllowAnyHeader().AllowAnyMethod();
    if (corsAllowedOrigins.Length > 0)
        policy.WithOrigins(corsAllowedOrigins);
}));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // WebApplicationFactory and hosting providers may contribute configuration
        // after Program has created the builder. Resolve the validated JWT settings
        // when authentication options are materialized, not during top-level startup.
        var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"];
        var jwtAudience = builder.Configuration["Auth:Jwt:Audience"];
        var jwtSigningKey = builder.Configuration["Auth:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience) ||
            string.IsNullOrWhiteSpace(jwtSigningKey) || jwtSigningKey.Length < 32)
            throw new InvalidOperationException("Configure Auth:Jwt:Issuer, Auth:Jwt:Audience, and a 32+ character Auth:Jwt:SigningKey shared with TriSend.");

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

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Validate(options =>
    {
        var parameters = options.TokenValidationParameters;
        return !string.IsNullOrWhiteSpace(parameters.ValidIssuer) &&
            !string.IsNullOrWhiteSpace(parameters.ValidAudience) &&
            parameters.IssuerSigningKey is SymmetricSecurityKey signingKey &&
            signingKey.KeySize >= 256;
    }, "Configure Auth:Jwt:Issuer, Auth:Jwt:Audience, and a 32+ character Auth:Jwt:SigningKey shared with TriSend.")
    .ValidateOnStart();
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ConcurrentDictionary<string, DemoRide>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, string>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, RideIdempotencyRecord>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverOnboardingProfile>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverUnion>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverUnionAssignment>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverWorkActivity>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverMonthlyPayslip>>();
builder.Services.AddSingleton<ConcurrentDictionary<string, DriverRideReservation>>();
builder.Services.AddOptions<TriSendSettings>()
    .Bind(builder.Configuration.GetSection("TriSend"))
    .Validate(settings =>
        !string.IsNullOrWhiteSpace(settings.BaseUrl) &&
        Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps,
        "TriSend:BaseUrl must be a valid HTTPS URL.")
    .ValidateOnStart();

builder.Services.AddHttpClient("TriSend", (services, client) =>
{
    var settings = services.GetRequiredService<IOptions<TriSendSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl!, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(15);
});


var cosmosEndpoint = builder.Configuration["CosmosDb:Endpoint"];
var cosmosKey = builder.Configuration["CosmosDb:Key"];
var databaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "TurnOvaDb";
var useCosmos = !string.IsNullOrWhiteSpace(cosmosEndpoint) || !string.IsNullOrWhiteSpace(cosmosKey);

if (builder.Environment.IsProduction() && !useCosmos)
    throw new InvalidOperationException("Production requires CosmosDb:Endpoint and CosmosDb:Key. In-memory persistence is development/test only.");

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
    builder.Services.AddSingleton<IRiderActiveBookingReservationStore>(sp =>
        new CosmosRiderActiveBookingReservationStore(sp.GetRequiredService<CosmosClient>().GetContainer(databaseName, "RideIdempotency")));
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
    if (builder.Environment.IsDevelopment())
    {
        var demoDrivers = new[]
        {
            new DriverQueueEntry("DRV-1001", "hyderabad-central", "cab", DateTimeOffset.UtcNow.AddMinutes(-28), 0, true),
            new DriverQueueEntry("DRV-1002", "hyderabad-central", "cab", DateTimeOffset.UtcNow.AddMinutes(-18), 0, true),
            new DriverQueueEntry("DRV-1003", "hyderabad-central", "cab", DateTimeOffset.UtcNow.AddMinutes(-9), 0, true)
        };
        foreach (var driver in demoDrivers)
            await queueStore.AddAsync(driver, CancellationToken.None);
    }

    builder.Services.AddSingleton<IQueueStore>(queueStore);
    builder.Services.AddSingleton<IRideOfferStore, InMemoryRideOfferStore>();
    builder.Services.AddSingleton<IDriverRideReservationStore, InMemoryDriverRideReservationStore>();
    builder.Services.AddSingleton<IRiderActiveBookingReservationStore, InMemoryRiderActiveBookingReservationStore>();
}

builder.Services.AddSingleton<StorageReadinessProbe>(sp =>
{
    Func<CancellationToken, Task> storageProbe;
    if (useCosmos)
    {
        storageProbe = async ct =>
        {
            await sp.GetRequiredService<CosmosClient>()
                .GetDatabase(databaseName).ReadAsync(cancellationToken: ct);
        };
    }
    else
    {
        storageProbe = _ => Task.CompletedTask;
    }

    return new StorageReadinessProbe(storageProbe, TimeSpan.FromSeconds(3));
});

builder.Services.AddSingleton<IRideOfferAdvancer>(sp => new RideOfferAdvancer(
    sp.GetRequiredService<IRideOfferStore>(), sp.GetRequiredService<IQueueStore>(),
    sp.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>(),
    sp.GetRequiredService<ConcurrentDictionary<string, DemoRide>>(),
    useCosmos ? sp.GetRequiredService<CosmosClient>().GetContainer(databaseName, "Drivers") : null,
    useCosmos ? sp.GetRequiredService<CosmosClient>().GetContainer(databaseName, "Rides") : null,
    sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IOptions<DispatchOfferOptions>>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<DispatchOfferOptions>(builder.Configuration.GetSection("Dispatch"));
builder.Services.AddHostedService<OfferNotificationOutboxWorker>();
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

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError("Unhandled API exception. TraceId {TraceId} ExceptionType {ExceptionType}", context.TraceIdentifier, exception?.GetType().Name ?? "Unknown");
    context.Response.Clear();
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await Results.Problem(
        title: "An unexpected error occurred.",
        statusCode: StatusCodes.Status500InternalServerError,
        extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier })
        .ExecuteAsync(context);
}));

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapHealthChecks("/health/live");
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/__test/throw", () =>
    {
        throw new InvalidOperationException("sensitive-test-exception");
    });
}
app.MapHub<RideTrackingHub>("/hubs/rides").RequireAuthorization();
app.MapAiAssistanceEndpoints();

var rides = app.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
var idempotencyKeys = app.Services.GetRequiredService<ConcurrentDictionary<string, string>>();
Container? rideContainer = useCosmos ? app.Services.GetRequiredService<CosmosClient>().GetContainer(databaseName, "Rides") : null;
var driverRideReservations = app.Services.GetRequiredService<IDriverRideReservationStore>();
var riderActiveBookingReservations = app.Services.GetRequiredService<IRiderActiveBookingReservationStore>();
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

async Task ReconcileSelectedRideWorkActivitiesAsync(
    string driverId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken ct)
{
    // Ride audit events share the authoritative ride snapshot. Rebuild selected-driver
    // activity rows before creating the first immutable monthly snapshot so an API
    // interruption between the ride write and the separate ledger write cannot omit work.
    var driverRides = new List<DemoRide>();
    if (rideContainer is null)
    {
        driverRides.AddRange(rides.Values.Where(ride => ride.DriverId == driverId));
    }
    else
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.driverId = @driverId")
            .WithParameter("@driverId", driverId);
        using var iterator = rideContainer.GetItemQueryIterator<DemoRide>(query);
        while (iterator.HasMoreResults)
            driverRides.AddRange(await iterator.ReadNextAsync(ct));
    }

    foreach (var ride in driverRides)
    {
        if (ride.DriverId != driverId ||
            !string.Equals(ride.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(ride.UnionId))
            continue;

        var auditEvents = ride.AuditEvents ?? [];
        var completedEvent = auditEvents.LastOrDefault(item =>
            item.EventType == "RideStatusChanged" &&
            item.NewStatus == "Completed" &&
            item.ActorRole == "Driver" &&
            item.ActorId == driverId);
        if (ride.Status == "Completed" && completedEvent is not null)
            await EnsureCompletedRideSideEffectsAsync(ride, driverId, ct);

        foreach (var auditEvent in auditEvents.Where(item =>
            item.EventType == "RideStatusChanged" &&
            item.ActorRole == "Driver" &&
            item.ActorId == driverId &&
            item.OccurredAtUtc >= periodStart &&
            item.OccurredAtUtc < periodEnd))
        {
            if (auditEvent.NewStatus == "Accepted")
            {
                await RecordWorkActivityAsync(driverId, ride.RideId, "Accepted", auditEvent.OccurredAtUtc, ct);
            }
            else if (auditEvent.NewStatus == "Cancelled" &&
                     auditEvent.PreviousStatus is "Negotiating" or "AgreementConfirmed")
            {
                await RecordWorkActivityAsync(driverId, ride.RideId, "Rejected", auditEvent.OccurredAtUtc, ct);
            }
        }
    }
}

async Task<DriverMonthlyPayslip?> FindPayslipAsync(string driverId, string payslipId, CancellationToken ct)
{
    if (payslipContainer is null)
        return monthlyPayslips.TryGetValue($"{driverId}|{payslipId}", out var value) ? value : null;
    try { return (await payslipContainer.ReadItemAsync<DriverMonthlyPayslip>(payslipId, new PartitionKey(driverId), cancellationToken: ct)).Resource; }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
}

async Task<DriverMonthlyPayslip> SavePayslipIfAbsentAsync(DriverMonthlyPayslip payslip, CancellationToken ct)
{
    if (payslipContainer is null)
        return monthlyPayslips.GetOrAdd($"{payslip.DriverId}|{payslip.PayslipId}", payslip);

    try
    {
        await payslipContainer.CreateItemAsync(payslip, new PartitionKey(payslip.DriverId), cancellationToken: ct);
        return payslip;
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
    {
        // A create conflict means another API instance won. Never fall back to this
        // request's locally calculated metrics if the canonical point read is delayed.
        return await DriverPayslipSnapshotRecovery.ReadCanonicalAfterCreateConflictAsync(
            token => FindPayslipAsync(payslip.DriverId, payslip.PayslipId, token),
            cancellationToken: ct);
    }
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

bool IsUnionManagerFor(ClaimsPrincipal user, string unionId) =>
    user.IsInRole("UnionManager") &&
    user.FindAll("unionId").Any(claim => string.Equals(claim.Value, unionId, StringComparison.Ordinal));

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

async Task EnsureUnionSelfieAuditAsync(
    string driverId, string unionId, string unionName, string status, string? reason,
    string actor, DateTimeOffset reviewedAtUtc, CancellationToken ct)
{
    var eventType = status == "Approved" ? "SelfieApprovedByUnion" : "SelfieRejectedByUnion";
    var auditKey = $"{driverId}|{unionId}|{eventType}|{reviewedAtUtc.UtcDateTime.Ticks}";
    var eventId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(auditKey)));
    if ((await GetUnionAuditAsync(driverId, ct)).Any(item => item.EventId == eventId))
        return;

    try
    {
        await SaveUnionAuditAsync(new DriverUnionAuditEvent(
            eventId, driverId, unionId, unionName, unionId, unionName, eventType,
            reason ?? "Approved by the currently assigned union.", actor, reviewedAtUtc), ct);
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
    {
        // A previous request may have committed the same deterministic audit event before its response was lost.
    }
}

async Task<IReadOnlyList<DriverUnion>> ListUnionsAsync(CancellationToken ct)
{
    if (unionContainer is null) return unions.Values.OrderBy(item => item.Name).ToArray();
    var results = new List<DriverUnion>();
    using var iterator = unionContainer.GetItemQueryIterator<DriverUnion>(new QueryDefinition("SELECT * FROM c"));
    while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(ct));
    return results.OrderBy(item => item.Name).ToArray();
}

async Task<IReadOnlyList<DriverOnboardingProfile>> ListOnlineCabProfilesForDiscoveryAsync(string zoneId, CancellationToken ct)
{
    if (driverContainer is null)
        return driverProfiles.Values.Where(profile =>
            profile.Status == "Approved" &&
            profile.AvailabilityStatus == "Online" &&
            string.Equals(profile.ZoneId, zoneId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase)).ToArray();

    var query = new QueryDefinition(
        "SELECT * FROM c WHERE c.zoneId = @zoneId AND c.status = 'Approved' AND c.availabilityStatus = 'Online' AND c.serviceClass = 'cab'")
        .WithParameter("@zoneId", zoneId);
    var results = new List<DriverOnboardingProfile>();
    using var iterator = driverContainer.GetItemQueryIterator<DriverOnboardingProfile>(query);
    while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(ct));
    return results;
}

async Task<IReadOnlyList<DriverUnionAssignment>> ListActiveUnionAssignmentsAsync(CancellationToken ct)
{
    if (unionAssignmentContainer is null)
        return unionAssignments.Values.Where(item => item.Status == "Active").ToArray();

    var query = new QueryDefinition("SELECT * FROM c WHERE c.status = 'Active'");
    var results = new List<DriverUnionAssignment>();
    using var iterator = unionAssignmentContainer.GetItemQueryIterator<DriverUnionAssignment>(query);
    while (iterator.HasMoreResults) results.AddRange(await iterator.ReadNextAsync(ct));
    return results;
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

async Task<bool> TryCreateDriverProfileAsync(DriverOnboardingProfile profile, CancellationToken ct)
{
    if (driverContainer is null)
        return driverProfiles.TryAdd(profile.DriverId, profile);

    try
    {
        await driverContainer.CreateItemAsync(profile, new PartitionKey(profile.DriverId), cancellationToken: ct);
        return true;
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
    {
        return false;
    }
}

async Task<bool> TryUpdateDriverProfileAsync(
    string driverId,
    Func<DriverOnboardingProfile, DriverOnboardingProfile?> update,
    CancellationToken ct)
{
    if (driverContainer is null)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!driverProfiles.TryGetValue(driverId, out var current))
                return false;
            var updated = update(current);
            if (updated is null)
                return false;
            if (driverProfiles.TryUpdate(driverId, updated, current))
                return true;
        }
        return false;
    }

    for (var attempt = 0; attempt < 3; attempt++)
    {
        ItemResponse<DriverOnboardingProfile> response;
        try
        {
            response = await driverContainer.ReadItemAsync<DriverOnboardingProfile>(
                driverId, new PartitionKey(driverId), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        var updated = update(response.Resource);
        if (updated is null)
            return false;

        try
        {
            await driverContainer.ReplaceItemAsync(
                updated, driverId, new PartitionKey(driverId),
                new ItemRequestOptions { IfMatchEtag = response.ETag }, ct);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            // Retry against the latest profile so location updates cannot overwrite an
            // availability, approval or onboarding change made by another API instance.
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    return false;
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

async Task<bool> TryCreateRideAsync(DemoRide ride, CancellationToken ct)
{
    var auditEvent = (ride.AuditEvents ?? []).LastOrDefault();
    using var activity = auditEvent is null ? null : TurnOvaDiagnostics.StartRideAuditWrite(auditEvent.EventType);
    try
    {
        bool created;
        if (rideContainer is null)
        {
            created = rides.TryAdd(ride.RideId, ride);
        }
        else
        {
            try
            {
                await rideContainer.CreateItemAsync(ride, new PartitionKey(ride.RideId), cancellationToken: ct);
                created = true;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                created = false;
            }
        }

        if (auditEvent is not null)
        {
            TurnOvaDiagnostics.RecordRideAuditWrite(auditEvent.EventType, created ? "success" : "conflict");
            activity?.SetTag("turnova.audit.outcome", created ? "success" : "conflict");
            activity?.SetStatus(created ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        return created;
    }
    catch
    {
        if (auditEvent is not null)
        {
            TurnOvaDiagnostics.RecordRideAuditWrite(auditEvent.EventType, "failure");
            activity?.SetTag("turnova.audit.outcome", "failure");
            activity?.SetStatus(ActivityStatusCode.Error);
        }
        throw;
    }
}
async Task<DemoRide?> ReadRideRawAsync(string rideId, CancellationToken ct)
{
    if (rideContainer is null) return rides.TryGetValue(rideId, out var item) ? item : null;
    try { return (await rideContainer.ReadItemAsync<DemoRide>(rideId, new PartitionKey(rideId), cancellationToken: ct)).Resource; }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
}

async Task<DemoRide?> ReleaseTerminalSelectedRideReservationAsync(
    DemoRide? ride, string rideId, CancellationToken ct)
{
    // Only Union-linked selected Cab rides use the exclusive reservation lease.
    // Match both DriverId and RideId so a stale history item cannot release a newer ride.
    if (ride is null || string.IsNullOrWhiteSpace(ride.UnionId) ||
        !string.Equals(ride.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(ride.DriverId) ||
        ride.Status is not ("Completed" or "Cancelled" or "Expired"))
        return ride;

    await driverRideReservations.TryReleaseAsync(ride.DriverId, rideId, ct);
    if (!string.IsNullOrWhiteSpace(ride.RiderId))
        await riderActiveBookingReservations.TryReleaseAsync(
            ride.RiderId, rideId, ct,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.InternalReconciliation));
    return ride;
}

async Task<DemoRide?> FindRideAsync(string rideId, CancellationToken ct)
{
    var ride = await ReadRideRawAsync(rideId, ct);
    if (ride is null) return null;

    if (ride.Status is "Completed" or "Cancelled" or "Expired")
        return await ReleaseTerminalSelectedRideReservationAsync(ride, rideId, ct);

    if (ride.Status is not ("Negotiating" or "AgreementConfirmed") ||
        ride.NegotiationExpiresAtUtc is null || ride.NegotiationExpiresAtUtc > DateTimeOffset.UtcNow)
        return ride;

    var expired = await TryTransitionAsync(ride, "Expired", "system", "System", ct);
    if (expired is null)
    {
        // Another API instance may have won the terminal ETag transition and stopped
        // before its reservation-release side effect. Re-read and repair only that lease.
        var latest = await ReadRideRawAsync(rideId, ct);
        return await ReleaseTerminalSelectedRideReservationAsync(latest, rideId, ct);
    }

    if (!string.IsNullOrWhiteSpace(expired.DriverId))
        await driverRideReservations.TryReleaseAsync(expired.DriverId, rideId, ct);
    await app.Services.GetRequiredService<IHubContext<RideTrackingHub>>().Clients.Group(rideId)
        .SendAsync("RideStatusChanged", new { rideId, status = expired.Status }, ct);
    return expired;
}

async Task<bool> HasActiveDriverRideAsync(string driverId, CancellationToken ct, string? exceptRideId = null)
{
    // Read expired leases without deleting them. The ride aggregate is the authority for
    // expiry; only after its terminal transition (or an expired orphan lease) may the
    // matching reservation be released and another booking acquire the driver.
    var reservation = await driverRideReservations.GetActiveAsync(driverId, ct);
    if (reservation is null) return false;

    var isSameIdempotentRide = reservation.RideId == exceptRideId;
    var reservedRide = await FindRideAsync(reservation.RideId, ct);
    if (reservedRide is not null && reservedRide.Status is "Completed" or "Cancelled" or "Expired")
    {
        await driverRideReservations.TryReleaseAsync(driverId, reservation.RideId, ct);
        return false;
    }

    if (reservedRide is null && reservation.ExpiresAtUtc is { } orphanExpiry &&
        orphanExpiry <= DateTimeOffset.UtcNow)
    {
        await driverRideReservations.TryReleaseAsync(driverId, reservation.RideId, ct);
        return false;
    }

    // An idempotent retry may continue using its own live lease if the ride write was
    // interrupted. Other requests remain blocked. A live/expired lease with a ride that
    // is still active remains exclusive until the authoritative lifecycle resolves it.
    if (isSameIdempotentRide && (reservedRide is null || reservedRide.Status is "Negotiating" or "AgreementConfirmed"))
        return false;

    // An expired lease with a still-active ride is inconsistent and must fail closed.
    // FindRideAsync has already attempted the authoritative Negotiating/AgreementConfirmed expiry transition.
    return true;
}

async Task<bool> HasActiveRiderBookingAsync(
    string riderId, string? exceptRideId, string? requestHash, CancellationToken ct)
{
    var reservation = await riderActiveBookingReservations.GetActiveAsync(riderId, ct);
    if (reservation is null)
        return false;

    var isSameIdempotentBooking =
        reservation.RideId == exceptRideId &&
        reservation.RequestHash == requestHash;
    var reservedRide = await FindRideAsync(reservation.RideId, ct);

    if (reservedRide is not null &&
        reservedRide.Status is "Completed" or "Cancelled" or "Expired")
    {
        await riderActiveBookingReservations.TryReleaseAsync(
            riderId, reservation.RideId, ct,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.InternalReconciliation));
        return false;
    }

    if (reservedRide is null &&
        reservation.ExpiresAtUtc is { } orphanExpiry &&
        orphanExpiry <= DateTimeOffset.UtcNow)
    {
        // A missing ride plus an expired temporary reservation is an interrupted
        // pre-persistence booking. A permanent orphan is never automatically stolen.
        await riderActiveBookingReservations.TryReleaseAsync(
            riderId, reservation.RideId, ct,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.InternalReconciliation));
        return false;
    }

    if (reservedRide is not null &&
        reservedRide.Status is "Accepted" or "Arrived" or "InProgress" &&
        reservation.ExpiresAtUtc is not null)
    {
        // Repair a temporary lease if the ride transition committed but the promotion
        // response was lost. A permanent lease is safer than permitting another booking.
        await riderActiveBookingReservations.TryMakePermanentAsync(
            riderId, reservation.RideId, ct,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.AgreementConfirmed));
    }

    if (isSameIdempotentBooking &&
        (reservedRide is null || reservedRide.Status is "Negotiating" or "AgreementConfirmed"))
        return false;

    // Active rides remain exclusive. Expired Negotiating/AgreementConfirmed rides are
    // transitioned by FindRideAsync before this point. A permanent orphan fails closed.
    return true;
}

string? ResolveDispatchZone(double latitude, double longitude)
{
    var zones = app.Configuration.GetSection("Dispatch:Zones").Get<DispatchZoneConfig[]>() ?? [];
    var matches = zones
        .Where(zone =>
            latitude >= zone.MinLatitude && latitude <= zone.MaxLatitude &&
            longitude >= zone.MinLongitude && longitude <= zone.MaxLongitude)
        .Take(2)
        .ToArray();

    // Never let configuration order decide a pickup zone. Overlapping rectangles
    // (including inclusive shared boundaries) are ambiguous and must fail closed.
    return matches.Length == 1 && !string.IsNullOrWhiteSpace(matches[0].ZoneId)
        ? matches[0].ZoneId
        : null;
}

double CalculateDistanceKm(double firstLatitude, double firstLongitude, double secondLatitude, double secondLongitude)
{
    const double earthRadiusKm = 6371.0088;
    var latitudeDelta = (secondLatitude - firstLatitude) * Math.PI / 180;
    var longitudeDelta = (secondLongitude - firstLongitude) * Math.PI / 180;
    var firstLatitudeRadians = firstLatitude * Math.PI / 180;
    var secondLatitudeRadians = secondLatitude * Math.PI / 180;
    var haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
        Math.Cos(firstLatitudeRadians) * Math.Cos(secondLatitudeRadians) *
        Math.Pow(Math.Sin(longitudeDelta / 2), 2);
    return earthRadiusKm * 2 * Math.Asin(Math.Min(1, Math.Sqrt(haversine)));
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

app.MapGet("/health/ready", async (StorageReadinessProbe storageReadiness, CancellationToken ct) =>
{
    try
    {
        await storageReadiness.CheckAsync(ct);
        if (!useCosmos)
            return Results.Ok(new { service = "TurnOva.API", status = "ready", storage = storageMode });

        return Results.Ok(new { service = "TurnOva.API", status = "ready", storage = storageMode, database = databaseName });
    }
    catch (StorageReadinessException ex)
    {
        // Keep diagnostics useful without exposing Cosmos endpoints or SDK diagnostics.
        app.Logger.LogWarning(
            "Storage readiness check failed. FailureKind {FailureKind}",
            ex.FailureKind);
        return Results.Problem(
            "The configured storage dependency is not reachable.",
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
        email = request.Email.Trim().ToLowerInvariant(),
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
        email = request.Email.Trim().ToLowerInvariant(),
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
    if (!string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "Legacy non-Cab profiles cannot update a selfie for new MVP eligibility." });
    var capturedAtUtc = DateTimeOffset.UtcNow;
    var saved = await TryUpdateDriverProfileAsync(driverId, current =>
    {
        if (!string.Equals(current.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
            return null;

        return current with
        {
            ProfilePhotoBase64 = request.ProfilePhotoBase64,
            ProfilePhotoContentType = "image/jpeg",
            ProfilePhotoCapturedAtUtc = capturedAtUtc,
            ProfilePhotoApprovalStatus = "Pending",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = null,
            ProfilePhotoReviewReason = null
        };
    }, ct);
    if (!saved)
        return Results.Conflict(new { message = "Driver profile changed during selfie update. Refresh the profile and retry." });

    var updated = await FindDriverProfileAsync(driverId, ct);
    return updated is null
        ? Results.NotFound(new { message = "Driver profile no longer exists." })
        : Results.Ok(updated);
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
        !string.Equals(request.ServiceClass?.Trim(), "cab", StringComparison.OrdinalIgnoreCase) ||
        request.Documents is null || request.Documents.Count is < 1 or > 8 ||
        request.Documents.Any(document => document is null ||
            document.DocumentType is not ("DrivingLicence" or "VehicleRegistration" or "Insurance" or "Permit" or "ProfilePhoto") ||
            string.IsNullOrWhiteSpace(document.StorageReference) || document.StorageReference.Length > 200 ||
            document.StorageReference.Contains("..", StringComparison.Ordinal) ||
            Uri.TryCreate(document.StorageReference, UriKind.Absolute, out _)))
        return Results.BadRequest(new { message = "Provide valid driver, vehicle and document metadata. Upload files to private object storage and submit opaque storage references only." });

    var submittedAtUtc = DateTimeOffset.UtcNow;
    var profilePhotoCapturedAtUtc = submittedAtUtc;
    var documents = request.Documents
        .Select(document => new DriverDocumentReference(document.DocumentType, document.StorageReference))
        .ToArray();
    var profile = new DriverOnboardingProfile(
        driverId, driverId, request.ZoneId.Trim(), request.FullName.Trim(), request.PhoneNumber.Trim(),
        request.VehicleMake.Trim(), request.VehicleModel.Trim(), request.VehicleRegistration.Trim().ToUpperInvariant(),
        "cab", request.PassengerCapacity, documents, "Pending", submittedAtUtc,
        ProfilePhotoBase64: request.ProfilePhotoBase64,
        ProfilePhotoContentType: "image/jpeg",
        ProfilePhotoCapturedAtUtc: profilePhotoCapturedAtUtc,
        ProfilePhotoApprovalStatus: "Pending");

    // Create-if-absent handles the first submission. If another request creates the
    // profile first, retry as a conditional update against that authoritative snapshot.
    for (var attempt = 0; attempt < 3; attempt++)
    {
        var existing = await FindDriverProfileAsync(driverId, ct);
        if (existing is null)
        {
            if (await TryCreateDriverProfileAsync(profile, ct))
                return Results.Accepted("/api/v1/driver/onboarding/me", profile);
            continue;
        }

        if (!string.Equals(existing.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { message = "This legacy non-Cab profile is retained for historical records and cannot be converted through onboarding. Cab vehicle verification must be completed through the union workflow." });
        if (existing.Status is "Approved" or "Suspended")
            return Results.Conflict(new { message = "An approved or suspended driver profile cannot be replaced through onboarding." });

        var saved = await TryUpdateDriverProfileAsync(driverId, current =>
        {
            if (!string.Equals(current.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) ||
                current.Status is "Approved" or "Suspended")
                return null;

            // Replace submitted identity/vehicle/document data while preserving the latest
            // unrelated availability and location fields from the ETag/CAS snapshot.
            return current with
            {
                ZoneId = profile.ZoneId,
                FullName = profile.FullName,
                PhoneNumber = profile.PhoneNumber,
                VehicleMake = profile.VehicleMake,
                VehicleModel = profile.VehicleModel,
                VehicleRegistration = profile.VehicleRegistration,
                ServiceClass = profile.ServiceClass,
                PassengerCapacity = profile.PassengerCapacity,
                Documents = profile.Documents,
                Status = "Pending",
                SubmittedAtUtc = current.SubmittedAtUtc,
                ReviewedAtUtc = null,
                ReviewReason = null,
                ProfilePhotoBase64 = profile.ProfilePhotoBase64,
                ProfilePhotoContentType = profile.ProfilePhotoContentType,
                ProfilePhotoCapturedAtUtc = profile.ProfilePhotoCapturedAtUtc,
                ProfilePhotoApprovalStatus = "Pending",
                ProfilePhotoApprovedByUnionId = null,
                ProfilePhotoReviewedAtUtc = null,
                ProfilePhotoReviewReason = null
            };
        }, ct);

        if (saved)
        {
            var updated = await FindDriverProfileAsync(driverId, ct);
            return updated is null
                ? Results.Conflict(new { message = "Driver profile changed during onboarding. Refresh the profile and retry." })
                : Results.Accepted("/api/v1/driver/onboarding/me", updated);
        }

        var latest = await FindDriverProfileAsync(driverId, ct);
        if (latest is null)
            continue;
        if (!string.Equals(latest.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { message = "This legacy non-Cab profile is retained for historical records and cannot be converted through onboarding. Cab vehicle verification must be completed through the union workflow." });
        if (latest.Status is "Approved" or "Suspended")
            return Results.Conflict(new { message = "An approved or suspended driver profile cannot be replaced through onboarding." });
        return Results.Conflict(new { message = "Driver profile changed during onboarding. Refresh the profile and retry." });
    }

    return Results.Conflict(new { message = "Driver profile could not be created because another onboarding update is in progress. Refresh the profile and retry." });
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
    if (request.Status == "Approved" && !string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "Legacy non-Cab profiles cannot be approved for new MVP workflows." });
    if (profile.Documents is null)
        return Results.Conflict(new { message = "Driver document references are unavailable. Refresh the profile before review." });
    if (request.Status == "Approved" &&
        (!profile.Documents.Any(document => document.DocumentType == "DrivingLicence") ||
         !profile.Documents.Any(document => document.DocumentType == "VehicleRegistration")))
        return Results.Conflict(new { message = "A driving licence and vehicle registration reference are required before approval." });

    var reviewedAtUtc = DateTimeOffset.UtcNow;
    var saved = await TryUpdateDriverProfileAsync(driverId, current =>
    {
        if (!DriverProfileReviewGuard.Matches(current, profile))
            return null;
        if (request.Status == "Approved" &&
            (!string.Equals(current.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) ||
             current.Documents is null ||
             !current.Documents.Any(document => document.DocumentType == "DrivingLicence") ||
             !current.Documents.Any(document => document.DocumentType == "VehicleRegistration")))
            return null;

        return current with
        {
            Status = request.Status,
            Documents = current.Documents!.Select(document => document with { Status = request.Status }).ToArray(),
            ReviewedAtUtc = reviewedAtUtc,
            ReviewReason = request.Reason?.Trim()
        };
    }, ct);
    if (!saved)
        return Results.Conflict(new { message = "Driver onboarding or document references changed during review. Refresh the profile and retry." });

    var reviewed = await FindDriverProfileAsync(driverId, ct)
        ?? throw new InvalidOperationException("The reviewed driver profile could not be reloaded.");
    if (request.Status is "Rejected" or "Suspended")
        await queueService.LeaveAsync(driverId, reviewed.ZoneId, reviewed.ServiceClass, ct);
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
        request.ServiceClasses is null || request.ServiceClasses.Count != 1 ||
        !string.Equals(request.ServiceClasses[0]?.Trim(), "cab", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Provide a name, unique code, at least one service zone and the Cab service class only." });

    var code = request.Code.Trim().ToUpperInvariant();
    var existing = await ListUnionsAsync(ct);
    if (existing.Any(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { message = "A union with this code already exists." });

    var union = new DriverUnion(Guid.NewGuid().ToString("N"), request.Name.Trim(), code,
        request.Location?.Trim(), request.ManagerName?.Trim(), request.ManagerPhone?.Trim(),
        request.ZoneIds.Select(zone => zone.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        ["cab"],
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
    // Operations may reject/hide a selfie, but public approval is reserved for the current authenticated UnionManager.
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
    if (request.Status == "Approved" && !string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "Legacy non-Cab driver photos cannot be approved for new public discovery." });
    if (request.Status == "Approved")
        return Results.Forbid(); // Only an authenticated manager of the current union may approve public selfie visibility.

    var now = DateTimeOffset.UtcNow;
    var saved = await TryUpdateDriverProfileAsync(driverId, current =>
    {
        if (current.ProfilePhotoBase64 != profile.ProfilePhotoBase64 ||
            current.ProfilePhotoCapturedAtUtc != profile.ProfilePhotoCapturedAtUtc ||
            current.ProfilePhotoApprovalStatus != profile.ProfilePhotoApprovalStatus ||
            current.ProfilePhotoApprovedByUnionId != profile.ProfilePhotoApprovedByUnionId ||
            current.ProfilePhotoReviewReason != profile.ProfilePhotoReviewReason)
            return null;

        return current with
        {
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = now,
            ProfilePhotoReviewReason = request.Reason!.Trim()
        };
    }, ct);
    if (!saved)
        return Results.Conflict(new { message = "Driver selfie changed during operations review. Refresh the member record and retry." });

    var updated = await FindDriverProfileAsync(driverId, ct)
        ?? throw new InvalidOperationException("The rejected driver profile could not be reloaded.");
    await SaveUnionAuditAsync(new DriverUnionAuditEvent(Guid.NewGuid().ToString("N"), driverId,
        unionId, union.Name, unionId, union.Name, "SelfieRejectedByOperations",
        request.Reason.Trim(), context.User.Identity?.Name ?? "Operations", now), ct);
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


var unionManagerApi = app.MapGroup("/api/v1/union")
    .RequireAuthorization(policy => policy.RequireRole("UnionManager"));

unionManagerApi.MapGet("/drivers", async (ClaimsPrincipal user, CancellationToken ct) =>
{
    var unionIds = user.FindAll("unionId")
        .Select(claim => claim.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToHashSet(StringComparer.Ordinal);
    if (unionIds.Count == 0) return Results.Forbid();

    var drivers = new List<object>();
    foreach (var assignment in await ListActiveUnionAssignmentsAsync(ct))
    {
        if (!unionIds.Contains(assignment.UnionId)) continue;
        var union = await FindUnionAsync(assignment.UnionId, ct);
        if (union is null || union.Status != "Active") continue;
        var profile = await FindDriverProfileAsync(assignment.DriverId, ct);
        if (profile is null || !string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
            continue;

        drivers.Add(new
        {
            assignment.DriverId,
            assignment.UnionId,
            assignment.UnionName,
            assignment.Status,
            assignment.AssignedAtUtc,
            assignment.UpdatedAtUtc,
            driver = new
            {
                profile.DriverId,
                profile.FullName,
                profile.VehicleMake,
                profile.VehicleModel,
                profile.VehicleRegistration,
                profile.ServiceClass,
                profile.PassengerCapacity,
                profile.Status,
                documentStatuses = profile.Documents?.Select(document => new
                {
                    document.DocumentType,
                    document.Status
                }).ToArray() ?? [],
                profile.ProfilePhotoBase64,
                profile.ProfilePhotoContentType,
                profile.ProfilePhotoCapturedAtUtc,
                profile.ProfilePhotoApprovalStatus,
                profile.ProfilePhotoReviewedAtUtc,
                profile.ProfilePhotoReviewReason
            }
        });
    }

    return Results.Ok(drivers);
});


unionManagerApi.MapGet("/reconciliation", async (ClaimsPrincipal user, CancellationToken ct) =>
{
    var unionIds = user.FindAll("unionId")
        .Select(claim => claim.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToHashSet(StringComparer.Ordinal);
    if (unionIds.Count == 0) return Results.Forbid();

    // Keep Union oversight reads least-privilege at the storage boundary too.
    // Do not materialize full DemoRide documents containing Rider identity, route,
    // stops or coordinates when the reconciliation list needs only these fields.
    var reconciliationRecords = new List<UnionReconciliationQueryProjection>();
    if (rideContainer is null)
    {
        reconciliationRecords.AddRange(rides.Values
            .Where(ride => ride.PaymentReconciliation is { } report &&
                unionIds.Contains(ride.UnionId ?? "") &&
                string.Equals(ride.UnionId, report.UnionId, StringComparison.Ordinal))
            .Select(ride =>
            {
                var report = ride.PaymentReconciliation!;
                return new UnionReconciliationQueryProjection(
                    ride.ServiceClass,
                    ride.UnionId,
                    report.RideId,
                    report.AgreementVersion,
                    report.UnionId,
                    report.AgreedAmount,
                    report.AmountReceived,
                    report.Delta,
                    report.Currency,
                    report.ReportedAtUtc,
                    report.ReportedByDriverId,
                    report.Status);
            }));
    }
    else
    {
        foreach (var unionId in unionIds)
        {
            var query = new QueryDefinition(TurnOva.API.UnionReconciliationQuery.Sql)
                .WithParameter("@unionId", unionId);
            using var iterator = rideContainer.GetItemQueryIterator<UnionReconciliationQueryProjection>(query);
            while (iterator.HasMoreResults)
                reconciliationRecords.AddRange(await iterator.ReadNextAsync(ct));
        }
    }

    return Results.Ok(reconciliationRecords
        .Where(record => string.Equals(record.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(record.UnionId, record.ReportUnionId, StringComparison.Ordinal) &&
            unionIds.Contains(record.ReportUnionId))
        .OrderByDescending(record => record.ReportedAtUtc)
        .Select(record => new RidePaymentReconciliationResponse(
            record.RideId,
            record.AgreementVersion,
            record.ReportUnionId,
            record.AgreedAmount,
            record.AmountReceived,
            record.Delta,
            record.Currency,
            record.ReportedAtUtc,
            record.ReportedByDriverId,
            record.Status))
        .ToArray());
});

unionManagerApi.MapPut("/drivers/{driverId}/selfie-review", async (
    ClaimsPrincipal user, string driverId, DriverSelfieReviewRequest request, CancellationToken ct) =>
{
    if (request.Status is not ("Approved" or "Rejected"))
        return Results.BadRequest(new { message = "Selfie review status must be Approved or Rejected." });
    if (request.Status == "Rejected" &&
        (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 300))
        return Results.BadRequest(new { message = "A rejection reason of 1–300 characters is required." });

    // Authorize against the current assignment before acquiring any per-driver lease.
    // This prevents a manager from another Union from reserving an unrelated Driver ID.
    var assignment = await FindUnionAssignmentAsync(driverId, ct);
    if (assignment is null || assignment.Status != "Active" ||
        !IsUnionManagerFor(user, assignment.UnionId))
        return Results.NotFound(new { message = "Driver is not an active member of a union managed by this account." });

    // Reconcile the authoritative reservation before competing for the same per-Driver lease.
    // Expired orphan/terminal reservations may be released by the shared guard; active rides,
    // negotiations, transfers and another live review remain exclusive.
    if (await HasActiveDriverRideAsync(driverId, ct))
        return Results.Conflict(new { message = "The Driver is reserved by a selected booking, active ride or Union membership change. Retry after it is resolved." });

    // Serialize selfie review with membership transfer and selected-driver reservations.
    // The lease is technical only; no customer-facing queue or ride policy is introduced.
    var reviewLeaseId = $"UNION-SELFIE-REVIEW-{Guid.NewGuid():N}";
    if (!await driverRideReservations.TryAcquireTemporaryAsync(
        driverId, reviewLeaseId, DateTimeOffset.UtcNow.AddMinutes(5), ct,
        DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.DriverProfileReview)))
        return Results.Conflict(new { message = "The Driver is reserved by a selected booking, active ride or Union membership change. Retry after it is resolved." });

    try
    {
        // Re-read and re-authorize while holding the lease so a completed transfer cannot
        // leave a stale UnionManager able to approve the previous Union's selfie.
        assignment = await FindUnionAssignmentAsync(driverId, ct);
        if (assignment is null || assignment.Status != "Active" ||
            !IsUnionManagerFor(user, assignment.UnionId))
            return Results.NotFound(new { message = "Driver is not an active member of a union managed by this account." });

        var union = await FindUnionAsync(assignment.UnionId, ct);
        if (union is null || union.Status != "Active")
            return Results.NotFound(new { message = "Active union membership was not found." });

        var profile = await FindDriverProfileAsync(driverId, ct);
        if (profile is null || !IsValidDriverSelfie(profile.ProfilePhotoBase64))
            return Results.NotFound(new { message = "A valid driver selfie is not available for review." });
        if (profile.Status != "Approved" ||
            !string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) ||
            profile.Documents is null ||
            !profile.Documents.Any(document => document.DocumentType == "DrivingLicence" && document.Status == "Approved") ||
            !profile.Documents.Any(document => document.DocumentType == "VehicleRegistration" && document.Status == "Approved") ||
            !union.ServiceClasses.Contains("cab", StringComparer.OrdinalIgnoreCase) ||
            !union.ZoneIds.Contains(profile.ZoneId, StringComparer.OrdinalIgnoreCase))
            return Results.Conflict(new { message = "The active union, Cab driver profile and required licence/vehicle documents must be approved before selfie review." });

        var reason = request.Status == "Rejected" ? request.Reason!.Trim() : null;
        var sameReview =
            profile.ProfilePhotoApprovalStatus == request.Status &&
            profile.ProfilePhotoApprovedByUnionId == (request.Status == "Approved" ? assignment.UnionId : null) &&
            profile.ProfilePhotoReviewReason == reason;

        var reviewedAtUtc = profile.ProfilePhotoReviewedAtUtc;
        if (!sameReview || reviewedAtUtc is null)
        {
            reviewedAtUtc = DateTimeOffset.UtcNow;
            var saved = await TryUpdateDriverProfileAsync(driverId, current =>
            {
                // Do not approve a selfie/document/profile snapshot that changed while it was being reviewed.
                if (current.ProfilePhotoBase64 != profile.ProfilePhotoBase64 ||
                    current.ProfilePhotoCapturedAtUtc != profile.ProfilePhotoCapturedAtUtc ||
                    current.ProfilePhotoApprovalStatus != profile.ProfilePhotoApprovalStatus ||
                    current.ProfilePhotoApprovedByUnionId != profile.ProfilePhotoApprovedByUnionId ||
                    current.ProfilePhotoReviewReason != profile.ProfilePhotoReviewReason ||
                    current.Documents is null || profile.Documents is null ||
                    !current.Documents.SequenceEqual(profile.Documents) ||
                    current.Status != "Approved" ||
                    !string.Equals(current.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) ||
                    !current.Documents.Any(document => document.DocumentType == "DrivingLicence" && document.Status == "Approved") ||
                    !current.Documents.Any(document => document.DocumentType == "VehicleRegistration" && document.Status == "Approved"))
                    return null;

                return current with
                {
                    ProfilePhotoApprovalStatus = request.Status,
                    ProfilePhotoApprovedByUnionId = request.Status == "Approved" ? assignment.UnionId : null,
                    ProfilePhotoReviewedAtUtc = reviewedAtUtc,
                    ProfilePhotoReviewReason = reason
                };
            }, ct);
            if (!saved)
                return Results.Conflict(new { message = "Driver onboarding or selfie data changed during review. Refresh the member record and retry." });

            profile = await FindDriverProfileAsync(driverId, ct)
                ?? throw new InvalidOperationException("The reviewed driver profile could not be reloaded.");
        }

        await EnsureUnionSelfieAuditAsync(
            driverId, assignment.UnionId, union.Name, request.Status, reason,
            user.FindFirstValue("sub") ?? "UnionManager", reviewedAtUtc.Value, ct);

        return Results.Ok(new
        {
            driverId,
            unionId = assignment.UnionId,
            status = profile.ProfilePhotoApprovalStatus,
            reviewedAtUtc = profile.ProfilePhotoReviewedAtUtc,
            reason = profile.ProfilePhotoReviewReason
        });
    }
    finally
    {
        await driverRideReservations.TryReleaseAsync(driverId, reviewLeaseId, CancellationToken.None,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.DriverProfileReview));
    }
});

app.MapPost("/api/v1/ops/drivers/{driverId}/union-assignment", async (
    HttpContext context, string driverId, AssignDriverUnionRequest request, CancellationToken ct) =>
{
    if (!IsOpsAuthorized(context)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 300)
        return Results.BadRequest(new { message = "A transfer/assignment reason of 1–300 characters is required." });

    // Membership changes share the same per-driver exclusive lease as selected bookings.
    // This closes the check-then-transfer race: either a ride or a transfer owns the lease,
    // never both. The bounded technical lease is not a customer negotiation policy.
    if (await HasActiveDriverRideAsync(driverId, ct))
        return Results.Conflict(new { message = "A Driver with an active selected negotiation or ride cannot be transferred between Unions." });

    var transferLeaseId = $"UNION-TRANSFER-{Guid.NewGuid():N}";
    if (!await driverRideReservations.TryAcquireTemporaryAsync(
        driverId, transferLeaseId, DateTimeOffset.UtcNow.AddMinutes(5), ct,
        DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.UnionTransfer)))
        return Results.Conflict(new { message = "The Driver is already reserved by a booking, ride or Union membership change. Retry after it is resolved." });

    try
    {
        // Re-read all membership inputs while holding the exclusive per-driver lease.
        var profile = await FindDriverProfileAsync(driverId, ct);
        if (profile is null) return Results.NotFound(new { message = "Driver onboarding profile not found." });
        if (profile.Status != "Approved") return Results.Conflict(new { message = "Only approved drivers can be assigned to a union." });
        if (!string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { message = "Only Cab drivers can receive new MVP Union assignments. Legacy non-Cab membership records are retained for history." });
        if (profile.Documents is null ||
            !profile.Documents.Any(document => document.DocumentType == "DrivingLicence" && document.Status == "Approved") ||
            !profile.Documents.Any(document => document.DocumentType == "VehicleRegistration" && document.Status == "Approved"))
            return Results.Conflict(new { message = "Approved driving licence and Cab vehicle registration documents are required before Union assignment." });
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
        if (!string.Equals(profile.ProfilePhotoApprovedByUnionId, union.UnionId, StringComparison.Ordinal))
        {
            // A selfie approval is scoped to the current union. New/changed membership must be re-reviewed.
            var profileReset = await TryUpdateDriverProfileAsync(driverId, current =>
            {
                if (current.ProfilePhotoBase64 != profile.ProfilePhotoBase64 ||
                    current.ProfilePhotoCapturedAtUtc != profile.ProfilePhotoCapturedAtUtc ||
                    current.ProfilePhotoApprovalStatus != profile.ProfilePhotoApprovalStatus ||
                    current.ProfilePhotoApprovedByUnionId != profile.ProfilePhotoApprovedByUnionId ||
                    current.ProfilePhotoReviewReason != profile.ProfilePhotoReviewReason ||
                    current.Documents is null || profile.Documents is null ||
                    !current.Documents.SequenceEqual(profile.Documents) ||
                    current.Status != "Approved" ||
                    !string.Equals(current.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
                    return null;

                return current with
                {
                    ProfilePhotoApprovalStatus = "Pending",
                    ProfilePhotoApprovedByUnionId = null,
                    ProfilePhotoReviewedAtUtc = null,
                    ProfilePhotoReviewReason = null
                };
            }, ct);
            if (!profileReset)
                return Results.Conflict(new { message = "Driver onboarding or selfie data changed during Union transfer. Refresh and retry." });
        }

        var assignment = new DriverUnionAssignment(driverId, union.UnionId, union.Name, "Active",
            previous?.AssignedAtUtc ?? now, now, request.Reason.Trim());
        await SaveUnionAssignmentAsync(assignment, ct);
        await SaveUnionAuditAsync(new DriverUnionAuditEvent(Guid.NewGuid().ToString("N"), driverId,
            previous?.UnionId, previous?.UnionName, union.UnionId, union.Name,
            previous is null ? "Assigned" : "Transferred", request.Reason.Trim(),
            context.User.Identity?.Name ?? "Operations", now), ct);
        return Results.Ok(assignment);
    }
    finally
    {
        await driverRideReservations.TryReleaseAsync(driverId, transferLeaseId, CancellationToken.None,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.UnionTransfer));
    }
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
    var indiaTimeZoneOffset = TimeSpan.FromMinutes(330);
    var nowLocal = DateTimeOffset.UtcNow.ToOffset(indiaTimeZoneOffset);
    if (year < 2020 || year > nowLocal.Year || month is < 1 or > 12)
        return Results.BadRequest(new { message = "A valid payslip year and month are required." });
    var periodStartLocal = new DateTimeOffset(year, month, 1, 0, 0, 0, indiaTimeZoneOffset);
    var periodEndLocal = periodStartLocal.AddMonths(1);
    var currentMonthStartLocal = new DateTimeOffset(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, indiaTimeZoneOffset);
    if (periodStartLocal >= currentMonthStartLocal)
        return Results.BadRequest(new { message = "Payslips are available only for completed months." });
    var periodStart = periodStartLocal.ToUniversalTime();
    var periodEnd = periodEndLocal.ToUniversalTime();

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
        await ReconcileSelectedRideWorkActivitiesAsync(driverId, periodStart, periodEnd, ct);
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
        payslip = await SavePayslipIfAbsentAsync(payslip, ct);
    }
    byte[] pdf;
    try
    {
        pdf = DriverPayslipPdf.Generate(payslip);
    }
    catch (NotSupportedException)
    {
        return Results.UnprocessableEntity(new
        {
            message = "This payslip contains a script that is not supported by the current embedded font set. No PDF was generated."
        });
    }

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
    if (await GetApprovedDriverProfileAsync(driverId, ct) is null)
        return Results.Conflict(new { message = "Only an approved driver can change availability." });
    if (await HasActiveDriverRideAsync(driverId, ct))
        return Results.Conflict(new { message = "Resolve or complete the active selected booking/ride before changing availability." });

    // Serialize availability changes with selected bookings and Union transfers.
    // This is a short technical lease, not a Rider/Driver negotiation-lifetime policy.
    var availabilityLeaseId = $"DRIVER-AVAILABILITY-{Guid.NewGuid():N}";
    if (!await driverRideReservations.TryAcquireTemporaryAsync(
        driverId, availabilityLeaseId, DateTimeOffset.UtcNow.AddMinutes(5), ct,
        DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.AvailabilityChange)))
        return Results.Conflict(new { message = "The Driver is reserved by a selected negotiation or ride. Resolve it before changing availability." });

    try
    {
        // Re-read the authoritative profile after acquiring the same per-Driver lease used by booking.
        var profile = await GetApprovedDriverProfileAsync(driverId, ct);
        if (profile is null)
            return Results.Conflict(new { message = "Only an approved driver can change availability." });

        if (request.IsOnline)
        {
            if (!string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new { message = "Only approved Cab drivers can go online for new MVP ride requests. Legacy non-Cab profiles are retained for history." });
            if (request.Latitude is null || request.Longitude is null ||
                !RideLifecycle.AreCoordinatesValid(request.Latitude.Value, request.Longitude.Value))
                return Results.BadRequest(new { message = "Valid GPS coordinates are required before going online." });

            var assignment = await FindUnionAssignmentAsync(driverId, ct);
            if (assignment is null || assignment.Status != "Active")
                return Results.Conflict(new { message = "An active approved Cab Union membership is required before going online." });
            var union = await FindUnionAsync(assignment.UnionId, ct);
            if (union is null || union.Status != "Active" ||
                union.ServiceClasses is null ||
                !union.ServiceClasses.Contains("cab", StringComparer.OrdinalIgnoreCase) ||
                union.ZoneIds is null ||
                !union.ZoneIds.Contains(profile.ZoneId, StringComparer.OrdinalIgnoreCase))
                return Results.Conflict(new { message = "The assigned Union must be active, serve Cab and cover the Driver's approved service zone." });
            if (profile.Documents is null ||
                !profile.Documents.Any(document => document.DocumentType == "DrivingLicence" && document.Status == "Approved") ||
                !profile.Documents.Any(document => document.DocumentType == "VehicleRegistration" && document.Status == "Approved"))
                return Results.Conflict(new { message = "Approved driving licence and Cab vehicle documents are required before going online." });

            var currentZoneId = ResolveDispatchZone(request.Latitude.Value, request.Longitude.Value);
            if (!string.Equals(currentZoneId, profile.ZoneId, StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new { message = "Your current location is outside your assigned service zone." });

            var capturedAtUtc = request.CapturedAtUtc ?? DateTimeOffset.UtcNow;
            if (capturedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
                return Results.BadRequest(new { message = "GPS capture time cannot be in the future." });

            var savedOnline = await TryUpdateDriverProfileAsync(driverId, current =>
                current.Status == "Approved" &&
                string.Equals(current.ZoneId, profile.ZoneId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.ServiceClass, profile.ServiceClass, StringComparison.OrdinalIgnoreCase) &&
                (current.LocationCapturedAtUtc is null || capturedAtUtc > current.LocationCapturedAtUtc) &&
                current.Documents is not null &&
                current.Documents.Any(document => document.DocumentType == "DrivingLicence" && document.Status == "Approved") &&
                current.Documents.Any(document => document.DocumentType == "VehicleRegistration" && document.Status == "Approved")
                    ? current with
                    {
                        AvailabilityStatus = "Online",
                        CurrentLatitude = request.Latitude,
                        CurrentLongitude = request.Longitude,
                        LocationUpdatedAtUtc = DateTimeOffset.UtcNow,
                        LocationCapturedAtUtc = capturedAtUtc
                    }
                    : null, ct);
            if (!savedOnline)
                return Results.Conflict(new { message = "Driver profile, verification or availability changed. Refresh before going online." });

            try
            {
                var current = await queueService.GetPositionedQueueAsync(profile.ZoneId, profile.ServiceClass, ct);
                if (!current.Any(entry => entry.DriverId == driverId))
                    await queueService.JoinAsync(new DriverQueueEntry(driverId, profile.ZoneId, profile.ServiceClass, DateTimeOffset.UtcNow, 0, true), ct);
            }
            catch
            {
                try { await queueService.LeaveAsync(driverId, profile.ZoneId, profile.ServiceClass, ct); }
                catch { /* Profile state below still prevents this Driver from being discovered. */ }
                await TryUpdateDriverProfileAsync(driverId, current =>
                    current.Status == "Approved" && current.AvailabilityStatus == "Online"
                        ? current with
                        {
                            AvailabilityStatus = "Offline",
                            CurrentLatitude = null,
                            CurrentLongitude = null,
                            LocationUpdatedAtUtc = null,
                        }
                        : null, ct);
                throw;
            }

            var queue = await queueService.GetPositionedQueueAsync(profile.ZoneId, profile.ServiceClass, ct);
            var position = queue.ToList().FindIndex(entry => entry.DriverId == driverId) + 1;
            return Results.Ok(new { availabilityStatus = "Online", isOnline = true, isQueued = position > 0, position });
        }

        var savedOffline = await TryUpdateDriverProfileAsync(driverId, current =>
            current.Status == "Approved" &&
            string.Equals(current.ZoneId, profile.ZoneId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.ServiceClass, profile.ServiceClass, StringComparison.OrdinalIgnoreCase)
                ? current with
                {
                    AvailabilityStatus = "Offline",
                    CurrentLatitude = null,
                    CurrentLongitude = null,
                    LocationUpdatedAtUtc = null,
                }
                : null, ct);
        if (!savedOffline)
            return Results.Conflict(new { message = "Driver profile or availability changed. Refresh before going offline." });

        await queueService.LeaveAsync(driverId, profile.ZoneId, profile.ServiceClass, ct);
        return Results.Ok(new { availabilityStatus = "Offline", isOnline = false, isQueued = false, position = (int?)null });
    }
    finally
    {
        await driverRideReservations.TryReleaseAsync(driverId, availabilityLeaseId, CancellationToken.None,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.AvailabilityChange));
    }
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/driver/location", async (
    ClaimsPrincipal user, DriverAvailabilityLocationRequest request, QueueService queueService, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId))
        return Results.Forbid();
    if (request.Latitude is null || request.Longitude is null ||
        !RideLifecycle.AreCoordinatesValid(request.Latitude.Value, request.Longitude.Value))
        return Results.BadRequest(new { message = "Valid latitude and longitude are required." });

    var latitude = request.Latitude.Value;
    var longitude = request.Longitude.Value;
    var capturedAtUtc = request.CapturedAtUtc ?? DateTimeOffset.UtcNow;
    if (capturedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        return Results.BadRequest(new { message = "GPS capture time cannot be in the future." });

    var profile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (profile is null || profile.AvailabilityStatus != "Online")
        return Results.Conflict(new { message = "Only an approved online driver can share availability location." });
    if (!string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "Legacy non-Cab profiles cannot share location for new MVP workflows." });
    if (await HasActiveDriverRideAsync(driverId, ct))
        return Results.Conflict(new { message = "Availability location is paused while you have an active ride." });
    if (profile.LocationCapturedAtUtc is not null && capturedAtUtc <= profile.LocationCapturedAtUtc)
        return Results.Conflict(new { message = "This GPS fix is older than the latest accepted Driver location." });

    var currentZoneId = ResolveDispatchZone(latitude, longitude);
    if (!string.Equals(currentZoneId, profile.ZoneId, StringComparison.OrdinalIgnoreCase))
    {
        // A zone-exit transition must not race a Rider selecting this Driver.
        // The same per-Driver lease serializes it with bookings and Union transfers.
        var zoneExitLeaseId = $"DRIVER-ZONE-EXIT-{Guid.NewGuid():N}";
        if (!await driverRideReservations.TryAcquireTemporaryAsync(
            driverId, zoneExitLeaseId, DateTimeOffset.UtcNow.AddMinutes(5), ct,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.DriverZoneExit)))
            return Results.Conflict(new { message = "Location cannot take the Driver offline while a selected negotiation or ride reservation is active." });

        try
        {
            var latestProfile = await GetApprovedDriverProfileAsync(driverId, ct);
            if (latestProfile is null || latestProfile.AvailabilityStatus != "Online" ||
                !string.Equals(latestProfile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new { message = "Driver availability changed. Refresh before sharing location." });
            if (latestProfile.LocationCapturedAtUtc is not null && capturedAtUtc <= latestProfile.LocationCapturedAtUtc)
                return Results.Conflict(new { message = "This GPS fix is older than the latest accepted Driver location." });

            var latestZoneId = ResolveDispatchZone(latitude, longitude);
            if (string.Equals(latestZoneId, latestProfile.ZoneId, StringComparison.OrdinalIgnoreCase))
            {
                var savedInside = await TryUpdateDriverProfileAsync(driverId, current =>
                    current.Status == "Approved" && current.AvailabilityStatus == "Online" &&
                    string.Equals(current.ZoneId, latestProfile.ZoneId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(current.ServiceClass, latestProfile.ServiceClass, StringComparison.OrdinalIgnoreCase) &&
                    (current.LocationCapturedAtUtc is null || capturedAtUtc > current.LocationCapturedAtUtc)
                        ? current with
                        {
                            CurrentLatitude = latitude,
                            CurrentLongitude = longitude,
                            LocationUpdatedAtUtc = DateTimeOffset.UtcNow,
                            LocationCapturedAtUtc = capturedAtUtc
                        }
                        : null, ct);
                return savedInside
                    ? Results.NoContent()
                    : Results.Conflict(new { message = "Driver availability changed. Refresh before sharing location." });
            }

            var takenOffline = await TryUpdateDriverProfileAsync(driverId, current =>
                current.Status == "Approved" && current.AvailabilityStatus == "Online" &&
                string.Equals(current.ZoneId, latestProfile.ZoneId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.ServiceClass, latestProfile.ServiceClass, StringComparison.OrdinalIgnoreCase) &&
                (current.LocationCapturedAtUtc is null || capturedAtUtc > current.LocationCapturedAtUtc)
                    ? current with
                    {
                        AvailabilityStatus = "Offline",
                        CurrentLatitude = null,
                        CurrentLongitude = null,
                        LocationUpdatedAtUtc = null,
                        LocationCapturedAtUtc = capturedAtUtc
                    }
                    : null, ct);
            if (!takenOffline)
                return Results.Conflict(new { message = "Driver availability changed. Refresh before sharing location." });

            await queueService.LeaveAsync(driverId, latestProfile.ZoneId, latestProfile.ServiceClass, ct);
            return Results.Conflict(new { message = "You left your assigned service zone and were taken offline." });
        }
        finally
        {
            await driverRideReservations.TryReleaseAsync(driverId, zoneExitLeaseId, CancellationToken.None,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.DriverZoneExit));
        }
    }

    var saved = await TryUpdateDriverProfileAsync(driverId, current =>
        current.Status == "Approved" && current.AvailabilityStatus == "Online" &&
        string.Equals(current.ZoneId, currentZoneId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(current.ServiceClass, profile.ServiceClass, StringComparison.OrdinalIgnoreCase) &&
        (current.LocationCapturedAtUtc is null || capturedAtUtc > current.LocationCapturedAtUtc)
            ? current with
            {
                CurrentLatitude = latitude,
                CurrentLongitude = longitude,
                LocationUpdatedAtUtc = DateTimeOffset.UtcNow,
                LocationCapturedAtUtc = capturedAtUtc
            }
            : null, ct);
    return saved
        ? Results.NoContent()
        : Results.Conflict(new { message = "Driver availability changed. Refresh before sharing location." });
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
    new { id = "hyderabad-central", name = "Hyderabad Central", serviceClasses = new[] { "cab" } }
})).RequireAuthorization();

app.MapGet("/api/v1/zones/{zoneId}/queues/{serviceClass}", async (
    string zoneId, string serviceClass, QueueService service, CancellationToken ct) =>
{
    if (!string.Equals(serviceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Only the Cab queue is available in the MVP." });
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
    if (!string.Equals(serviceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Only the Cab queue is available in the MVP." });

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
    if (!string.Equals(approvedProfile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "Only Cab drivers can join new MVP availability workflows. Legacy non-Cab queue records are retained but are not eligible for new rides." });
    if (approvedProfile.AvailabilityStatus != "Online")
        return Results.Conflict(new { message = "Go online before joining the FIFO queue." });

    try
    {
        var serviceClass = string.IsNullOrWhiteSpace(request.ServiceClass) ? "cab" : request.ServiceClass.Trim().ToLowerInvariant();
        if (!string.Equals(serviceClass, "cab", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = "Only Cab queue membership is supported in the MVP." });
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

app.MapGet("/api/v1/rider/discovery", async (
    double latitude, double longitude, int passengers, CancellationToken ct) =>
{
    if (!RideLifecycle.AreCoordinatesValid(latitude, longitude) ||
        passengers is < 1 or > 6)
        return Results.BadRequest(new { message = "Provide valid pickup coordinates and a passenger count between 1 and 6." });

    var radiusKm = app.Configuration.GetValue<double?>("Dispatch:NearbyRadiusKm");
    if (radiusKm is null || !double.IsFinite(radiusKm.Value) || radiusKm.Value <= 0)
        return Results.Problem("The approved Rider discovery radius is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

    var zoneId = ResolveDispatchZone(latitude, longitude);
    if (string.IsNullOrWhiteSpace(zoneId))
        return Results.BadRequest(new { message = "TurnOva is not available in this pickup zone yet." });

    var maxAgeSeconds = app.Configuration.GetValue<int?>("Dispatch:NearbyLocationMaxAgeSeconds");
    if (maxAgeSeconds is null || maxAgeSeconds <= 0)
        return Results.Problem("The approved maximum Driver location age is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var freshAfter = DateTimeOffset.UtcNow.AddSeconds(-maxAgeSeconds.Value);
    var eligibleUnions = (await ListUnionsAsync(ct))
        .Where(union => union.Status == "Active" &&
            union.ZoneIds.Contains(zoneId, StringComparer.OrdinalIgnoreCase) &&
            union.ServiceClasses.Contains("cab", StringComparer.OrdinalIgnoreCase))
        .ToDictionary(union => union.UnionId, StringComparer.Ordinal);
    var assignmentsByDriver = (await ListActiveUnionAssignmentsAsync(ct))
        .Where(assignment => eligibleUnions.ContainsKey(assignment.UnionId))
        .GroupBy(assignment => assignment.DriverId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedAtUtc).First(), StringComparer.Ordinal);
    var candidates = new List<(DriverUnion Union, DriverOnboardingProfile Profile, double DistanceKm)>();

    foreach (var profile in await ListOnlineCabProfilesForDiscoveryAsync(zoneId, ct))
    {
        var locationCapturedAtUtc = profile.LocationCapturedAtUtc ?? profile.LocationUpdatedAtUtc;
        if (profile.Status != "Approved" || profile.AvailabilityStatus != "Online" ||
            profile.PassengerCapacity < passengers ||
            locationCapturedAtUtc is null || locationCapturedAtUtc.Value < freshAfter ||
            profile.CurrentLatitude is null || profile.CurrentLongitude is null ||
            !double.IsFinite(profile.CurrentLatitude.Value) || !double.IsFinite(profile.CurrentLongitude.Value) ||
            profile.CurrentLatitude is < -90 or > 90 || profile.CurrentLongitude is < -180 or > 180 ||
            profile.Documents is null ||
            !profile.Documents.Any(document => document.DocumentType == "DrivingLicence" && document.Status == "Approved") ||
            !profile.Documents.Any(document => document.DocumentType == "VehicleRegistration" && document.Status == "Approved") ||
            !assignmentsByDriver.TryGetValue(profile.DriverId, out var assignment) ||
            !eligibleUnions.TryGetValue(assignment.UnionId, out var union) ||
            !string.Equals(assignment.Status, "Active", StringComparison.OrdinalIgnoreCase) ||
            await HasActiveDriverRideAsync(profile.DriverId, ct))
            continue;

        var distanceKm = CalculateDistanceKm(
            latitude, longitude, profile.CurrentLatitude.Value, profile.CurrentLongitude.Value);
        if (distanceKm > radiusKm.Value)
            continue;
        candidates.Add((union, profile, distanceKm));
    }

    var groups = candidates
        .GroupBy(candidate => candidate.Union.UnionId, StringComparer.Ordinal)
        .Select(group =>
        {
            var union = group.First().Union;
            var drivers = group
                .OrderBy(candidate => candidate.DistanceKm)
                .ThenBy(candidate => candidate.Profile.DriverId, StringComparer.Ordinal)
                .Take(3)
                .Select(candidate => new NearbyDriverDiscoveryResponse(
                    candidate.Profile.DriverId,
                    candidate.Profile.FullName,
                    candidate.Profile.VehicleMake,
                    candidate.Profile.VehicleModel,
                    "cab",
                    candidate.Profile.PassengerCapacity,
                    Math.Round(candidate.DistanceKm, 1),
                    candidate.Profile.ProfilePhotoApprovalStatus == "Approved" &&
                        candidate.Profile.ProfilePhotoApprovedByUnionId == union.UnionId
                            ? candidate.Profile.ProfilePhotoBase64 : null,
                    candidate.Profile.ProfilePhotoApprovalStatus == "Approved" &&
                        candidate.Profile.ProfilePhotoApprovedByUnionId == union.UnionId
                            ? candidate.Profile.ProfilePhotoContentType : null))
                .ToArray();
            return new NearbyUnionDiscoveryGroup(union.UnionId, union.Name, drivers);
        })
        .OrderBy(group => group.UnionName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(group => group.UnionId, StringComparer.Ordinal)
        .ToArray();

    return Results.Ok(new RiderDiscoveryResponse(zoneId, null, groups));
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

app.MapPost("/api/v1/rider/bookings", async (
    HttpContext context, ClaimsPrincipal user, RiderSelectedBookingRequest request,
    IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    var riderId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(riderId)) return Results.Forbid();
    var selectedDriverId = request.SelectedDriverId?.Trim();
    var unionId = request.UnionId?.Trim();
    var pickup = request.Pickup?.Trim();
    var dropoff = request.Dropoff?.Trim();
    if (string.IsNullOrWhiteSpace(selectedDriverId) || selectedDriverId.Length > 128 ||
        string.IsNullOrWhiteSpace(unionId) || unionId.Length > 128 ||
        string.IsNullOrWhiteSpace(pickup) || pickup.Length > 250 ||
        string.IsNullOrWhiteSpace(dropoff) || dropoff.Length > 250 ||
        request.Passengers is < 1 or > 6 ||
        !RideLifecycle.AreCoordinatesValid(request.PickupLatitude, request.PickupLongitude) ||
        request.Stops?.Count > 10 ||
        request.Stops?.Any(stop => string.IsNullOrWhiteSpace(stop) || stop.Trim().Length > 250) == true)
        return Results.BadRequest(new { message = "Provide a selected Cab driver and union, pickup, drop-off, valid stops, coordinates and passenger count." });

    var lifetimeMinutes = app.Configuration.GetValue<int?>("Dispatch:SelectedDriverNegotiationLifetimeMinutes");
    if (lifetimeMinutes is null || lifetimeMinutes <= 0)
        return Results.Problem("Selected-driver negotiation lifetime is not configured. Booking is disabled until the approved expiry policy is supplied.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    var radiusKm = app.Configuration.GetValue<double?>("Dispatch:NearbyRadiusKm");
    if (radiusKm is null || !double.IsFinite(radiusKm.Value) || radiusKm.Value <= 0)
        return Results.Problem("The approved Rider discovery radius is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var zoneId = ResolveDispatchZone(request.PickupLatitude, request.PickupLongitude);
    if (string.IsNullOrWhiteSpace(zoneId))
        return Results.BadRequest(new { message = "TurnOva is not available in this pickup zone yet." });

    var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        return Results.BadRequest(new { message = "A valid Idempotency-Key header (1-128 characters) is required." });
    var stops = request.Stops?.Select(stop => stop.Trim()).ToArray() ?? [];
    var requestFingerprint = CreateSelectedBookingRequestHash(
        selectedDriverId, unionId, pickup, dropoff, stops, request.Passengers,
        request.PickupLatitude, request.PickupLongitude);
    var keyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
    var idempotencyIndex = $"{riderId}:{keyId}";
    var bookingIdempotency = app.Services.GetRequiredService<ConcurrentDictionary<string, RideIdempotencyRecord>>();
    var now = DateTimeOffset.UtcNow;
    var bookingCreatedAtUtc = now;
    var rideId = $"RIDE-SEL-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{riderId}:{keyId}")))}";
    var isIdempotencyRecovery = false;

    async Task<IResult?> ResolveExistingBookingIdempotencyAsync(RideIdempotencyRecord prior)
    {
        if (prior.RequestHash != requestFingerprint)
            return Results.Conflict(new { message = "This Idempotency-Key was already used for a different booking request." });

        var priorRide = await FindRideAsync(prior.rideId, ct);
        if (priorRide is not null)
        {
            if (!string.IsNullOrWhiteSpace(priorRide.UnionId) &&
                string.Equals(priorRide.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) &&
                priorRide.Status is "Negotiating" or "AgreementConfirmed" or "Accepted" or "Arrived" or "InProgress")
            {
                if (await HasActiveRiderBookingAsync(riderId, priorRide.RideId, requestFingerprint, ct))
                    return Results.Problem(
                        "The existing selected booking could not safely restore its Rider reservation.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);

                var currentRiderReservation = await riderActiveBookingReservations.GetActiveAsync(riderId, ct);
                if (currentRiderReservation is null)
                {
                    var expiry = priorRide.Status is "Negotiating" or "AgreementConfirmed"
                        ? priorRide.NegotiationExpiresAtUtc
                        : null;
                    var recoveredReservation = new RiderActiveBookingReservation(
                        riderId, priorRide.RideId, requestFingerprint, priorRide.RequestedAt, expiry);
                    var recoveryResult = await riderActiveBookingReservations.TryAcquireAsync(
                        recoveredReservation, ct,
                        new DriverRideReservationAuditContext(riderId, "Rider", DriverRideReservationAuditReasons.InternalReconciliation));
                    if (recoveryResult == RiderBookingReservationAcquireResult.Conflict)
                        return Results.Problem(
                            "The existing selected booking could not safely restore its Rider reservation.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                else if (currentRiderReservation.RideId != priorRide.RideId ||
                         currentRiderReservation.RequestHash != requestFingerprint)
                {
                    return Results.Problem(
                        "The existing selected booking conflicts with the Rider's active reservation.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (priorRide.Status is "Accepted" or "Arrived" or "InProgress" &&
                    !await riderActiveBookingReservations.TryMakePermanentAsync(
                        riderId, priorRide.RideId, ct,
                        DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.AgreementConfirmed)))
                    return Results.Problem(
                        "The existing selected booking's Rider reservation could not be made permanent.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // Re-send the selected-driver notification on an idempotent replay so a
            // transient SignalR failure after persistence does not strand the Driver.
            if (priorRide.DriverId is not null &&
                priorRide.Status is "Negotiating" or "AgreementConfirmed")
            {
                await hub.Clients.Group($"driver:{priorRide.DriverId}").SendAsync("RideSelected", new
                {
                    rideId = priorRide.RideId,
                    pickup = priorRide.Pickup,
                    dropoff = priorRide.Dropoff,
                    stops = priorRide.Stops ?? [],
                    passengers = priorRide.Passengers,
                    expiresAtUtc = priorRide.NegotiationExpiresAtUtc
                }, ct);
            }
            return Results.Ok(priorRide);
        }

        // A deterministic ride ID lets a retry recover a process interrupted after the
        // idempotency record or reservation was written but before the ride document was saved.
        if (prior.CreatedAtUtc is null || prior.CreatedAtUtc.Value > now.AddSeconds(-60))
            return Results.Conflict(new { message = "The original booking is still being processed. Retry shortly." });

        rideId = prior.rideId;
        bookingCreatedAtUtc = prior.CreatedAtUtc.Value;
        isIdempotencyRecovery = true;
        return null;
    }

    if (idempotencyContainer is null)
    {
        if (bookingIdempotency.TryGetValue(idempotencyIndex, out var existingRecord))
        {
            var existingResult = await ResolveExistingBookingIdempotencyAsync(existingRecord);
            if (existingResult is not null) return existingResult;
        }
    }
    else
    {
        try
        {
            var existingRecord = await idempotencyContainer.ReadItemAsync<RideIdempotencyRecord>(
                keyId, new PartitionKey(riderId), cancellationToken: ct);
            var existingResult = await ResolveExistingBookingIdempotencyAsync(existingRecord.Resource);
            if (existingResult is not null) return existingResult;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Create the idempotency record only after current Union/Driver eligibility is validated.
        }
    }

    var union = await FindUnionAsync(unionId, ct);
    if (union is null || union.Status != "Active" ||
        !union.ZoneIds.Contains(zoneId, StringComparer.OrdinalIgnoreCase) ||
        !union.ServiceClasses.Contains("cab", StringComparer.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "The selected Union is not active for Cab service in this pickup zone." });
    var assignment = await FindUnionAssignmentAsync(selectedDriverId, ct);
    if (assignment is null || assignment.Status != "Active" || assignment.UnionId != unionId)
        return Results.Conflict(new { message = "The selected Driver is not an active member of this Union." });
    var profile = await GetApprovedDriverProfileAsync(selectedDriverId, ct);
    var maxAgeSeconds = app.Configuration.GetValue<int?>("Dispatch:NearbyLocationMaxAgeSeconds");
    if (maxAgeSeconds is null || maxAgeSeconds <= 0)
        return Results.Problem("The approved maximum Driver location age is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var freshAfter = DateTimeOffset.UtcNow.AddSeconds(-maxAgeSeconds.Value);
    var locationCapturedAtUtc = profile?.LocationCapturedAtUtc ?? profile?.LocationUpdatedAtUtc;
    if (profile is null || profile.AvailabilityStatus != "Online" ||
        !string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(profile.ZoneId, zoneId, StringComparison.OrdinalIgnoreCase) ||
        profile.PassengerCapacity < request.Passengers ||
        locationCapturedAtUtc is null || locationCapturedAtUtc.Value < freshAfter ||
        profile.CurrentLatitude is null or < -90 or > 90 || !double.IsFinite(profile.CurrentLatitude.Value) ||
        profile.CurrentLongitude is null or < -180 or > 180 || !double.IsFinite(profile.CurrentLongitude.Value) ||
        profile.Documents is null ||
        !profile.Documents.Any(document => document.DocumentType == "DrivingLicence" && document.Status == "Approved") ||
        !profile.Documents.Any(document => document.DocumentType == "VehicleRegistration" && document.Status == "Approved"))
        return Results.Conflict(new { message = "The selected Driver is no longer eligible or available for this Cab booking." });
    var distanceKm = CalculateDistanceKm(request.PickupLatitude, request.PickupLongitude,
        profile.CurrentLatitude.Value, profile.CurrentLongitude.Value);
    if (distanceKm > radiusKm.Value)
        return Results.Conflict(new { message = "The selected Driver is no longer within the approved pickup radius." });

    // Recovery must retain the original negotiation deadline. The reservation lease was
    // acquired for this timestamp; extending only the ride deadline would leave an active
    // negotiation with an expired lease that the Driver can no longer promote.
    var expiresAtUtc = bookingCreatedAtUtc.AddMinutes(lifetimeMinutes.Value);
    var idempotencyRecord = new RideIdempotencyRecord(keyId, riderId, rideId, requestFingerprint, now);
    if (!isIdempotencyRecovery)
    {
        if (idempotencyContainer is null)
        {
            if (!bookingIdempotency.TryAdd(idempotencyIndex, idempotencyRecord))
            {
                if (!bookingIdempotency.TryGetValue(idempotencyIndex, out var prior))
                    return Results.Conflict(new { message = "A booking with this Idempotency-Key is already being processed. Retry shortly." });
                var existingResult = await ResolveExistingBookingIdempotencyAsync(prior);
                if (existingResult is not null) return existingResult;
            }
        }
        else
        {
            try
            {
                await idempotencyContainer.CreateItemAsync(idempotencyRecord, new PartitionKey(riderId), cancellationToken: ct);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var prior = await idempotencyContainer.ReadItemAsync<RideIdempotencyRecord>(
                    keyId, new PartitionKey(riderId), cancellationToken: ct);
                var existingResult = await ResolveExistingBookingIdempotencyAsync(prior.Resource);
                if (existingResult is not null) return existingResult;
            }
        }
    }

    async Task RemoveIdempotencyRecordAsync()
    {
        if (isIdempotencyRecovery) return;
        if (idempotencyContainer is null) bookingIdempotency.TryRemove(idempotencyIndex, out _);
        else
        {
            try { await idempotencyContainer.DeleteItemAsync<RideIdempotencyRecord>(keyId, new PartitionKey(riderId), cancellationToken: ct); }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        }
    }

    if (await HasActiveRiderBookingAsync(riderId, rideId, requestFingerprint, ct))
    {
        await RemoveIdempotencyRecordAsync();
        return Results.Conflict(new { message = "You already have an active selected-driver negotiation or ride. Complete or cancel it before booking another Driver." });
    }

    var riderReservation = new RiderActiveBookingReservation(
        riderId, rideId, requestFingerprint, bookingCreatedAtUtc, expiresAtUtc);
    var riderReservationAcquire = await riderActiveBookingReservations.TryAcquireAsync(
        riderReservation, ct,
        new DriverRideReservationAuditContext(riderId, "Rider", DriverRideReservationAuditReasons.SelectedCabBooking));
    if (riderReservationAcquire == RiderBookingReservationAcquireResult.Conflict)
    {
        // Close the read/create race: another request may have acquired the Rider partition
        // after the first lifecycle read. Reconcile a terminal/expired orphan once, then retry.
        if (!await HasActiveRiderBookingAsync(riderId, rideId, requestFingerprint, ct))
            riderReservationAcquire = await riderActiveBookingReservations.TryAcquireAsync(
                riderReservation, ct,
                new DriverRideReservationAuditContext(riderId, "Rider", DriverRideReservationAuditReasons.SelectedCabBooking));
    }
    if (riderReservationAcquire == RiderBookingReservationAcquireResult.Conflict)
    {
        await RemoveIdempotencyRecordAsync();
        return Results.Conflict(new { message = "You already have an active selected-driver negotiation or ride. Complete or cancel it before booking another Driver." });
    }

    async Task ReleaseRiderReservationIfNewAsync()
    {
        if (riderReservationAcquire == RiderBookingReservationAcquireResult.Acquired)
            await riderActiveBookingReservations.TryReleaseAsync(
                riderId, rideId, ct,
                new DriverRideReservationAuditContext(riderId, "Rider", DriverRideReservationAuditReasons.BookingEligibilityChanged));
    }

    if (await HasActiveDriverRideAsync(selectedDriverId, ct, rideId))
    {
        await ReleaseRiderReservationIfNewAsync();
        await RemoveIdempotencyRecordAsync();
        return Results.Conflict(new { message = "The selected Driver has another active negotiation or ride. Refresh discovery and select an available Driver." });
    }
    if (!await driverRideReservations.TryAcquireTemporaryAsync(selectedDriverId, rideId, expiresAtUtc, ct,
        new DriverRideReservationAuditContext(riderId, "Rider", DriverRideReservationAuditReasons.SelectedCabBooking)))
    {
        await ReleaseRiderReservationIfNewAsync();
        await RemoveIdempotencyRecordAsync();
        return Results.Conflict(new { message = "The selected Driver has another active negotiation or ride. Refresh discovery and select an available Driver." });
    }

    // Re-read eligibility after acquiring the per-driver lease. Availability changes are
    // blocked while a lease is held; this second read closes the selection/assignment race.
    var latestAssignment = await FindUnionAssignmentAsync(selectedDriverId, ct);
    var latestUnion = await FindUnionAsync(unionId, ct);
    var latestProfile = await GetApprovedDriverProfileAsync(selectedDriverId, ct);
    var latestFreshAfter = DateTimeOffset.UtcNow.AddSeconds(-maxAgeSeconds.Value);
    var latestLocationCapturedAtUtc = latestProfile?.LocationCapturedAtUtc ?? latestProfile?.LocationUpdatedAtUtc;
    if (latestAssignment is null || latestAssignment.Status != "Active" || latestAssignment.UnionId != unionId ||
        latestUnion is null || latestUnion.Status != "Active" ||
        !latestUnion.ZoneIds.Contains(zoneId, StringComparer.OrdinalIgnoreCase) ||
        !latestUnion.ServiceClasses.Contains("cab", StringComparer.OrdinalIgnoreCase) ||
        latestProfile is null || latestProfile.AvailabilityStatus != "Online" ||
        !string.Equals(latestProfile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(latestProfile.ZoneId, zoneId, StringComparison.OrdinalIgnoreCase) ||
        latestProfile.PassengerCapacity < request.Passengers ||
        latestLocationCapturedAtUtc is null || latestLocationCapturedAtUtc.Value < latestFreshAfter ||
        latestProfile.CurrentLatitude is null or < -90 or > 90 || !double.IsFinite(latestProfile.CurrentLatitude.Value) ||
        latestProfile.CurrentLongitude is null or < -180 or > 180 || !double.IsFinite(latestProfile.CurrentLongitude.Value) ||
        latestProfile.Documents is null ||
        !latestProfile.Documents.Any(document => document.DocumentType == "DrivingLicence" && document.Status == "Approved") ||
        !latestProfile.Documents.Any(document => document.DocumentType == "VehicleRegistration" && document.Status == "Approved"))
    {
        await driverRideReservations.TryReleaseAsync(selectedDriverId, rideId, ct,
            new DriverRideReservationAuditContext(riderId, "Rider", DriverRideReservationAuditReasons.BookingEligibilityChanged));
        await ReleaseRiderReservationIfNewAsync();
        await RemoveIdempotencyRecordAsync();
        return Results.Conflict(new { message = "The selected Driver or Union changed eligibility while the booking was being created. Refresh discovery." });
    }

    var latestDistanceKm = CalculateDistanceKm(request.PickupLatitude, request.PickupLongitude,
        latestProfile.CurrentLatitude.Value, latestProfile.CurrentLongitude.Value);
    if (latestDistanceKm > radiusKm.Value)
    {
        await driverRideReservations.TryReleaseAsync(selectedDriverId, rideId, ct,
            new DriverRideReservationAuditContext(riderId, "Rider", DriverRideReservationAuditReasons.BookingEligibilityChanged));
        await ReleaseRiderReservationIfNewAsync();
        await RemoveIdempotencyRecordAsync();
        return Results.Conflict(new { message = "The selected Driver is no longer within the approved pickup radius." });
    }

    var ride = new DemoRide(
        rideId, pickup, dropoff, request.Passengers, "Negotiating", bookingCreatedAtUtc, null, riderId, selectedDriverId,
        request.PickupLatitude, request.PickupLongitude, null, null, null, null, false, "cab", zoneId,
        UnionId: unionId, Stops: stops, NegotiationExpiresAtUtc: expiresAtUtc,
        DriverDisplayName: latestProfile.FullName, DriverVehicleMake: latestProfile.VehicleMake,
        DriverVehicleModel: latestProfile.VehicleModel, DriverDistanceToPickupKm: latestDistanceKm,
        UnionName: latestUnion.Name);
    ride = ride with
    {
        AuditEvents = AppendRideAuditEvent(ride, "RideCreated", riderId, "Rider",
            newStatus: ride.Status, occurredAtUtc: bookingCreatedAtUtc)
    };
    bool MatchesSelectedBooking(DemoRide existing) =>
        existing.RiderId == riderId &&
        existing.DriverId == selectedDriverId &&
        existing.UnionId == unionId &&
        existing.Pickup == pickup &&
        existing.Dropoff == dropoff &&
        existing.Passengers == request.Passengers &&
        (existing.Stops ?? []).SequenceEqual(stops, StringComparer.Ordinal) &&
        existing.PickupLatitude == request.PickupLatitude &&
        existing.PickupLongitude == request.PickupLongitude;

    var rideWasAlreadyPersisted = false;
    try
    {
        if (!await TryCreateRideAsync(ride, ct))
        {
            var existing = await ReadRideRawAsync(rideId, ct);
            if (existing is null)
                return Results.Problem("The booking write could not be confirmed. Retry with the same Idempotency-Key.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            if (!MatchesSelectedBooking(existing))
                return Results.Conflict(new { message = "The deterministic booking reference already belongs to a different ride payload." });
            ride = existing;
            rideWasAlreadyPersisted = true;
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Do not release the lease or idempotency record after an ambiguous write cancellation.
        // A retry with the same key will resolve the deterministic ride ID.
        throw;
    }
    catch
    {
        // Cosmos may commit a create before the client observes a transport failure. Read
        // back without the request token; never release a possibly committed ride's lease.
        DemoRide? persisted;
        try { persisted = await ReadRideRawAsync(rideId, CancellationToken.None); }
        catch
        {
            return Results.Problem("The booking persistence outcome could not be confirmed. Retry with the same Idempotency-Key.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (persisted is null || !MatchesSelectedBooking(persisted))
            return Results.Problem("The booking persistence outcome could not be confirmed. Retry with the same Idempotency-Key.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        ride = persisted;
        rideWasAlreadyPersisted = true;
    }

    if (ride.Status is "Completed" or "Cancelled" or "Expired")
    {
        await driverRideReservations.TryReleaseAsync(selectedDriverId, rideId, ct);
        await riderActiveBookingReservations.TryReleaseAsync(
            riderId, rideId, ct,
            DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.InternalReconciliation));
        return Results.Ok(ride);
    }

    if (ride.Status is "Negotiating" or "AgreementConfirmed")
    {
        await hub.Clients.Group($"driver:{selectedDriverId}").SendAsync("RideSelected", new
        {
            rideId, pickup, dropoff, stops, passengers = request.Passengers, expiresAtUtc = ride.NegotiationExpiresAtUtc
        }, ct);
        await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = ride.Status }, ct);
    }

    return rideWasAlreadyPersisted
        ? Results.Ok(ride)
        : Results.Created($"/api/v1/rides/{rideId}", ride);
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

app.MapGet("/api/v1/rides/nearby", () =>
    Results.Problem("Customer-facing FIFO discovery is retired. Use Rider union discovery.", statusCode: StatusCodes.Status410Gone))
    .RequireAuthorization(policy => policy.RequireRole("Rider"));


app.MapPost("/api/v1/rides/request", async (HttpContext context, ClaimsPrincipal user, CreateRideRequest request, QueueService queueService, IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    if (!app.Configuration.GetValue<bool>("Dispatch:LegacyFifoRiderRequestsEnabled"))
        return Results.Conflict(new { message = "Customer-facing FIFO ride requests are disabled. Select a driver through Rider discovery." });
    if (string.IsNullOrWhiteSpace(request.Pickup) || string.IsNullOrWhiteSpace(request.Dropoff))
        return Results.BadRequest(new { message = "Pickup and drop-off are required." });
    if (request.Passengers is < 1 or > 6)
        return Results.BadRequest(new { message = "Passenger count must be between 1 and 6." });
    if (request.PickupLatitude is null || request.PickupLongitude is null ||
        !RideLifecycle.AreCoordinatesValid(request.PickupLatitude.Value, request.PickupLongitude.Value))
        return Results.BadRequest(new { message = "Valid pickup coordinates are required." });
    var serviceClass = request.ServiceClass?.Trim().ToLowerInvariant() ?? string.Empty;
    if (!string.Equals(serviceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Only Cab rides are supported in the TurnOva MVP." });
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
            var existingRide = await FindRideAsync(existing.Resource.rideId, ct);
            return existingRide is null ? Results.Conflict(new { message = "The original request is still being processed. Retry shortly." }) : Results.Ok(existingRide);
        }
    }

    var requestedAtUtc = DateTimeOffset.UtcNow;
    var ride = new DemoRide(rideId, request.Pickup.Trim(), request.Dropoff.Trim(), request.Passengers,
        "Requested", requestedAtUtc, null, riderId, null, request.PickupLatitude, request.PickupLongitude,
        null, null, null, null, ServiceClass: serviceClass, DispatchZoneId: dispatchZoneId);
    ride = ride with
    {
        AuditEvents = AppendRideAuditEvent(ride, "RideRequested", riderId, "Rider",
            newStatus: ride.Status, occurredAtUtc: requestedAtUtc)
    };
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
            var lifetimeSeconds = app.Services.GetRequiredService<IOptions<DispatchOfferOptions>>().Value.LifetimeSeconds;
            var offerId = Guid.NewGuid().ToString("N");
            var expiresAt = now.AddSeconds(lifetimeSeconds);
            var attempt = new RideOfferAttempt(offerId, candidate.DriverId, now, expiresAt, RideOfferStatuses.Pending);
            var offer = new RideOfferState(ride.RideId, dispatchZoneId, serviceClass, offerId,
                candidate.DriverId, now, expiresAt, RideOfferStatuses.Pending, [attempt]);
            var store = app.Services.GetRequiredService<IRideOfferStore>();
            await store.TryCreateAsync(offer, ct);
        }
    }
    return Results.Created($"/api/v1/rides/{ride.RideId}", ride);
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

app.MapGet("/api/v1/driver/offers/pending", async (ClaimsPrincipal user, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var profile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (profile is null || !string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.NoContent();
    var store = app.Services.GetRequiredService<IRideOfferStore>();
    var offer = await store.GetPendingForDriverAsync(driverId, ct);
    if (offer is null || !string.Equals(offer.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.NoContent();
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
    if (approvedProfile is null || approvedProfile.AvailabilityStatus != "Online" ||
        !string.Equals(approvedProfile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();

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

app.MapPost("/api/v1/rides/{rideId}/accept", async (HttpContext context, ClaimsPrincipal user, string rideId, QueueService queueService, IHubContext<RideTrackingHub> hub, IRideOfferStore offerStore, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var approvedProfile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (approvedProfile is null || approvedProfile.AvailabilityStatus != "Online" ||
        !string.Equals(approvedProfile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
    var selectedRide = await FindRideAsync(rideId, ct);
    if (selectedRide is not null && selectedRide.DriverId == driverId &&
        selectedRide.UnionId is not null && selectedRide.Status == "Accepted")
        return Results.Ok(selectedRide);
    if (selectedRide is not null && selectedRide.DriverId == driverId &&
        (selectedRide.Status is "Negotiating" or "AgreementConfirmed"))
    {
        if (selectedRide.Status != "AgreementConfirmed")
            return Results.Conflict(new { message = "Both Rider and Driver must confirm the current agreement before accepting the selected booking." });
        var assignment = await FindUnionAssignmentAsync(driverId, ct);
        var union = assignment is null ? null : await FindUnionAsync(assignment.UnionId, ct);
        var currentAgreement = (selectedRide.AgreementVersions ?? []).LastOrDefault();
        var confirmations = selectedRide.AgreementConfirmations ?? [];
        if (assignment is null || assignment.Status != "Active" || assignment.UnionId != selectedRide.UnionId ||
            union?.Status != "Active" || !union.ServiceClasses.Contains("cab", StringComparer.OrdinalIgnoreCase) ||
            currentAgreement is null ||
            !confirmations.Any(item => item.Version == currentAgreement.Version && item.Party == "Rider" && item.ActorId == selectedRide.RiderId) ||
            !confirmations.Any(item => item.Version == currentAgreement.Version && item.Party == "Driver" && item.ActorId == driverId))
            return Results.Conflict(new { message = "The selected booking or current agreement is no longer eligible." });

        bool reservationMade;
        try
        {
            reservationMade = await driverRideReservations.TryMakePermanentAsync(driverId, rideId, ct,
                new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.AgreementConfirmed));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // A timeout can occur after Cosmos committed the permanent lease. Re-read before
            // failing so an ambiguous successful write is recovered idempotently.
            DriverRideReservation? currentReservation = null;
            try
            {
                currentReservation = await driverRideReservations.GetActiveAsync(
                    driverId, CancellationToken.None);
            }
            catch
            {
                // Preserve the original promotion exception; the safe audit below records only
                // its allow-listed category and server trace, never either exception message.
            }

            if (currentReservation?.RideId == rideId && currentReservation.ExpiresAtUtc is null)
            {
                await queueService.RecordDispatchExceptionAsync(
                    driverId,
                    string.IsNullOrWhiteSpace(selectedRide.DispatchZoneId)
                        ? approvedProfile.ZoneId
                        : selectedRide.DispatchZoneId,
                    string.IsNullOrWhiteSpace(selectedRide.ServiceClass)
                        ? approvedProfile.ServiceClass
                        : selectedRide.ServiceClass,
                    context.TraceIdentifier,
                    QueueAuditReasonCategories.DriverReservationPromotionRecovered,
                    CancellationToken.None);
                reservationMade = true;
            }
            else
            {
                await queueService.RecordDispatchExceptionAsync(
                    driverId,
                    string.IsNullOrWhiteSpace(selectedRide.DispatchZoneId)
                        ? approvedProfile.ZoneId
                        : selectedRide.DispatchZoneId,
                    string.IsNullOrWhiteSpace(selectedRide.ServiceClass)
                        ? approvedProfile.ServiceClass
                        : selectedRide.ServiceClass,
                    context.TraceIdentifier,
                    QueueAuditReasonCategories.DriverReservationFailed,
                    CancellationToken.None);
                throw;
            }
        }

        if (!reservationMade)
            return Results.Conflict(new { message = "The selected-driver reservation expired or is no longer held. Refresh Rider discovery." });

        var selectedBookingRequestHash = CreateSelectedBookingRequestHash(
            driverId, selectedRide.UnionId!, selectedRide.Pickup, selectedRide.Dropoff,
            selectedRide.Stops ?? [], selectedRide.Passengers,
            selectedRide.PickupLatitude!.Value, selectedRide.PickupLongitude!.Value);
        var riderReservationMade = false;
        try
        {
            riderReservationMade = await riderActiveBookingReservations.TryMakePermanentAsync(
                selectedRide.RiderId, rideId, ct,
                new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.AgreementConfirmed));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            var currentRiderReservation = await riderActiveBookingReservations.GetActiveAsync(
                selectedRide.RiderId, CancellationToken.None);
            riderReservationMade = currentRiderReservation?.RideId == rideId &&
                currentRiderReservation.ExpiresAtUtc is null;
            if (!riderReservationMade)
                throw;
        }

        if (!riderReservationMade)
        {
            // Repair a pre-deployment or interrupted selected booking that has no Rider
            // reservation yet. A different active ride in the same Rider partition wins.
            var currentRiderReservation = await riderActiveBookingReservations.GetActiveAsync(
                selectedRide.RiderId, ct);
            if (currentRiderReservation is null)
            {
                var recovered = new RiderActiveBookingReservation(
                    selectedRide.RiderId, rideId, selectedBookingRequestHash,
                    selectedRide.RequestedAt, ExpiresAtUtc: null);
                var acquired = await riderActiveBookingReservations.TryAcquireAsync(
                    recovered, ct,
                    new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.AgreementConfirmed));
                if (acquired is RiderBookingReservationAcquireResult.Acquired or RiderBookingReservationAcquireResult.AlreadyOwned)
                    riderReservationMade = await riderActiveBookingReservations.TryMakePermanentAsync(
                        selectedRide.RiderId, rideId, ct,
                        new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.AgreementConfirmed));
            }
        }

        if (!riderReservationMade)
            return Results.Conflict(new { message = "The Rider already has another active booking or its reservation could not be secured." });

        var acceptedSelectedRide = await TryTransitionAsync(selectedRide, "Accepted", driverId, "Driver", ct);
        if (acceptedSelectedRide is null)
        {
            if (selectedRide.NegotiationExpiresAtUtc is { } expiry)
                await driverRideReservations.TrySetExpiryAsync(driverId, rideId, expiry, ct,
                    DriverRideReservationAuditContext.ForSystem(DriverRideReservationAuditReasons.ReservationExpiryRestored));
            return Results.Conflict(new { message = "The selected booking changed concurrently. Refresh and retry." });
        }
        await RecordWorkActivityAsync(driverId, rideId, "Accepted", DateTimeOffset.UtcNow, ct);
        await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = acceptedSelectedRide.Status }, ct);
        return Results.Ok(acceptedSelectedRide);
    }
    var pendingOffer = await offerStore.GetByRideAsync(rideId, ct);
    if (pendingOffer is not null &&
        (!pendingOffer.IsPending || pendingOffer.DriverId != driverId ||
         pendingOffer.IsExpiredAt(TimeProvider.System.GetUtcNow())))
        return Results.Conflict(new { message = "This ride offer is no longer assigned to you or has expired." });
    IReadOnlyList<DriverQueueEntry> queue;
    try
    {
        queue = await queueService.GetPositionedQueueAsync(
            approvedProfile.ZoneId, approvedProfile.ServiceClass, ct);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception) when (!ct.IsCancellationRequested)
    {
        // The request trace and actor are server/claim-derived. Never persist the exception
        // message, request body, token, contact details or location in the audit event.
        // The exception record is separate from queue mutation audit and does not alter state.
        await queueService.RecordDispatchExceptionAsync(
            driverId,
            approvedProfile.ZoneId,
            approvedProfile.ServiceClass,
            context.TraceIdentifier,
            QueueAuditReasonCategories.QueueReadFailed,
            CancellationToken.None);
        throw;
    }

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
        if (!await driverRideReservations.TryAcquireAsync(driverId, rideId, ct,
                new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.LegacyRideAccept)))
            return Results.Conflict(new { message = "You already have an active ride. Complete or cancel it before accepting another ride." });
        var acceptedAtUtc = DateTimeOffset.UtcNow;
        var acceptedInMemory = current with
        {
            Status = "Accepted", DriverId = driverId, AcceptedAt = acceptedAtUtc, LocationSharingActive = true,
            AuditEvents = AppendRideAuditEvent(current, "RideStatusChanged", driverId, "Driver",
                current.Status, "Accepted", occurredAtUtc: acceptedAtUtc)
        };
        if (!rides.TryUpdate(rideId, acceptedInMemory, current))
        {
            await driverRideReservations.TryReleaseAsync(driverId, rideId, ct,
                new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.RideAcceptConflict));
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
        if (!await driverRideReservations.TryAcquireAsync(driverId, rideId, ct,
            new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.LegacyRideAccept)))
            return Results.Conflict(new { message = "You already have an active ride. Complete or cancel it before accepting another ride." });
        var acceptedAtUtc = DateTimeOffset.UtcNow;
        var accepted = response.Resource with
        {
            Status = "Accepted", DriverId = driverId, AcceptedAt = acceptedAtUtc, LocationSharingActive = true,
            AuditEvents = AppendRideAuditEvent(response.Resource, "RideStatusChanged", driverId, "Driver",
                response.Resource.Status, "Accepted", occurredAtUtc: acceptedAtUtc)
        };
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
        await driverRideReservations.TryReleaseAsync(driverId, rideId, ct,
            new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.RideAcceptConflict));
        return Results.Conflict(new { message = "This ride is no longer available." });
    }
}).RequireAuthorization(policy => policy.RequireRole("Driver"));


app.MapPost("/api/v1/rides/{rideId}/reject", async (ClaimsPrincipal user, string rideId, QueueService queueService, IRideOfferStore offerStore, IRideOfferAdvancer offerAdvancer, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId)) return Results.Forbid();
    var profile = await GetApprovedDriverProfileAsync(driverId, ct);
    if (profile is null || profile.AvailabilityStatus != "Online" ||
        !string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
    var ride = await FindRideAsync(rideId, ct);
    if (ride is not null && ride.DriverId == driverId && ride.UnionId is not null && ride.Status == "Cancelled")
        return Results.NoContent();
    if (ride is not null && ride.DriverId == driverId &&
        (ride.Status is "Negotiating" or "AgreementConfirmed"))
    {
        var cancelledSelection = await TryTransitionAsync(ride, "Cancelled", driverId, "Driver", ct);
        if (cancelledSelection is null)
            return Results.Conflict(new { message = "The selected booking changed concurrently. Refresh and retry." });
        await driverRideReservations.TryReleaseAsync(driverId, rideId, ct,
            new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.DriverDeclined));
        await RecordWorkActivityAsync(driverId, rideId, "Rejected", DateTimeOffset.UtcNow, ct);
        await app.Services.GetRequiredService<IHubContext<RideTrackingHub>>().Clients.Group(rideId)
            .SendAsync("RideStatusChanged", new { rideId, status = cancelledSelection.Status }, ct);
        return Results.NoContent();
    }

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
        {
            await offerAdvancer.AdvanceAsync(rideId, ct);
            return Results.NoContent();
        }
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
    if (profile is null || profile.AvailabilityStatus != "Online" ||
        !string.Equals(profile.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
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
    if (!RideLifecycle.AreCoordinatesValid(location.Latitude, location.Longitude))
        return Results.BadRequest(new { message = "Invalid coordinates." });
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ride = await FindRideAsync(rideId, ct);
    if (ride is null || ride.DriverId != driverId || !ride.LocationSharingActive || !RideLifecycle.IsLocationSharingActive(ride.Status))
        return Results.NotFound(new { message = "Active ride not found." });
    var capturedAtUtc = location.CapturedAtUtc ?? DateTimeOffset.UtcNow;
    if (capturedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        return Results.BadRequest(new { message = "GPS capture time cannot be in the future." });
    if (ride.DriverLocationCapturedAtUtc is not null &&
        capturedAtUtc <= ride.DriverLocationCapturedAtUtc.Value)
        return Results.Conflict(new { message = "This GPS fix is older than the latest accepted trip location." });

    var updated = ride with
    {
        DriverLatitude = location.Latitude,
        DriverLongitude = location.Longitude,
        LocationUpdatedAt = DateTimeOffset.UtcNow,
        DriverLocationCapturedAtUtc = capturedAtUtc
    };
    if (!await TryReplaceRideAsync(ride, updated, ct))
        return Results.Conflict(new { message = "Ride state or location changed. Refresh before sending another location update." });
    await hub.Clients.Group(rideId).SendAsync("DriverLocationUpdated", new
    {
        rideId,
        latitude = updated.DriverLatitude,
        longitude = updated.DriverLongitude,
        updated.LocationUpdatedAt,
        locationCapturedAtUtc = updated.DriverLocationCapturedAtUtc
    }, ct);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/{rideId}/arrive", (ClaimsPrincipal user, string rideId, CancellationToken ct) =>
    TransitionRideAsync(user, rideId, "Accepted", "Arrived", ct))
    .RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapGet("/api/v1/rides/{rideId}/agreements", async (ClaimsPrincipal user, string rideId, CancellationToken ct) =>
{
    var ride = await FindRideAsync(rideId, ct);
    var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (ride is null || string.IsNullOrWhiteSpace(actorId) ||
        !((user.IsInRole("Rider") && ride.RiderId == actorId) ||
          (user.IsInRole("Driver") && ride.DriverId == actorId)))
        return Results.NotFound(new { message = "Ride agreement not found." });

    var versions = ride.AgreementVersions ?? [];
    var confirmations = ride.AgreementConfirmations ?? [];
    return Results.Ok(versions.OrderBy(item => item.Version).Select(version =>
    {
        var riderConfirmation = confirmations.FirstOrDefault(item => item.Version == version.Version &&
            item.Party == "Rider" && item.ActorId == version.RiderId);
        var driverConfirmation = confirmations.FirstOrDefault(item => item.Version == version.Version &&
            item.Party == "Driver" && item.ActorId == version.DriverId);
        return new
        {
            version.RideId, version.Version, version.RiderId, version.DriverId, version.UnionId,
            version.Amount, version.Currency, version.Pickup, version.Dropoff, version.Stops,
            version.ProposedBy, version.CreatedAtUtc,
            RiderConfirmed = riderConfirmation is not null,
            RiderConfirmedAtUtc = riderConfirmation?.ConfirmedAtUtc,
            DriverConfirmed = driverConfirmation is not null,
            DriverConfirmedAtUtc = driverConfirmation?.ConfirmedAtUtc
        };
    }));
}).RequireAuthorization();

app.MapPost("/api/v1/rides/{rideId}/agreements", async (
    HttpContext context, ClaimsPrincipal user, string rideId, ProposeRideAgreementRequest request,
    IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    var ride = await FindRideAsync(rideId, ct);
    var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var isRider = user.IsInRole("Rider") && ride?.RiderId == actorId;
    var isDriver = user.IsInRole("Driver") && ride?.DriverId == actorId;
    if (ride is null || string.IsNullOrWhiteSpace(actorId) || (!isRider && !isDriver))
        return Results.NotFound(new { message = "Ride not found." });
    if (ride.Status is not ("Selected" or "Negotiating" or "AgreementConfirmed" or "Accepted" or "Arrived" or "InProgress"))
        return Results.Conflict(new { message = "Fare negotiation is not available in the current ride state." });
    if (request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount ||
        request.Currency is null || request.Currency.Length != 3 ||
        request.Currency.Any(character => character is < 'A' or > 'Z'))
        return Results.BadRequest(new { message = "Provide a positive amount with at most two decimal places and a three-letter uppercase currency code." });

    var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        return Results.BadRequest(new { message = "A valid Idempotency-Key header (1-128 characters) is required." });

    var assignment = await FindUnionAssignmentAsync(ride.DriverId!, ct);
    if (assignment is null || assignment.Status != "Active")
        return Results.Conflict(new { message = "The selected Driver must have an active Union membership before negotiation." });
    var union = await FindUnionAsync(assignment.UnionId, ct);
    var driver = await FindDriverProfileAsync(ride.DriverId!, ct);
    if (union?.Status != "Active" || driver?.Status != "Approved" ||
        !string.Equals(driver.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) ||
        !union.ServiceClasses.Contains("cab", StringComparer.OrdinalIgnoreCase) ||
        (ride.UnionId is not null && ride.UnionId != assignment.UnionId))
        return Results.Conflict(new { message = "The selected Driver or Union is not eligible for Cab negotiation." });

    var versions = ride.AgreementVersions ?? [];
    var scopedIdempotencyKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{actorId}:{idempotencyKey}")));
    var requestHash = CreateAgreementRequestHash(request.Amount, request.Currency);
    var retry = versions.FirstOrDefault(item => item.IdempotencyKey == scopedIdempotencyKey);
    if (retry is not null)
    {
        if (!AgreementRequestMatches(retry, requestHash))
            return Results.Conflict(new { message = "This Idempotency-Key was already used for a different agreement proposal." });

        var confirmations = ride.AgreementConfirmations ?? [];
        return Results.Ok(new
        {
            agreement = new { retry.RideId, retry.Version, retry.RiderId, retry.DriverId, retry.UnionId, retry.Amount, retry.Currency, retry.Pickup, retry.Dropoff, retry.Stops, retry.ProposedBy, retry.CreatedAtUtc },
            riderConfirmed = confirmations.Any(item => item.Version == retry.Version && item.Party == "Rider"),
            driverConfirmed = confirmations.Any(item => item.Version == retry.Version && item.Party == "Driver")
        });
    }

    var version = new RideAgreementVersion(
        rideId, (versions.LastOrDefault()?.Version ?? 0) + 1,
        ride.RiderId, ride.DriverId!, ride.UnionId ?? assignment.UnionId, request.Amount, request.Currency,
        ride.Pickup, ride.Dropoff, ride.Stops ?? [], isRider ? "Rider" : "Driver", scopedIdempotencyKey, DateTimeOffset.UtcNow, requestHash);
    var nextStatus = ride.Status == "AgreementConfirmed" ? "Negotiating" : ride.Status;
    var updated = ride with
    {
        AgreementVersions = [.. versions, version],
        AgreementStateRevision = ride.AgreementStateRevision + 1,
        Status = nextStatus,
        AuditEvents = AppendRideAuditEvent(ride, "AgreementProposed", actorId, isRider ? "Rider" : "Driver",
            ride.Status, nextStatus, version.Version, version.Amount, currency: version.Currency,
            occurredAtUtc: version.CreatedAtUtc)
    };
    if (!await TryReplaceRideAsync(ride, updated, ct))
    {
        var latest = await FindRideAsync(rideId, ct);
        var concurrentRetry = latest?.AgreementVersions?.FirstOrDefault(item => item.IdempotencyKey == scopedIdempotencyKey);
        if (concurrentRetry is not null)
        {
            if (!AgreementRequestMatches(concurrentRetry, requestHash))
                return Results.Conflict(new { message = "This Idempotency-Key was already used for a different agreement proposal." });

            var latestConfirmations = latest!.AgreementConfirmations ?? [];
            return Results.Ok(new
            {
                agreement = new { concurrentRetry.RideId, concurrentRetry.Version, concurrentRetry.RiderId, concurrentRetry.DriverId,
                    concurrentRetry.UnionId, concurrentRetry.Amount, concurrentRetry.Currency, concurrentRetry.Pickup,
                    concurrentRetry.Dropoff, concurrentRetry.Stops, concurrentRetry.ProposedBy, concurrentRetry.CreatedAtUtc },
                riderConfirmed = latestConfirmations.Any(item => item.Version == concurrentRetry.Version && item.Party == "Rider"),
                driverConfirmed = latestConfirmations.Any(item => item.Version == concurrentRetry.Version && item.Party == "Driver")
            });
        }
        return Results.Conflict(new { message = "Ride or agreement changed concurrently. Refresh the agreement history and retry with a new idempotency key." });
    }

    if (updated.Status != ride.Status)
        await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = updated.Status }, ct);
    await hub.Clients.Group(rideId).SendAsync("RideAgreementChanged", new { rideId, version = version.Version }, ct);
    return Results.Created($"/api/v1/rides/{rideId}/agreements", new
    {
        agreement = new { version.RideId, version.Version, version.RiderId, version.DriverId, version.UnionId, version.Amount, version.Currency, version.Pickup, version.Dropoff, version.Stops, version.ProposedBy, version.CreatedAtUtc }, riderConfirmed = false, driverConfirmed = false
    });
}).RequireAuthorization();

app.MapPost("/api/v1/rides/{rideId}/agreements/{version:int}/confirm", async (
    ClaimsPrincipal user, string rideId, int version, IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    var ride = await FindRideAsync(rideId, ct);
    var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var isRider = user.IsInRole("Rider") && ride?.RiderId == actorId;
    var isDriver = user.IsInRole("Driver") && ride?.DriverId == actorId;
    if (ride is null || string.IsNullOrWhiteSpace(actorId) || (!isRider && !isDriver))
        return Results.NotFound(new { message = "Ride agreement not found." });
    if (ride.Status is not ("Selected" or "Negotiating" or "AgreementConfirmed" or "Accepted" or "Arrived" or "InProgress"))
        return Results.Conflict(new { message = "Agreement confirmation is closed for this ride state." });

    var versions = ride.AgreementVersions ?? [];
    var current = versions.LastOrDefault();
    if (current is null || current.Version != version)
        return Results.Conflict(new { message = "Only the current agreement version can be confirmed." });

    var party = isRider ? "Rider" : "Driver";
    var confirmations = ride.AgreementConfirmations ?? [];
    var existing = confirmations.FirstOrDefault(item => item.Version == version && item.Party == party);
    if (existing is not null && existing.ActorId != actorId)
        return Results.Conflict(new { message = "This agreement party has already been confirmed by a different account." });

    var confirmationAtUtc = DateTimeOffset.UtcNow;
    var nextConfirmations = existing is null
        ? confirmations.Append(new RideAgreementConfirmation(rideId, version, party, actorId, confirmationAtUtc)).ToArray()
        : confirmations.ToArray();
    var riderConfirmed = nextConfirmations.Any(item => item.Version == version && item.Party == "Rider" && item.ActorId == current.RiderId);
    var driverConfirmed = nextConfirmations.Any(item => item.Version == version && item.Party == "Driver" && item.ActorId == current.DriverId);
    var statusAfterConfirmation = ride.Status;
    if (existing is null)
    {
        var nextStatus = ride.Status == "Negotiating" && riderConfirmed && driverConfirmed
            ? "AgreementConfirmed" : ride.Status;
        var updated = ride with
        {
            AgreementConfirmations = nextConfirmations,
            AgreementStateRevision = ride.AgreementStateRevision + 1,
            Status = nextStatus,
            AuditEvents = AppendRideAuditEvent(ride, "AgreementPartyConfirmed", actorId, party,
                ride.Status, nextStatus, version, current.Amount, currency: current.Currency,
                occurredAtUtc: confirmationAtUtc)
        };
        if (!await TryReplaceRideAsync(ride, updated, ct))
            return Results.Conflict(new { message = "Agreement changed concurrently. Refresh and confirm the current version." });
        statusAfterConfirmation = updated.Status;
    }
    if (statusAfterConfirmation != ride.Status)
        await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = statusAfterConfirmation }, ct);
    await hub.Clients.Group(rideId).SendAsync("RideAgreementChanged",
        new { rideId, version, riderConfirmed, driverConfirmed }, ct);
    return Results.Ok(new { rideId, version, riderConfirmed, driverConfirmed, mutuallyConfirmed = riderConfirmed && driverConfirmed });
}).RequireAuthorization();

app.MapPost("/api/v1/rides/{rideId}/start", async (
    ClaimsPrincipal user, string rideId, IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ride = await FindRideAsync(rideId, ct);
    if (ride is null || ride.DriverId != driverId) return Results.NotFound();
    if (ride.Status == "InProgress") return Results.Ok(ride);
    var current = (ride.AgreementVersions ?? []).LastOrDefault();
    var confirmations = ride.AgreementConfirmations ?? [];
    if (current is null ||
        !confirmations.Any(item => item.Version == current.Version && item.Party == "Rider" && item.ActorId == ride.RiderId) ||
        !confirmations.Any(item => item.Version == current.Version && item.Party == "Driver" && item.ActorId == ride.DriverId))
        return Results.Conflict(new { message = "Both Rider and Driver must confirm the current agreement version before the ride can start." });
    if (ride.Status != "Arrived")
        return Results.Conflict(new { message = $"Cannot move ride from {ride.Status} to InProgress." });
    var started = await TryTransitionAsync(ride, "InProgress", driverId!, "Driver", ct);
    if (started is null) return Results.Conflict(new { message = "Ride or agreement changed concurrently. Refresh and retry." });
    await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = started.Status }, ct);
    return Results.Ok(started);
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

async Task EnsureCompletedRideSideEffectsAsync(DemoRide completedRide, string driverId, CancellationToken ct)
{
    var completedAtUtc = (completedRide.AuditEvents ?? [])
        .LastOrDefault(item => item.EventType == "RideStatusChanged" && item.NewStatus == "Completed")
        ?.OccurredAtUtc ?? DateTimeOffset.UtcNow;
    await RecordWorkActivityAsync(driverId, completedRide.RideId, "Completed", completedAtUtc, ct);
    await driverRideReservations.TryReleaseAsync(driverId, completedRide.RideId, ct,
        new DriverRideReservationAuditContext(driverId, "Driver", DriverRideReservationAuditReasons.RideCompleted));
}

app.MapPost("/api/v1/rides/{rideId}/complete", async (
    ClaimsPrincipal user, string rideId, IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ride = await FindRideAsync(rideId, ct);
    if (ride is null || ride.DriverId != driverId) return Results.NotFound();
    if (ride.Status == "Completed")
    {
        await EnsureCompletedRideSideEffectsAsync(ride, driverId!, ct);
        return Results.Ok(ride);
    }
    var current = (ride.AgreementVersions ?? []).LastOrDefault();
    var confirmations = ride.AgreementConfirmations ?? [];
    if (current is null ||
        !confirmations.Any(item => item.Version == current.Version && item.Party == "Rider" && item.ActorId == ride.RiderId) ||
        !confirmations.Any(item => item.Version == current.Version && item.Party == "Driver" && item.ActorId == ride.DriverId))
        return Results.Conflict(new { message = "Both parties must confirm the current agreement version before completing the ride." });
    if (ride.Status != "InProgress")
        return Results.Conflict(new { message = $"Cannot move ride from {ride.Status} to Completed." });
    var completed = await TryTransitionAsync(ride, "Completed", driverId!, "Driver", ct);
    if (completed is null)
    {
        var latest = await ReadRideRawAsync(rideId, ct);
        if (latest is null || latest.DriverId != driverId || latest.Status != "Completed")
            return Results.Conflict(new { message = "Ride or agreement changed concurrently. Refresh and retry." });
        await EnsureCompletedRideSideEffectsAsync(latest, driverId!, ct);
        return Results.Ok(latest);
    }
    await EnsureCompletedRideSideEffectsAsync(completed, driverId!, ct);
    await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = completed.Status }, ct);
    return Results.Ok(completed);
}).RequireAuthorization(policy => policy.RequireRole("Driver"));


app.MapPost("/api/v1/driver/rides/{rideId}/payment-report", async (
    HttpContext context, ClaimsPrincipal user, string rideId, ReportRidePaymentRequest request, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ride = await FindRideAsync(rideId, ct);
    if (string.IsNullOrWhiteSpace(driverId) || ride is null || ride.DriverId != driverId) return Results.NotFound();
    if (!string.Equals(ride.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "Direct-payment reporting is available only for Cab rides." });
    if (ride.Status != "Completed")
        return Results.Conflict(new { message = "The Driver can report payment only after the ride is completed." });

    var current = (ride.AgreementVersions ?? []).LastOrDefault();
    var confirmations = ride.AgreementConfirmations ?? [];
    if (current is null || current.RiderId != ride.RiderId || current.DriverId != driverId ||
        string.IsNullOrWhiteSpace(ride.UnionId) ||
        !string.Equals(current.UnionId, ride.UnionId, StringComparison.Ordinal) ||
        !confirmations.Any(item => item.Version == current.Version &&
            item.Party == "Rider" && item.ActorId == ride.RiderId) ||
        !confirmations.Any(item => item.Version == current.Version &&
            item.Party == "Driver" && item.ActorId == driverId))
        return Results.Conflict(new { message = "A current, mutually confirmed Cab agreement and Union assignment are required." });

    if (request.AmountReceived is not decimal amountReceived || amountReceived < 0 ||
        decimal.Round(amountReceived, 2, MidpointRounding.AwayFromZero) != amountReceived)
        return Results.BadRequest(new { message = "An explicit amount received of zero or more with at most two decimal places is required." });

    var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        return Results.BadRequest(new { message = "A valid Idempotency-Key header (1-128 characters) is required." });
    var idempotencyKeyHash = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{rideId}:{driverId}:{idempotencyKey}")));

    if (ride.PaymentReconciliation is { } existing)
    {
        if (existing.IdempotencyKeyHash == idempotencyKeyHash &&
            existing.AmountReceived == amountReceived)
            return Results.Ok(ToPaymentReconciliationResponse(existing));
        return Results.Conflict(new { message = "A payment report is already recorded for this ride and is immutable." });
    }

    var report = new RidePaymentReconciliation(
        rideId, current.Version, ride.RiderId, driverId!, ride.UnionId!,
        current.Amount, amountReceived,
        decimal.Round(current.Amount - amountReceived, 2, MidpointRounding.AwayFromZero),
        current.Currency, DateTimeOffset.UtcNow, driverId!, "DriverReportedUnverified", idempotencyKeyHash);
    var updated = ride with
    {
        PaymentReconciliation = report,
        AuditEvents = AppendRideAuditEvent(ride, "PaymentReportedUnverified", driverId!, "Driver",
            ride.Status, ride.Status, current.Version, current.Amount, amountReceived, report.Delta,
            current.Currency, report.ReportedAtUtc)
    };
    if (!await TryReplaceRideAsync(ride, updated, ct))
    {
        var latest = await ReadRideRawAsync(rideId, ct);
        if (latest?.PaymentReconciliation is { } concurrent &&
            concurrent.IdempotencyKeyHash == idempotencyKeyHash &&
            concurrent.AmountReceived == amountReceived)
            return Results.Ok(ToPaymentReconciliationResponse(concurrent));
        return Results.Conflict(new { message = "The ride or payment report changed concurrently. Refresh the trip history before retrying." });
    }

    return Results.Ok(ToPaymentReconciliationResponse(report));
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapPost("/api/v1/rides/{rideId}/cancel", async (ClaimsPrincipal user, string rideId, IHubContext<RideTrackingHub> hub, CancellationToken ct) =>
{
    var riderId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ride = await FindRideAsync(rideId, ct);
    if (ride is null || ride.RiderId != riderId) return Results.NotFound();
    if (ride.Status == "Cancelled") return Results.Ok(ride);
    if (ride.Status is not ("Requested" or "Negotiating" or "AgreementConfirmed" or "Accepted" or "Arrived"))
        return Results.Conflict(new { message = "A ride can only be cancelled before it starts." });
    var updated = await TryTransitionAsync(ride, "Cancelled", riderId!, "Rider", ct);
    if (updated is null) return Results.Conflict(new { message = "Ride state changed. Refresh and try again." });
    if (!string.IsNullOrWhiteSpace(updated.DriverId))
        await driverRideReservations.TryReleaseAsync(updated.DriverId, rideId, ct,
            new DriverRideReservationAuditContext(riderId!, "Rider", DriverRideReservationAuditReasons.RiderCancelled));
    await hub.Clients.Group(rideId).SendAsync("RideStatusChanged", new { rideId, status = updated.Status }, ct);
    return Results.Ok(updated);
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

async Task<IResult> TransitionRideAsync(ClaimsPrincipal user, string rideId, string expected, string next, CancellationToken ct)
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ride = await FindRideAsync(rideId, ct);
    if (ride is null || ride.DriverId != driverId) return Results.NotFound();
    if (ride.Status == next) return Results.Ok(ride);
    if (ride.Status != expected) return Results.Conflict(new { message = $"Cannot move ride from {ride.Status} to {next}." });
    var updated = await TryTransitionAsync(ride, next, driverId!, "Driver", ct);
    if (updated is null) return Results.Conflict(new { message = "Ride state changed. Refresh and try again." });
    if (next == "Completed")
    {
        await RecordWorkActivityAsync(driverId!, rideId, "Completed", DateTimeOffset.UtcNow, ct);
        await driverRideReservations.TryReleaseAsync(driverId!, rideId, ct,
            new DriverRideReservationAuditContext(driverId!, "Driver", DriverRideReservationAuditReasons.RideCompleted));
    }
    await app.Services.GetRequiredService<IHubContext<RideTrackingHub>>().Clients.Group(rideId)
        .SendAsync("RideStatusChanged", new { rideId, status = updated.Status }, ct);
    return Results.Ok(updated);
}

async Task<DemoRide?> TryTransitionAsync(
    DemoRide current, string next, string actorId, string actorRole, CancellationToken ct)
{
    if (!RideLifecycle.CanTransition(current.Status, next)) return null;
    var occurredAtUtc = DateTimeOffset.UtcNow;
    var updated = current with
    {
        Status = next,
        LocationSharingActive = RideLifecycle.IsLocationSharingActive(next),
        NegotiationExpiresAtUtc = next is "Accepted" or "Cancelled" or "Expired" or "Completed"
            ? null : current.NegotiationExpiresAtUtc,
        AuditEvents = AppendRideAuditEvent(current, "RideStatusChanged", actorId, actorRole,
            current.Status, next, occurredAtUtc: occurredAtUtc)
    };
    if (!await TryReplaceRideAsync(current, updated, ct))
        return null;

    if (next is "Completed" or "Cancelled" or "Expired" &&
        !string.IsNullOrWhiteSpace(current.UnionId) &&
        string.Equals(current.ServiceClass, "cab", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(current.DriverId))
    {
        var reason = next switch
        {
            "Completed" => DriverRideReservationAuditReasons.RideCompleted,
            "Cancelled" when actorRole == "Rider" => DriverRideReservationAuditReasons.RiderCancelled,
            "Cancelled" when actorRole == "Driver" => DriverRideReservationAuditReasons.DriverDeclined,
            "Expired" => DriverRideReservationAuditReasons.ReservationExpiryRestored,
            _ => DriverRideReservationAuditReasons.InternalReconciliation
        };
        await riderActiveBookingReservations.TryReleaseAsync(
            current.RiderId, current.RideId, ct,
            new DriverRideReservationAuditContext(actorId, actorRole, reason));
    }

    return updated;
}

string CreateSelectedBookingRequestHash(
    string selectedDriverId, string unionId, string pickup, string dropoff,
    IReadOnlyList<string> stops, int passengers, double pickupLatitude, double pickupLongitude) =>
    Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        selectedDriverId, unionId, pickup, dropoff, stops,
        Passengers = passengers, PickupLatitude = pickupLatitude, PickupLongitude = pickupLongitude
    })));

string CreateAgreementRequestHash(decimal amount, string currency)
{
    // Amounts are already validated to two decimal places. Formatting invariantly makes
    // 450, 450.0 and 450.00 the same monetary request across retries and host cultures.
    var normalizedAmount = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{normalizedAmount}:{currency}")));
}

bool AgreementRequestMatches(RideAgreementVersion version, string requestHash)
{
    // Older persisted agreement versions have no RequestHash. Reconstruct their
    // fingerprint from the immutable stored terms without requiring a migration.
    var storedHash = version.RequestHash ?? CreateAgreementRequestHash(version.Amount, version.Currency);
    return string.Equals(storedHash, requestHash, StringComparison.Ordinal);
}

IReadOnlyList<RideAuditEvent> AppendRideAuditEvent(
    DemoRide ride, string eventType, string actorId, string actorRole,
    string? previousStatus = null, string? newStatus = null, int? agreementVersion = null,
    decimal? amount = null, decimal? amountReceived = null, decimal? delta = null,
    string? currency = null, DateTimeOffset? occurredAtUtc = null)
{
    var auditEvent = new RideAuditEvent(
        Guid.NewGuid().ToString("N"), ride.RideId, ride.UnionId, eventType, actorId, actorRole,
        previousStatus, newStatus, agreementVersion, amount, amountReceived, delta, currency,
        occurredAtUtc ?? DateTimeOffset.UtcNow);
    return (ride.AuditEvents ?? []).Append(auditEvent).ToArray();
}

bool IsRideAuditAppendOnly(IReadOnlyList<RideAuditEvent>? current, IReadOnlyList<RideAuditEvent>? updated)
{
    var previous = current ?? [];
    var next = updated ?? [];
    return next.Count >= previous.Count &&
        previous.SequenceEqual(next.Take(previous.Count)) &&
        next.Select(item => item.EventId).Distinct(StringComparer.Ordinal).Count() == next.Count;
}

bool IsSameRideSnapshot(DemoRide left, DemoRide right)
{
    if (left.RideId != right.RideId || left.Pickup != right.Pickup || left.Dropoff != right.Dropoff ||
        left.Passengers != right.Passengers || left.Status != right.Status || left.RequestedAt != right.RequestedAt ||
        left.EstimatedFareInr != right.EstimatedFareInr || left.RiderId != right.RiderId || left.DriverId != right.DriverId ||
        left.PickupLatitude != right.PickupLatitude || left.PickupLongitude != right.PickupLongitude ||
        left.DriverLatitude != right.DriverLatitude || left.DriverLongitude != right.DriverLongitude ||
        left.AcceptedAt != right.AcceptedAt || left.LocationUpdatedAt != right.LocationUpdatedAt ||
        left.DriverLocationCapturedAtUtc != right.DriverLocationCapturedAtUtc ||
        left.LocationSharingActive != right.LocationSharingActive || left.ServiceClass != right.ServiceClass ||
        left.DispatchZoneId != right.DispatchZoneId || left.AgreementStateRevision != right.AgreementStateRevision ||
        left.UnionId != right.UnionId || left.NegotiationExpiresAtUtc != right.NegotiationExpiresAtUtc ||
        left.DriverDisplayName != right.DriverDisplayName || left.DriverVehicleMake != right.DriverVehicleMake ||
        left.DriverVehicleModel != right.DriverVehicleModel || left.DriverDistanceToPickupKm != right.DriverDistanceToPickupKm ||
        left.UnionName != right.UnionName || left.PaymentReconciliation != right.PaymentReconciliation ||
        !(left.AuditEvents ?? []).SequenceEqual(right.AuditEvents ?? []) ||
        !(left.Stops ?? []).SequenceEqual(right.Stops ?? [], StringComparer.Ordinal))
        return false;

    var leftVersions = left.AgreementVersions ?? [];
    var rightVersions = right.AgreementVersions ?? [];
    if (leftVersions.Count != rightVersions.Count) return false;
    for (var index = 0; index < leftVersions.Count; index++)
    {
        var first = leftVersions[index];
        var second = rightVersions[index];
        if (first.RideId != second.RideId || first.Version != second.Version ||
            first.RiderId != second.RiderId || first.DriverId != second.DriverId || first.UnionId != second.UnionId ||
            first.Amount != second.Amount || first.Currency != second.Currency || first.Pickup != second.Pickup ||
            first.Dropoff != second.Dropoff || first.ProposedBy != second.ProposedBy ||
            first.IdempotencyKey != second.IdempotencyKey || first.CreatedAtUtc != second.CreatedAtUtc ||
            !first.Stops.SequenceEqual(second.Stops, StringComparer.Ordinal))
            return false;
    }

    return (left.AgreementConfirmations ?? []).SequenceEqual(right.AgreementConfirmations ?? []);
}

RidePaymentReconciliationResponse? ToPaymentReconciliationResponse(RidePaymentReconciliation? report) =>
    report is null ? null : new RidePaymentReconciliationResponse(
        report.RideId, report.AgreementVersion, report.UnionId, report.AgreedAmount,
        report.AmountReceived, report.Delta, report.Currency, report.ReportedAtUtc,
        report.ReportedByDriverId, report.Status);

async Task<bool> TryReplaceRideAsync(DemoRide current, DemoRide updated, CancellationToken ct)
{
    if (!IsRideAuditAppendOnly(current.AuditEvents, updated.AuditEvents))
        return false;

    var previousAuditCount = (current.AuditEvents ?? []).Count;
    var appendedAuditEvents = (updated.AuditEvents ?? []).Skip(previousAuditCount).ToArray();
    var auditEvent = appendedAuditEvents.LastOrDefault();
    using var activity = auditEvent is null ? null : TurnOvaDiagnostics.StartRideAuditWrite(auditEvent.EventType);

    try
    {
        bool replaced;
        if (rideContainer is null)
        {
            replaced = rides.TryUpdate(current.RideId, updated, current);
        }
        else
        {
            var response = await rideContainer.ReadItemAsync<DemoRide>(
                current.RideId, new PartitionKey(current.RideId), cancellationToken: ct);
            if (!IsSameRideSnapshot(response.Resource, current))
            {
                replaced = false;
            }
            else
            {
                await rideContainer.ReplaceItemAsync(
                    updated, current.RideId, new PartitionKey(current.RideId),
                    new ItemRequestOptions { IfMatchEtag = response.ETag }, ct);
                replaced = true;
            }
        }

        foreach (var item in appendedAuditEvents)
            TurnOvaDiagnostics.RecordRideAuditWrite(item.EventType, replaced ? "success" : "conflict");
        activity?.SetTag("turnova.audit.outcome", replaced ? "success" : "conflict");
        activity?.SetStatus(replaced ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        return replaced;
    }
    catch (CosmosException ex) when (
        ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
    {
        foreach (var item in appendedAuditEvents)
            TurnOvaDiagnostics.RecordRideAuditWrite(item.EventType, "conflict");
        activity?.SetTag("turnova.audit.outcome", "conflict");
        activity?.SetStatus(ActivityStatusCode.Error);
        return false;
    }
    catch
    {
        foreach (var item in appendedAuditEvents)
            TurnOvaDiagnostics.RecordRideAuditWrite(item.EventType, "failure");
        activity?.SetTag("turnova.audit.outcome", "failure");
        activity?.SetStatus(ActivityStatusCode.Error);
        throw;
    }
}
app.MapGet("/api/v1/driver/rides/mine", async (ClaimsPrincipal user, CancellationToken ct) =>
{
    var driverId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(driverId))
        return Results.Forbid();

    var history = new List<DemoRide>();
    if (rideContainer is null)
    {
        history.AddRange(rides.Values.Where(ride => ride.DriverId == driverId));
    }
    else
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.driverId = @driverId")
            .WithParameter("@driverId", driverId);
        using var iterator = rideContainer.GetItemQueryIterator<DemoRide>(query);
        while (iterator.HasMoreResults)
            history.AddRange(await iterator.ReadNextAsync(ct));
    }

    for (var index = 0; index < history.Count; index++)
    {
        var current = await FindRideAsync(history[index].RideId, ct);
        if (current is not null) history[index] = current;
    }

    var items = history
        .OrderByDescending(ride => ride.RequestedAt)
        .Select(ride =>
        {
            var currentAgreement = (ride.AgreementVersions ?? []).LastOrDefault();
            return new DriverRideHistoryItem(
                ride.RideId,
                ride.Pickup,
                ride.Dropoff,
                ride.Passengers,
                ride.ServiceClass,
                ride.Status,
                ride.RequestedAt,
                ride.AcceptedAt,
                ride.LocationSharingActive,
                ride.Stops ?? [],
                ride.NegotiationExpiresAtUtc,
                ToPaymentReconciliationResponse(ride.PaymentReconciliation),
                currentAgreement is null
                    ? null
                    : new RideAgreementSummary(currentAgreement.Version, currentAgreement.Amount, currentAgreement.Currency));
        })
        .ToArray();
    return Results.Ok(items);
}).RequireAuthorization(policy => policy.RequireRole("Driver"));

app.MapGet("/api/v1/rides/mine", async (ClaimsPrincipal user, CancellationToken ct) =>
{
    var riderId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(riderId))
        return Results.Forbid();

    var history = new List<DemoRide>();
    if (rideContainer is null)
    {
        history.AddRange(rides.Values.Where(ride => ride.RiderId == riderId));
    }
    else
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.riderId = @riderId")
            .WithParameter("@riderId", riderId);
        using var iterator = rideContainer.GetItemQueryIterator<DemoRide>(query);
        while (iterator.HasMoreResults)
            history.AddRange(await iterator.ReadNextAsync(ct));
    }

    for (var index = 0; index < history.Count; index++)
    {
        var current = await FindRideAsync(history[index].RideId, ct);
        if (current is not null) history[index] = current;
    }

    var items = history
        .OrderByDescending(ride => ride.RequestedAt)
        .Select(ride => new RiderRideHistoryItem(
            ride.RideId,
            ride.Pickup,
            ride.Dropoff,
            ride.Passengers,
            ride.ServiceClass,
            ride.Status,
            ride.RequestedAt,
            ride.AcceptedAt,
            ride.Stops ?? []))
        .ToArray();
    return Results.Ok(items);
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

app.MapGet("/api/v1/rides/{rideId}", async (ClaimsPrincipal user, string rideId, CancellationToken ct) =>
{
    var riderId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var ownedRide = await ReadRideRawAsync(rideId, ct);
    if (ownedRide is null || ownedRide.RiderId != riderId)
        return Results.NotFound(new { message = "Ride not found." });

    // Verify ownership before FindRideAsync can reconcile expiry or release a reservation.
    var ride = await FindRideAsync(rideId, ct);
    return ride is not null && ride.RiderId == riderId
        ? Results.Ok(ride)
        : Results.NotFound(new { message = "Ride not found." });
}).RequireAuthorization(policy => policy.RequireRole("Rider"));

app.Run();

public sealed record JoinQueueRequest(string ZoneId, string? ServiceClass);
public sealed record DriverAvailabilityRequest(bool IsOnline, double? Latitude = null, double? Longitude = null, DateTimeOffset? CapturedAtUtc = null);
public sealed record DriverDocumentRequest(string DocumentType, string StorageReference);
public sealed record DriverOnboardingRequest(string ZoneId, string FullName, string PhoneNumber, string VehicleMake, string VehicleModel, string VehicleRegistration, string ServiceClass, int PassengerCapacity, IReadOnlyList<DriverDocumentRequest> Documents, string? ProfilePhotoBase64);
public sealed record DriverSelfieRequest(string ProfilePhotoBase64);
public sealed record DriverSelfieReviewRequest(string Status, string? Reason);
public sealed record DriverReviewRequest(string Status, string? Reason);
public sealed record CreateUnionRequest(string Name, string Code, string? Location, string? ManagerName, string? ManagerPhone, IReadOnlyList<string> ZoneIds, IReadOnlyList<string> ServiceClasses);
public sealed record UpdateUnionStatusRequest(string Status);
public sealed record AssignDriverUnionRequest(string UnionId, string Reason);
public sealed record CreateRideRequest(string Pickup, string Dropoff, int Passengers = 1, double? PickupLatitude = null, double? PickupLongitude = null, string ServiceClass = "cab");
public sealed record ProposeRideAgreementRequest(decimal Amount, string Currency);
public sealed record ReportRidePaymentRequest(decimal? AmountReceived);
public sealed record RiderSelectedBookingRequest(string SelectedDriverId, string UnionId, string Pickup, string Dropoff,
    IReadOnlyList<string>? Stops, int Passengers, double PickupLatitude, double PickupLongitude);
public sealed record DriverLocationRequest(double Latitude, double Longitude, DateTimeOffset? CapturedAtUtc = null);
public sealed record DriverAvailabilityLocationRequest(double? Latitude, double? Longitude, DateTimeOffset? CapturedAtUtc = null);
public sealed record RiderDiscoveryResponse(string PickupZoneId, double? JourneyDistanceKm, IReadOnlyList<NearbyUnionDiscoveryGroup> Unions);
public sealed record NearbyUnionDiscoveryGroup(string UnionId, string UnionName, IReadOnlyList<NearbyDriverDiscoveryResponse> Drivers);
public sealed record NearbyDriverDiscoveryResponse(string DriverId, string FullName, string VehicleMake, string VehicleModel, string ServiceClass, int PassengerCapacity, double DistanceToPickupKm, string? ProfilePhotoBase64, string? ProfilePhotoContentType);
public sealed record RiderRideHistoryItem(string RideId, string Pickup, string Dropoff, int Passengers, string ServiceClass, string Status, DateTimeOffset RequestedAt, DateTimeOffset? AcceptedAt, IReadOnlyList<string> Stops);
public sealed record DriverRideHistoryItem(string RideId, string Pickup, string Dropoff, int Passengers, string ServiceClass, string Status, DateTimeOffset RequestedAt, DateTimeOffset? AcceptedAt, bool LocationSharingActive, IReadOnlyList<string> Stops, DateTimeOffset? NegotiationExpiresAtUtc = null, RidePaymentReconciliationResponse? PaymentReconciliation = null, RideAgreementSummary? CurrentAgreement = null);
public sealed record RideAgreementSummary(int Version, decimal Amount, string Currency);
public sealed record RidePaymentReconciliationResponse(string RideId, int AgreementVersion, string UnionId, decimal AgreedAmount, decimal AmountReceived, decimal Delta, string Currency, DateTimeOffset ReportedAtUtc, string ReportedByDriverId, string Status);
public sealed record RideIdempotencyRecord(string id, string riderId, string rideId, string? RequestHash = null, DateTimeOffset? CreatedAtUtc = null);
public sealed record PasswordRegisterRequest(string Email, string Password, string? Name, bool ReplaceOldest = false);
public sealed record PasswordLoginRequest(string Email, string Password, bool ReplaceOldest = false);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record DemoRide(
    string RideId, string Pickup, string Dropoff, int Passengers,
    string Status, DateTimeOffset RequestedAt, decimal? EstimatedFareInr, string RiderId, string? DriverId, double? PickupLatitude, double? PickupLongitude, double? DriverLatitude, double? DriverLongitude, DateTimeOffset? AcceptedAt, DateTimeOffset? LocationUpdatedAt, bool LocationSharingActive = false, string ServiceClass = "cab", string DispatchZoneId = "hyderabad-central",
    IReadOnlyList<RideAgreementVersion>? AgreementVersions = null, IReadOnlyList<RideAgreementConfirmation>? AgreementConfirmations = null,
    int AgreementStateRevision = 0, string? UnionId = null, IReadOnlyList<string>? Stops = null, DateTimeOffset? NegotiationExpiresAtUtc = null,
    string? DriverDisplayName = null, string? DriverVehicleMake = null, string? DriverVehicleModel = null,
    double? DriverDistanceToPickupKm = null, string? UnionName = null,
    RidePaymentReconciliation? PaymentReconciliation = null,
    IReadOnlyList<RideAuditEvent>? AuditEvents = null,
    DateTimeOffset? DriverLocationCapturedAtUtc = null);

public sealed record DispatchZoneConfig(string ZoneId, double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude);

public partial class Program;
