using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class RideApiIntegrationTests
{
    private const string Issuer = "turnova-integration-tests";
    private const string Audience = "turnova-api";
    private const string SigningKey = "turnova-integration-test-signing-key-32-bytes-minimum";

    [Fact]
    public async Task ReadinessEndpointReturnsReadyInInMemoryMode()
    {
        using var factory = new TurnOvaApiFactory();
        using var client = factory.CreateClient("Rider", "rider-health");

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DriverMustSubmitOnboardingAndBeApprovedBeforeJoiningQueue()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-onboarding-pending");
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()
            .TryRemove("driver-onboarding-pending", out _);

        var submission = await driver.PostAsJsonAsync("/api/v1/driver/onboarding", ValidOnboardingRequest());
        Assert.Equal(HttpStatusCode.Accepted, submission.StatusCode);

        var profile = await driver.GetFromJsonAsync<DriverOnboardingProfile>("/api/v1/driver/onboarding/me");
        Assert.NotNull(profile);
        Assert.Equal("Pending", profile.Status);

        var join = await driver.PostAsJsonAsync("/api/v1/queues/join",
            new { zoneId = "hyderabad-central", serviceClass = "auto" });
        Assert.Equal(HttpStatusCode.Conflict, join.StatusCode);

        using var approval = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-onboarding-pending/review")
        {
            Content = JsonContent.Create(new { status = "Approved", reason = (string?)null })
        };
        approval.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        var approved = await driver.SendAsync(approval);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var joined = await driver.PostAsJsonAsync("/api/v1/queues/join",
            new { zoneId = "hyderabad-central", serviceClass = "auto" });
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);

        var wrongZone = await driver.PostAsJsonAsync("/api/v1/queues/join",
            new { zoneId = "hyderabad-north", serviceClass = "auto" });
        Assert.Equal(HttpStatusCode.Forbidden, wrongZone.StatusCode);

        using var suspension = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-onboarding-pending/review")
        {
            Content = JsonContent.Create(new { status = "Suspended", reason = "Policy review" })
        };
        suspension.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.OK, (await driver.SendAsync(suspension)).StatusCode);
        var membership = await driver.GetFromJsonAsync<QueueMembershipTest>(
            "/api/v1/zones/hyderabad-central/queues/auto/me");
        Assert.NotNull(membership);
        Assert.False(membership.IsQueued);
    }

    [Fact]
    public async Task InvalidOnboardingMetadataIsRejectedAndSuspendedDriverCannotAcceptRide()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-onboarding-suspended");
        using var rider = factory.CreateClient("Rider", "rider-onboarding-suspended");

        var invalid = await driver.PostAsJsonAsync("/api/v1/driver/onboarding",
            new { fullName = "Driver", phoneNumber = "123", vehicleMake = "T", vehicleModel = "M",
                vehicleRegistration = "TS01AA1234", serviceClass = "auto", passengerCapacity = 2,
                documents = new[] { new { documentType = "DrivingLicence", storageReference = "https://example.com/doc.pdf" } } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var ride = await CreateRideAsync(rider, "request-suspended-driver-001");
        using var review = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-onboarding-suspended/review")
        {
            Content = JsonContent.Create(new { status = "Suspended", reason = "Verification failed" })
        };
        review.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.OK, (await driver.SendAsync(review)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await driver.PostAsync($"/api/v1/rides/{ride.RideId}/accept", null)).StatusCode);
    }


    [Fact]
    public async Task OperationsCanCreateUnionAssignDriverTransferAndReadAuditHistory()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-union-transfer");
        using var ops = factory.CreateClient("Rider", "ops-union-test");

        async Task<string> CreateUnionAsync(string name, string code)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/unions")
            {
                Content = JsonContent.Create(new
                {
                    name,
                    code,
                    location = "Hyderabad",
                    managerName = "Union Manager",
                    managerPhone = "+919876543210",
                    zoneIds = new[] { "hyderabad-central" },
                    serviceClasses = new[] { "auto" }
                })
            };
            request.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
            using var response = await ops.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("unionId").GetString()!;
        }

        var firstUnionId = await CreateUnionAsync("Central Drivers Union", "HYD-CENTRAL");
        var secondUnionId = await CreateUnionAsync("Metro Drivers Union", "HYD-METRO");

        using var assign = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-union-transfer/union-assignment")
        {
            Content = JsonContent.Create(new { unionId = firstUnionId, reason = "Initial driver onboarding" })
        };
        assign.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.OK, (await ops.SendAsync(assign)).StatusCode);

        var current = await driver.GetFromJsonAsync<DriverUnionAssignmentTest>("/api/v1/driver/union");
        Assert.NotNull(current);
        Assert.Equal(firstUnionId, current.UnionId);

        using var transfer = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-union-transfer/union-assignment")
        {
            Content = JsonContent.Create(new { unionId = secondUnionId, reason = "Operational capacity balancing" })
        };
        transfer.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.OK, (await ops.SendAsync(transfer)).StatusCode);

        current = await driver.GetFromJsonAsync<DriverUnionAssignmentTest>("/api/v1/driver/union");
        Assert.NotNull(current);
        Assert.Equal(secondUnionId, current.UnionId);

        using var historyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ops/drivers/driver-union-transfer/union-history");
        historyRequest.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var historyResponse = await ops.SendAsync(historyRequest);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<DriverUnionAuditTest>>();
        Assert.NotNull(history);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, item => item.EventType == "Assigned" && item.NewUnionId == firstUnionId);
        Assert.Contains(history, item => item.EventType == "Transferred" &&
            item.PreviousUnionId == firstUnionId && item.NewUnionId == secondUnionId &&
            item.Reason == "Operational capacity balancing");
    }

    [Fact]
    public async Task UnionAssignmentRejectsPendingAndZoneIncompatibleDrivers()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-union-invalid");
        using var ops = factory.CreateClient("Rider", "ops-union-invalid");
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()
            .TryRemove("driver-union-invalid", out _);

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/unions")
        {
            Content = JsonContent.Create(new
            {
                name = "North Drivers Union",
                code = "HYD-NORTH",
                zoneIds = new[] { "hyderabad-north" },
                serviceClasses = new[] { "auto" }
            })
        };
        create.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var created = await ops.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var unionJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var unionId = unionJson.RootElement.GetProperty("unionId").GetString()!;

        var submitted = await driver.PostAsJsonAsync("/api/v1/driver/onboarding", ValidOnboardingRequest());
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);

        using var pendingAssignment = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-union-invalid/union-assignment")
        {
            Content = JsonContent.Create(new { unionId, reason = "Initial assignment" })
        };
        pendingAssignment.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.Conflict, (await ops.SendAsync(pendingAssignment)).StatusCode);

        using var review = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-union-invalid/review")
        {
            Content = JsonContent.Create(new { status = "Approved", reason = (string?)null })
        };
        review.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.OK, (await ops.SendAsync(review)).StatusCode);

        using var incompatibleAssignment = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-union-invalid/union-assignment")
        {
            Content = JsonContent.Create(new { unionId, reason = "Zone mismatch" })
        };
        incompatibleAssignment.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.Conflict, (await ops.SendAsync(incompatibleAssignment)).StatusCode);
    }

    private sealed record DriverUnionAssignmentTest(string UnionId, string Status);
    private sealed record DriverUnionAuditTest(string EventType, string? PreviousUnionId, string NewUnionId, string Reason);

    private sealed record QueueMembershipTest(bool IsQueued, int? Position);

    private static object ValidOnboardingRequest() => new
    {
        zoneId = "hyderabad-central",
        fullName = "Test Driver",
        phoneNumber = "+919876543210",
        vehicleMake = "Bajaj",
        vehicleModel = "RE",
        vehicleRegistration = "TS09AB1234",
        serviceClass = "auto",
        passengerCapacity = 3,
        documents = new[]
        {
            new { documentType = "DrivingLicence", storageReference = "drivers/test/licence" },
            new { documentType = "VehicleRegistration", storageReference = "drivers/test/rc" }
        }
    };

    [Fact]
    public async Task RiderCanCreateRideAndRepeatRequestWithoutCreatingDuplicate()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-idempotency");
        using var firstRequest = CreateRideRequest("request-idempotency-001");

        var firstResponse = await rider.PostAsync("/api/v1/rides/request", firstRequest);
        var firstRide = await ReadRideAsync(firstResponse);

        using var retryRequest = CreateRideRequest("request-idempotency-001");
        var retryResponse = await rider.PostAsync("/api/v1/rides/request", retryRequest);
        var retryRide = await ReadRideAsync(retryResponse);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(firstRide.RideId, retryRide.RideId);
        Assert.Equal("Requested", retryRide.Status);
    }

    [Fact]
    public async Task RideLifecycleEnforcesTransitionsAndStopsLocationSharingAfterCompletion()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-lifecycle");
        using var driver = factory.CreateClient("Driver", "driver-lifecycle");
        var ride = await CreateRideAsync(rider, "request-lifecycle-001");

        var invalidStart = await driver.PostAsync($"/api/v1/rides/{ride.RideId}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, invalidStart.StatusCode);

        var accepted = await driver.PostAsync($"/api/v1/rides/{ride.RideId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True((await ReadRideAsync(accepted)).LocationSharingActive);

        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{ride.RideId}/arrive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{ride.RideId}/start", null)).StatusCode);

        var completed = await driver.PostAsync($"/api/v1/rides/{ride.RideId}/complete", null);
        var completedRide = await ReadRideAsync(completed);
        Assert.Equal("Completed", completedRide.Status);
        Assert.False(completedRide.LocationSharingActive);

        var location = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{ride.RideId}/location",
            new { latitude = 17.4, longitude = 78.4 });
        Assert.Equal(HttpStatusCode.NotFound, location.StatusCode);
    }

    [Fact]
    public async Task OnlyAssignedDriverCanAdvanceRideAndOnlyOwningRiderCanReadIt()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-owner");
        using var otherRider = factory.CreateClient("Rider", "rider-other");
        using var driver = factory.CreateClient("Driver", "driver-assigned");
        using var otherDriver = factory.CreateClient("Driver", "driver-other");
        var ride = await CreateRideAsync(rider, "request-ownership-001");

        Assert.Equal(HttpStatusCode.NotFound,
            (await otherRider.GetAsync($"/api/v1/rides/{ride.RideId}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{ride.RideId}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await otherDriver.PostAsync($"/api/v1/rides/{ride.RideId}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherDriver.PostAsync($"/api/v1/rides/{ride.RideId}/arrive", null)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentDriverAcceptanceAllowsExactlyOneWinner()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-concurrency");
        using var firstDriver = factory.CreateClient("Driver", "driver-race-a");
        using var secondDriver = factory.CreateClient("Driver", "driver-race-b");
        var ride = await CreateRideAsync(rider, "request-concurrency-001");

        var results = await Task.WhenAll(
            firstDriver.PostAsync($"/api/v1/rides/{ride.RideId}/accept", null),
            secondDriver.PostAsync($"/api/v1/rides/{ride.RideId}/accept", null));

        Assert.Single(results.Where(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Single(results.Where(response => response.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task RiderCanCancelBeforeTripStartsButNotAfterItStarts()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-cancel-before");
        using var driver = factory.CreateClient("Driver", "driver-cancel-before");
        var cancelBefore = await CreateRideAsync(rider, "request-cancel-before-001");
        await driver.PostAsync($"/api/v1/rides/{cancelBefore.RideId}/accept", null);

        var cancelled = await rider.PostAsync($"/api/v1/rides/{cancelBefore.RideId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await ReadRideAsync(cancelled)).Status);

        var secondRide = await CreateRideAsync(rider, "request-cancel-after-001");
        await driver.PostAsync($"/api/v1/rides/{secondRide.RideId}/accept", null);
        await driver.PostAsync($"/api/v1/rides/{secondRide.RideId}/arrive", null);
        await driver.PostAsync($"/api/v1/rides/{secondRide.RideId}/start", null);

        var lateCancellation = await rider.PostAsync($"/api/v1/rides/{secondRide.RideId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, lateCancellation.StatusCode);
    }

    [Fact]
    public async Task RideRequestRequiresIdempotencyKeyAndRiderRole()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-validation");
        using var driver = factory.CreateClient("Driver", "driver-validation");

        using var requestWithoutKey = CreateRideRequest(null);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rider.PostAsync("/api/v1/rides/request", requestWithoutKey)).StatusCode);

        using var driverRequest = CreateRideRequest("driver-must-not-book-001");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await driver.PostAsync("/api/v1/rides/request", driverRequest)).StatusCode);
    }

    private static HttpRequestMessage CreateRideRequest(string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rides/request")
        {
            Content = JsonContent.Create(new
            {
                pickup = "KPHB, Hyderabad",
                dropoff = "HITEC City, Hyderabad",
                passengers = 1,
                pickupLatitude = 17.493,
                pickupLongitude = 78.399
            })
        };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task<TestRide> CreateRideAsync(HttpClient rider, string key)
    {
        using var response = await rider.PostAsync("/api/v1/rides/request", CreateRideRequest(key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadRideAsync(response);
    }

    private static async Task<TestRide> ReadRideAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TestRide>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The API returned an empty ride response.");
    }

    private sealed record TestRide(string RideId, string Status, bool LocationSharingActive);

    private sealed class TurnOvaApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Jwt:Issuer"] = Issuer,
                    ["Auth:Jwt:Audience"] = Audience,
                    ["Auth:Jwt:SigningKey"] = SigningKey,
                    ["TriSend:BaseUrl"] = "https://localhost:7443",
                    ["DriverOnboarding:AdminApiKey"] = "integration-test-admin-key-32chars"
                }));
        }

        public HttpClient CreateClient(string role, string subject)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(role, subject));
            if (role == "Driver")
            {
                var profiles = Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
                profiles[subject] = new DriverOnboardingProfile(
                    subject, subject, "hyderabad-central", "Approved Test Driver", "+919876543210",
                    "Bajaj", "RE", "TS09AB1234", "auto", 3,
                    [new DriverDocumentReference("DrivingLicence", "test/licence")],
                    "Approved", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            }
            return client;
        }

        private static string CreateToken(string role, string subject)
        {
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims:
                [
                    new Claim("sub", subject),
                    new Claim("role", role),
                    new Claim("name", subject)
                ],
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
