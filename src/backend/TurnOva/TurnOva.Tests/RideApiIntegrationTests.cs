using TurnOva.API;
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
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class RideApiIntegrationTests
{
    private const string Issuer = "turnova-integration-tests";
    private const string Audience = "turnova-api";
    private const string SigningKey = "turnova-integration-test-signing-key-32-bytes-minimum";

    [Fact]
    public async Task HealthEndpointsExposeLivenessAndCosmosAwareReadiness()
    {
        using var factory = new TurnOvaApiFactory();
        using var client = factory.CreateClient("Rider", "rider-health");

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var json = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.Equal("ready", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("demo-in-memory", json.RootElement.GetProperty("storage").GetString());
    }

    [Fact]
    public async Task CorsAllowsOnlyConfiguredOrigins()
    {
        using var factory = new TurnOvaApiFactory();
        using var client = factory.CreateClient("Rider", "rider-cors");

        using var allowedRequest = new HttpRequestMessage(HttpMethod.Options, "/health/ready");
        allowedRequest.Headers.Add("Origin", "https://app.turnova.test");
        allowedRequest.Headers.Add("Access-Control-Request-Method", "GET");
        using var allowedResponse = await client.SendAsync(allowedRequest);

        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
        Assert.Equal("https://app.turnova.test",
            Assert.Single(allowedResponse.Headers.GetValues("Access-Control-Allow-Origin")));

        using var deniedRequest = new HttpRequestMessage(HttpMethod.Options, "/health/ready");
        deniedRequest.Headers.Add("Origin", "https://untrusted.example");
        deniedRequest.Headers.Add("Access-Control-Request-Method", "GET");
        using var deniedResponse = await client.SendAsync(deniedRequest);

        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task UnhandledExceptionReturnsGenericProblemDetailsWithTraceId()
    {
        using var factory = new TurnOvaApiFactory();
        using var client = factory.CreateClient("Rider", "rider-exception");

        using var response = await client.GetAsync("/__test/throw");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("An unexpected error occurred.", body);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("sensitive-test-exception", body);
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
            new { zoneId = "hyderabad-central", serviceClass = "cab" });
        Assert.Equal(HttpStatusCode.Conflict, join.StatusCode);

        using var approval = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-onboarding-pending/review")
        {
            Content = JsonContent.Create(new { status = "Approved", reason = (string?)null })
        };
        approval.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        var approved = await driver.SendAsync(approval);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var onboardingProfiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        onboardingProfiles["driver-onboarding-pending"] = onboardingProfiles["driver-onboarding-pending"] with
        {
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", "drivers/test/licence", "Approved"),
                new DriverDocumentReference("VehicleRegistration", "drivers/test/rc", "Approved")
            ]
        };
        SeedActiveCabUnion(factory, "driver-onboarding-pending");

        var online = await driver.PostAsJsonAsync("/api/v1/driver/availability", new { isOnline = true, latitude = 17.493, longitude = 78.399 });
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);
        var eligible = await factory.Services.GetRequiredService<IQueueStore>()
            .GetEligibleAsync("hyderabad-central", "cab", CancellationToken.None);
        Assert.Contains(eligible, entry => entry.DriverId == "driver-onboarding-pending");

        var wrongZone = await driver.PostAsJsonAsync("/api/v1/queues/join",
            new { zoneId = "hyderabad-north", serviceClass = "cab" });
        Assert.Equal(HttpStatusCode.Forbidden, wrongZone.StatusCode);

        using var suspension = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-onboarding-pending/review")
        {
            Content = JsonContent.Create(new { status = "Suspended", reason = "Policy review" })
        };
        suspension.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.OK, (await driver.SendAsync(suspension)).StatusCode);
        var membership = await driver.GetFromJsonAsync<QueueMembershipTest>(
            "/api/v1/zones/hyderabad-central/queues/cab/me");
        Assert.NotNull(membership);
        Assert.False(membership.IsQueued);
    }

    [Fact]
    public async Task DriverAvailabilityControlsQueueEligibilityAndRejoinsAtBack()
    {
        using var factory = new TurnOvaApiFactory();
        using var first = factory.CreateClient("Driver", "availability-first");
        using var second = factory.CreateClient("Driver", "availability-second");
        SeedActiveCabUnion(factory, "availability-first");
        SeedActiveCabUnion(factory, "availability-second");

        Assert.Equal(HttpStatusCode.Conflict,
            (await first.PostAsJsonAsync("/api/v1/queues/join", new { zoneId = "hyderabad-central", serviceClass = "cab" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await first.PostAsJsonAsync("/api/v1/driver/availability", new { isOnline = true, latitude = 17.493, longitude = 78.399 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await second.PostAsJsonAsync("/api/v1/driver/availability", new { isOnline = true, latitude = 17.493, longitude = 78.399 })).StatusCode);

        var before = await first.GetFromJsonAsync<List<QueueEntryTest>>("/api/v1/zones/hyderabad-central/queues/cab");
        Assert.NotNull(before);
        Assert.Equal(new[] { "availability-first", "availability-second" }, before.Select(x => x.DriverId));

        Assert.Equal(HttpStatusCode.OK,
            (await first.PostAsJsonAsync("/api/v1/driver/availability", new { isOnline = false })).StatusCode);
        var afterOffline = await second.GetFromJsonAsync<List<QueueEntryTest>>("/api/v1/zones/hyderabad-central/queues/cab");
        Assert.NotNull(afterOffline);
        Assert.Equal(new[] { "availability-second" }, afterOffline.Select(x => x.DriverId));

        Assert.Equal(HttpStatusCode.OK,
            (await first.PostAsJsonAsync("/api/v1/driver/availability", new { isOnline = true, latitude = 17.493, longitude = 78.399 })).StatusCode);
        var rejoined = await second.GetFromJsonAsync<List<QueueEntryTest>>("/api/v1/zones/hyderabad-central/queues/cab");
        Assert.NotNull(rejoined);
        Assert.Equal(new[] { "availability-second", "availability-first" }, rejoined.Select(x => x.DriverId));
    }

    [Fact]
    public async Task DriverCannotGoOnlineWithoutActiveCabUnionAndApprovedDocuments()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "availability-unverified-driver");

        using var noUnion = await driver.PostAsJsonAsync("/api/v1/driver/availability",
            new { isOnline = true, latitude = 17.493, longitude = 78.399 });
        Assert.Equal(HttpStatusCode.Conflict, noUnion.StatusCode);
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        Assert.Equal("Offline", profiles["availability-unverified-driver"].AvailabilityStatus);
        var queue = await driver.GetFromJsonAsync<List<QueueEntryTest>>(
            "/api/v1/zones/hyderabad-central/queues/cab");
        Assert.DoesNotContain(queue!, entry => entry.DriverId == "availability-unverified-driver");

        SeedActiveCabUnion(factory, "availability-unverified-driver");
        profiles["availability-unverified-driver"] = profiles["availability-unverified-driver"] with
        {
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", "test/licence", "Pending"),
                new DriverDocumentReference("VehicleRegistration", "test/vehicle-registration", "Approved")
            ]
        };
        using var pendingDocuments = await driver.PostAsJsonAsync("/api/v1/driver/availability",
            new { isOnline = true, latitude = 17.493, longitude = 78.399 });
        Assert.Equal(HttpStatusCode.Conflict, pendingDocuments.StatusCode);
        Assert.Equal("Offline", profiles["availability-unverified-driver"].AvailabilityStatus);

        profiles["availability-unverified-driver"] = profiles["availability-unverified-driver"] with
        {
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", "test/licence", "Approved"),
                new DriverDocumentReference("VehicleRegistration", "test/vehicle-registration", "Approved")
            ]
        };
        using var eligible = await driver.PostAsJsonAsync("/api/v1/driver/availability",
            new { isOnline = true, latitude = 17.493, longitude = 78.399 });
        Assert.Equal(HttpStatusCode.OK, eligible.StatusCode);
        Assert.Equal("Online", profiles["availability-unverified-driver"].AvailabilityStatus);
    }

    [Fact]
    public async Task AvailabilityAndLocationRemainUnchangedDuringSelectedDriverNegotiation()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "availability-reservation-rider");
        using var driver = factory.CreateClient("Driver", "availability-reservation-driver");
        SeedBookableDriver(factory, "availability-reservation-driver", "availability-reservation-union");

        using var bookingRequest = CreateSelectedBookingRequest(
            "availability-reservation-key", "availability-reservation-driver",
            "availability-reservation-union", "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(bookingRequest);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var before = profiles["availability-reservation-driver"];
        using var offline = await driver.PostAsJsonAsync("/api/v1/driver/availability",
            new { isOnline = false });
        Assert.Equal(HttpStatusCode.Conflict, offline.StatusCode);

        using var zoneExit = await driver.PostAsJsonAsync("/api/v1/driver/location",
            new { latitude = 0d, longitude = 0d });
        Assert.Equal(HttpStatusCode.Conflict, zoneExit.StatusCode);

        var after = profiles["availability-reservation-driver"];
        Assert.Equal("Online", after.AvailabilityStatus);
        Assert.Equal(before.CurrentLatitude, after.CurrentLatitude);
        Assert.Equal(before.CurrentLongitude, after.CurrentLongitude);
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("availability-reservation-driver"));
    }

    [Fact]
    public async Task InvalidOnboardingMetadataIsRejectedAndSuspendedDriverCannotAcceptRide()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-onboarding-suspended");
        using var rider = factory.CreateClient("Rider", "rider-onboarding-suspended");

        var invalid = await driver.PostAsJsonAsync("/api/v1/driver/onboarding",
            new { fullName = "Driver", phoneNumber = "123", vehicleMake = "T", vehicleModel = "M",
                vehicleRegistration = "TS01AA1234", serviceClass = "cab", passengerCapacity = 2,
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
                    serviceClasses = new[] { "cab" }
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
                serviceClasses = new[] { "cab" }
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

    [Fact]
    public async Task CabOnlyScopeRejectsNonCabNewWorkflowsAndPreservesLegacyProfiles()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-cab-scope");
        using var legacyDriver = factory.CreateClient("Driver", "driver-legacy-auto");
        using var ops = factory.CreateClient("Rider", "ops-cab-scope");

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var seeded = profiles["driver-legacy-auto"];
        profiles["driver-legacy-auto"] = seeded with { ServiceClass = "auto", AvailabilityStatus = "Offline" };

        var legacyProfile = await legacyDriver.GetFromJsonAsync<DriverOnboardingProfile>("/api/v1/driver/onboarding/me");
        Assert.NotNull(legacyProfile);
        Assert.Equal("auto", legacyProfile.ServiceClass);

        var online = await legacyDriver.PostAsJsonAsync("/api/v1/driver/availability",
            new { isOnline = true, latitude = 17.493, longitude = 78.399 });
        Assert.Equal(HttpStatusCode.Conflict, online.StatusCode);

        var join = await legacyDriver.PostAsJsonAsync("/api/v1/queues/join",
            new { zoneId = "hyderabad-central", serviceClass = "auto" });
        Assert.Equal(HttpStatusCode.Conflict, join.StatusCode);

        var legacyReplacement = await legacyDriver.PostAsJsonAsync("/api/v1/driver/onboarding", ValidOnboardingRequest("cab"));
        Assert.Equal(HttpStatusCode.Conflict, legacyReplacement.StatusCode);
        Assert.Equal("auto", profiles["driver-legacy-auto"].ServiceClass);

        using var nonCabRide = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rides/request")
        {
            Content = JsonContent.Create(new
            {
                pickup = "Pickup landmark",
                dropoff = "Drop-off landmark",
                passengers = 1,
                serviceClass = "auto",
                pickupLatitude = 17.493,
                pickupLongitude = 78.399
            })
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await rider.SendAsync(nonCabRide)).StatusCode);

        Assert.Equal(HttpStatusCode.Gone,
            (await rider.GetAsync("/api/v1/rides/nearby?latitude=17.493&longitude=78.399&serviceClass=auto&passengers=1")).StatusCode);

        using var nonCabUnion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/unions")
        {
            Content = JsonContent.Create(new
            {
                name = "Legacy Auto Union",
                code = "LEGACY-AUTO",
                zoneIds = new[] { "hyderabad-central" },
                serviceClasses = new[] { "auto" }
            })
        };
        nonCabUnion.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.BadRequest, (await ops.SendAsync(nonCabUnion)).StatusCode);

        using var cabUnion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/unions")
        {
            Content = JsonContent.Create(new
            {
                name = "Cab Union",
                code = "CAB-UNION",
                zoneIds = new[] { "hyderabad-central" },
                serviceClasses = new[] { "cab" }
            })
        };
        cabUnion.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var cabUnionResponse = await ops.SendAsync(cabUnion);
        Assert.Equal(HttpStatusCode.Created, cabUnionResponse.StatusCode);
        using var cabUnionJson = JsonDocument.Parse(await cabUnionResponse.Content.ReadAsStringAsync());
        var cabUnionId = cabUnionJson.RootElement.GetProperty("unionId").GetString()!;
        using var legacyAssignment = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/drivers/driver-legacy-auto/union-assignment")
        {
            Content = JsonContent.Create(new { unionId = cabUnionId, reason = "Cab-only scope check" })
        };
        legacyAssignment.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.Conflict, (await ops.SendAsync(legacyAssignment)).StatusCode);
    }

    [Fact]
    public async Task UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var driver = factory.CreateClient("Driver", "union-review-driver");
        using var rider = factory.CreateClient("Rider", "union-review-rider");
        using var ops = factory.CreateClient("Rider", "union-review-ops");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");

        const string unionId = "union-review-union";
        const string driverId = "union-review-driver";
        SeedBookableDriver(factory, driverId, unionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Pending",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = null,
            ProfilePhotoReviewReason = null
        };

        using var manager = factory.CreateUnionManagerClient(unionId, "union-manager-review");
        using var otherManager = factory.CreateUnionManagerClient("another-union", "other-union-manager");
        using var managerWithoutUnion = factory.CreateClient("UnionManager", "manager-without-union");

        var members = await manager.GetFromJsonAsync<List<JsonElement>>("/api/v1/union/drivers");
        var member = Assert.Single(members!);
        Assert.Equal(driverId, member.GetProperty("driverId").GetString());
        Assert.Equal(unionId, member.GetProperty("unionId").GetString());

        var driverDetails = member.GetProperty("driver");
        Assert.Equal("Pending", driverDetails.GetProperty("profilePhotoApprovalStatus").GetString());
        Assert.Equal("/9j/2Q==", driverDetails.GetProperty("profilePhotoBase64").GetString());
        Assert.Equal("cab", driverDetails.GetProperty("serviceClass").GetString());
        Assert.False(driverDetails.TryGetProperty("phoneNumber", out _));
        Assert.False(driverDetails.TryGetProperty("email", out _));
        Assert.False(driverDetails.TryGetProperty("zoneId", out _));
        Assert.False(driverDetails.TryGetProperty("currentLatitude", out _));
        Assert.False(driverDetails.TryGetProperty("currentLongitude", out _));
        Assert.False(driverDetails.TryGetProperty("storageReference", out _));
        Assert.False(driverDetails.TryGetProperty("riderId", out _));
        Assert.False(driverDetails.TryGetProperty("chatMessages", out _));
        Assert.All(driverDetails.GetProperty("documentStatuses").EnumerateArray(),
            document => Assert.False(document.TryGetProperty("storageReference", out _)));

        var approvedDocuments = profiles[driverId].Documents;
        profiles[driverId] = profiles[driverId] with
        {
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", "test/licence", "Pending"),
                new DriverDocumentReference("VehicleRegistration", "test/vehicle-registration", "Approved")
            ]
        };
        using var pendingDocumentsReview = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review", new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, pendingDocumentsReview.StatusCode);
        profiles[driverId] = profiles[driverId] with { Documents = approvedDocuments };

        Assert.Empty(await otherManager.GetFromJsonAsync<List<JsonElement>>("/api/v1/union/drivers") ?? []);
        Assert.Equal(HttpStatusCode.Forbidden, (await managerWithoutUnion.GetAsync("/api/v1/union/drivers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rider.GetAsync("/api/v1/union/drivers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync("/api/v1/union/drivers")).StatusCode);
        using var wrongUnionReview = await otherManager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review", new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, wrongUnionReview.StatusCode);

        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.False(await reservations.HasActiveAsync(driverId));
        using var noClaimReview = await managerWithoutUnion.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review", new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, noClaimReview.StatusCode);
        Assert.False(await reservations.HasActiveAsync(driverId));

        using var operationsApproval = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
            new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, operationsApproval.StatusCode);

        Assert.True(await reservations.TryAcquireTemporaryAsync(
            driverId, "union-review-existing-reservation", DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None));
        using var blockedReview = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review", new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, blockedReview.StatusCode);
        Assert.True(await reservations.TryReleaseAsync(driverId, "union-review-existing-reservation", CancellationToken.None));

        using var discoveryBeforeApproval = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        Assert.Equal(HttpStatusCode.OK, discoveryBeforeApproval.StatusCode);
        using var beforeJson = JsonDocument.Parse(await discoveryBeforeApproval.Content.ReadAsStringAsync());
        var beforeDriver = beforeJson.RootElement.GetProperty("unions").EnumerateArray()
            .Single(group => group.GetProperty("unionId").GetString() == unionId)
            .GetProperty("drivers").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, beforeDriver.GetProperty("profilePhotoBase64").ValueKind);

        using var approval = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review", new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        using var repeatedApproval = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review", new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, repeatedApproval.StatusCode);
        Assert.Equal("Approved", profiles[driverId].ProfilePhotoApprovalStatus);
        Assert.Equal(unionId, profiles[driverId].ProfilePhotoApprovedByUnionId);

        using var discoveryAfterApproval = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        using var afterJson = JsonDocument.Parse(await discoveryAfterApproval.Content.ReadAsStringAsync());
        var afterDriver = afterJson.RootElement.GetProperty("unions").EnumerateArray()
            .Single(group => group.GetProperty("unionId").GetString() == unionId)
            .GetProperty("drivers").EnumerateArray().Single();
        Assert.Equal("/9j/2Q==", afterDriver.GetProperty("profilePhotoBase64").GetString());

        var ownMembership = await driver.GetFromJsonAsync<JsonElement>("/api/v1/driver/union");
        Assert.Equal(unionId, ownMembership.GetProperty("unionId").GetString());

        using var auditResponse = await ops.GetAsync($"/api/v1/ops/drivers/{driverId}/union-history");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        using var auditJson = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync());
        Assert.Single(auditJson.RootElement.EnumerateArray(),
            item => item.GetProperty("eventType").GetString() == "SelfieApprovedByUnion");

        using var operationsRejection = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = "Vehicle image requires a clearer review photo." });
        Assert.Equal(HttpStatusCode.OK, operationsRejection.StatusCode);
        Assert.Equal("Rejected", profiles[driverId].ProfilePhotoApprovalStatus);
        using var discoveryAfterRejection = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        using var rejectedJson = JsonDocument.Parse(await discoveryAfterRejection.Content.ReadAsStringAsync());
        var rejectedDriver = rejectedJson.RootElement.GetProperty("unions").EnumerateArray()
            .Single(group => group.GetProperty("unionId").GetString() == unionId)
            .GetProperty("drivers").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, rejectedDriver.GetProperty("profilePhotoBase64").ValueKind);

        using var rejectedAuditResponse = await ops.GetAsync($"/api/v1/ops/drivers/{driverId}/union-history");
        using var rejectedAuditJson = JsonDocument.Parse(await rejectedAuditResponse.Content.ReadAsStringAsync());
        Assert.Contains(rejectedAuditJson.RootElement.EnumerateArray(), item =>
            item.GetProperty("eventType").GetString() == "SelfieRejectedByOperations");
    }

    [Fact]
    public async Task UnionTransferResetsSelfieApprovalUntilTheNewUnionApprovesIt()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var driver = factory.CreateClient("Driver", "transfer-selfie-driver");
        using var rider = factory.CreateClient("Rider", "transfer-selfie-rider");
        using var ops = factory.CreateClient("Rider", "transfer-selfie-ops");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");

        const string driverId = "transfer-selfie-driver";
        const string oldUnionId = "transfer-selfie-old-union";
        const string newUnionId = "transfer-selfie-new-union";
        SeedBookableDriver(factory, driverId, oldUnionId);

        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[newUnionId] =
            new DriverUnion(newUnionId, "Transfer Selfie New Union", "TRANSFER-SELFIE-NEW",
                "Hyderabad", null, null, ["hyderabad-central"], ["cab"], "Active", now, now);

        using var oldManager = factory.CreateUnionManagerClient(oldUnionId, "old-union-manager");
        using var newManager = factory.CreateUnionManagerClient(newUnionId, "new-union-manager");

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var approvedDocuments = profiles[driverId].Documents;
        profiles[driverId] = profiles[driverId] with
        {
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", "test/licence", "Pending"),
                new DriverDocumentReference("VehicleRegistration", "test/vehicle-registration", "Approved")
            ]
        };
        using var unverifiedTransfer = await ops.PostAsJsonAsync(
            $"/api/v1/ops/drivers/{driverId}/union-assignment",
            new { unionId = newUnionId, reason = "Driver moved to the new Cab union." });
        Assert.Equal(HttpStatusCode.Conflict, unverifiedTransfer.StatusCode);
        profiles[driverId] = profiles[driverId] with { Documents = approvedDocuments };

        using var transfer = await ops.PostAsJsonAsync(
            $"/api/v1/ops/drivers/{driverId}/union-assignment",
            new { unionId = newUnionId, reason = "Driver moved to the new Cab union." });
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

        var transferredProfile = profiles[driverId];
        Assert.Equal("Pending", transferredProfile.ProfilePhotoApprovalStatus);
        Assert.Null(transferredProfile.ProfilePhotoApprovedByUnionId);
        Assert.Null(transferredProfile.ProfilePhotoReviewedAtUtc);
        Assert.Null(transferredProfile.ProfilePhotoReviewReason);

        Assert.Empty(await oldManager.GetFromJsonAsync<List<JsonElement>>("/api/v1/union/drivers") ?? []);
        using var oldUnionApproval = await oldManager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review", new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, oldUnionApproval.StatusCode);

        var newMembers = await newManager.GetFromJsonAsync<List<JsonElement>>("/api/v1/union/drivers");
        Assert.Single(newMembers!);
        Assert.Equal("Pending", newMembers![0].GetProperty("driver").GetProperty("profilePhotoApprovalStatus").GetString());

        using var discoveryBeforeApproval = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        using var beforeJson = JsonDocument.Parse(await discoveryBeforeApproval.Content.ReadAsStringAsync());
        var beforeDriver = beforeJson.RootElement.GetProperty("unions").EnumerateArray()
            .Single(group => group.GetProperty("unionId").GetString() == newUnionId)
            .GetProperty("drivers").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, beforeDriver.GetProperty("profilePhotoBase64").ValueKind);

        using var newUnionApproval = await newManager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review", new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, newUnionApproval.StatusCode);

        using var discoveryAfterApproval = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        using var afterJson = JsonDocument.Parse(await discoveryAfterApproval.Content.ReadAsStringAsync());
        var afterDriver = afterJson.RootElement.GetProperty("unions").EnumerateArray()
            .Single(group => group.GetProperty("unionId").GetString() == newUnionId)
            .GetProperty("drivers").EnumerateArray().Single();
        Assert.Equal("/9j/2Q==", afterDriver.GetProperty("profilePhotoBase64").GetString());

        var ownMembership = await driver.GetFromJsonAsync<JsonElement>("/api/v1/driver/union");
        Assert.Equal(newUnionId, ownMembership.GetProperty("unionId").GetString());

        using var auditResponse = await ops.GetAsync($"/api/v1/ops/drivers/{driverId}/union-history");
        using var auditJson = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync());
        Assert.Contains(auditJson.RootElement.EnumerateArray(), item =>
            item.GetProperty("eventType").GetString() == "Transferred");
        Assert.Contains(auditJson.RootElement.EnumerateArray(), item =>
            item.GetProperty("eventType").GetString() == "SelfieApprovedByUnion");
    }

    [Fact]
    public async Task LegacyNonCabDriverCannotEnterNewSelfieQueueLocationOrOfferWorkflows()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-legacy-guard-test");
        using var driver = factory.CreateClient("Driver", "driver-legacy-auto");
        using var ops = factory.CreateClient("Rider", "ops-legacy-guard-test");

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var existing = profiles["driver-legacy-auto"];
        profiles["driver-legacy-auto"] = existing with
        {
            ServiceClass = "auto",
            Status = "Approved",
            AvailabilityStatus = "Online",
            CurrentLatitude = 17.493,
            CurrentLongitude = 78.399,
            LocationUpdatedAtUtc = DateTimeOffset.UtcNow,
            ProfilePhotoBase64 = "/9j/",
            ProfilePhotoApprovalStatus = "Pending"
        };

        var now = DateTimeOffset.UtcNow;
        var union = new DriverUnion(
            "legacy-guard-union", "Legacy Guard Union", "LEGACY-GUARD", "Hyderabad",
            "Union Manager", null, ["hyderabad-central"], ["cab"], "Active", now, now);
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[union.UnionId] = union;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>()
            ["driver-legacy-auto"] = new DriverUnionAssignment(
                "driver-legacy-auto", union.UnionId, union.Name, "Active", now, now, "Legacy guard test");

        var pendingOffer = new RideOfferState(
            "legacy-cab-offer", "hyderabad-central", "cab", "legacy-cab-offer-1",
            "driver-legacy-auto", now, now.AddMinutes(1), RideOfferStatuses.Pending,
            [new RideOfferAttempt("legacy-cab-offer-1", "driver-legacy-auto", now, now.AddMinutes(1), RideOfferStatuses.Pending)]);
        Assert.True(await factory.Services.GetRequiredService<IRideOfferStore>().TryCreateAsync(pendingOffer));

        using var selfie = await driver.PutAsJsonAsync(
            "/api/v1/driver/onboarding/selfie", new { profilePhotoBase64 = "/9j/" });
        Assert.Equal(HttpStatusCode.Conflict, selfie.StatusCode);

        using var approval = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/ops/drivers/driver-legacy-auto/review")
        {
            Content = JsonContent.Create(new { status = "Approved", reason = (string?)null })
        };
        approval.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.Conflict, (await ops.SendAsync(approval)).StatusCode);

        using var selfieApproval = new HttpRequestMessage(
            HttpMethod.Put, $"/api/v1/ops/unions/{union.UnionId}/drivers/driver-legacy-auto/selfie-review")
        {
            Content = JsonContent.Create(new { status = "Approved", reason = (string?)null })
        };
        selfieApproval.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        Assert.Equal(HttpStatusCode.Conflict, (await ops.SendAsync(selfieApproval)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await driver.GetAsync("/api/v1/zones/hyderabad-central/queues/auto")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await driver.GetAsync("/api/v1/zones/hyderabad-central/queues/auto/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await driver.PostAsJsonAsync("/api/v1/driver/location",
                new { latitude = 17.494, longitude = 78.400 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await driver.GetAsync("/api/v1/driver/rides/available")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await driver.GetAsync("/api/v1/driver/offers/pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await driver.PostAsync("/api/v1/queues/hyderabad-central/auto/offer-timeout", null)).StatusCode);

        var ride = await CreateRideAsync(rider, "legacy-auto-reject-guard");
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[ride.RideId] = rides[ride.RideId] with { ServiceClass = "auto" };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await driver.PostAsync($"/api/v1/rides/{ride.RideId}/reject", null)).StatusCode);

        var queue = factory.Services.GetRequiredService<IQueueStore>();
        await queue.AddAsync(new DriverQueueEntry(
            "driver-legacy-auto", "hyderabad-central", "auto", now, 0, true), CancellationToken.None);
        using var offline = await driver.PostAsJsonAsync(
            "/api/v1/driver/availability", new { isOnline = false });
        Assert.Equal(HttpStatusCode.OK, offline.StatusCode);
        Assert.Equal("auto", profiles["driver-legacy-auto"].ServiceClass);
        Assert.Equal("Offline", profiles["driver-legacy-auto"].AvailabilityStatus);
        Assert.Empty(await queue.GetEligibleAsync("hyderabad-central", "auto", CancellationToken.None));
    }

    [Fact]
    public async Task DriverOnboardingRejectsNonCabWithoutCreatingOrOverwritingProfile()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-noncab-onboarding");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles.TryRemove("driver-noncab-onboarding", out _);

        var response = await driver.PostAsJsonAsync("/api/v1/driver/onboarding", ValidOnboardingRequest("auto"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var profile = await driver.GetAsync("/api/v1/driver/onboarding/me");
        Assert.Equal(HttpStatusCode.NoContent, profile.StatusCode);
        Assert.False(profiles.ContainsKey("driver-noncab-onboarding"));
    }

    [Fact]
    public async Task LegacyNonCabRideHistoryRemainsReadableForRiderAndDriver()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-legacy-history");
        using var driver = factory.CreateClient("Driver", "driver-legacy-history");

        var ride = await CreateRideAsync(rider, "legacy-noncab-history");
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[ride.RideId] = rides[ride.RideId] with
        {
            DriverId = "driver-legacy-history",
            Status = "Completed",
            ServiceClass = "auto"
        };

        var riderHistory = await rider.GetFromJsonAsync<List<JsonElement>>("/api/v1/rides/mine");
        Assert.NotNull(riderHistory);
        Assert.Single(riderHistory);
        Assert.Equal("auto", riderHistory[0].GetProperty("serviceClass").GetString());

        var driverHistory = await driver.GetFromJsonAsync<List<JsonElement>>("/api/v1/driver/rides/mine");
        Assert.NotNull(driverHistory);
        Assert.Single(driverHistory);
        Assert.Equal("auto", driverHistory[0].GetProperty("serviceClass").GetString());
    }

    private static object ValidOnboardingRequest(string serviceClass = "cab") => new
    {
        profilePhotoBase64 = "/9j/2Q==",
        zoneId = "hyderabad-central",
        fullName = "Test Driver",
        phoneNumber = "+919876543210",
        vehicleMake = "Maruti",
        vehicleModel = "Dzire",
        vehicleRegistration = "TS09AB1234",
        serviceClass,
        passengerCapacity = 3,
        documents = new[]
        {
            new { documentType = "DrivingLicence", storageReference = "drivers/test/licence" },
            new { documentType = "VehicleRegistration", storageReference = "drivers/test/rc" }
        }
    };




    [Fact]
    public async Task DriverCanDeclineRideWithoutCancellingItAndRejectionIsCountedOnce()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-decline-test");
        using var driver = factory.CreateClient("Driver", "driver-decline-test");
        await EnqueueLegacyDriverAsync(factory, "driver-decline-test");
        var ride = await CreateRideAsync(rider, "request-driver-decline-001");

        Assert.Equal(HttpStatusCode.NoContent,
            (await driver.PostAsync($"/api/v1/rides/{ride.RideId}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await driver.PostAsync($"/api/v1/rides/{ride.RideId}/reject", null)).StatusCode);

        var stillAvailable = await rider.GetFromJsonAsync<TestRide>($"/api/v1/rides/{ride.RideId}");
        Assert.NotNull(stillAvailable);
        Assert.Equal("Requested", stillAvailable.Status);

        var ledger = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverWorkActivity>>();
        Assert.Single(ledger.Values.Where(item => item.DriverId == "driver-decline-test" && item.ActivityType == "Rejected"));
    }


    [Fact]
    public async Task RejectedOrTimedOutFrontDriverMovesBehindOtherDrivers()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-fifo-rotation");
        using var firstDriver = factory.CreateClient("Driver", "driver-fifo-first");
        using var secondDriver = factory.CreateClient("Driver", "driver-fifo-second");
        var queue = factory.Services.GetRequiredService<IQueueStore>();
        await EnqueueLegacyDriverAsync(factory, "driver-fifo-first");
        await EnqueueLegacyDriverAsync(factory, "driver-fifo-second");

        var ride = await CreateRideAsync(rider, "request-fifo-reject-001");
        Assert.Equal(HttpStatusCode.NoContent,
            (await firstDriver.PostAsync($"/api/v1/rides/{ride.RideId}/reject", null)).StatusCode);
        var afterReject = await queue.GetEligibleAsync("hyderabad-central", "cab", CancellationToken.None);
        Assert.Equal(new[] { "driver-fifo-second", "driver-fifo-first" }, afterReject.Select(entry => entry.DriverId));

        using var timeout = await firstDriver.PostAsync("/api/v1/queues/hyderabad-central/cab/offer-timeout", null);
        Assert.Equal(HttpStatusCode.Conflict, timeout.StatusCode);

        using var secondTimeout = await secondDriver.PostAsync("/api/v1/queues/hyderabad-central/cab/offer-timeout", null);
        Assert.Equal(HttpStatusCode.OK, secondTimeout.StatusCode);
        var afterTimeout = await queue.GetEligibleAsync("hyderabad-central", "cab", CancellationToken.None);
        Assert.Equal(new[] { "driver-fifo-first", "driver-fifo-second" }, afterTimeout.Select(entry => entry.DriverId));
    }

    [Fact]
    public async Task DriverCanDownloadPersistedMonthlyPayslipAsPdfWithMetrics()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-payslip-pdf");
        using var otherDriver = factory.CreateClient("Driver", "driver-payslip-other");
        var union = new DriverUnion(
            "union-payslip", "Central Drivers Union", "HYD-CENTRAL", "Hyderabad",
            "Union Manager", null, ["hyderabad-central"], ["cab"], "Active",
            DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow);
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[union.UnionId] = union;

        var assignmentEvent = new DriverUnionAuditEvent(
            "assignment-payslip", "driver-payslip-pdf", null, null,
            union.UnionId, union.Name, "Assigned", "Initial assignment", "Operations",
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>()
            ["driver-payslip-pdf|assignment-payslip"] = assignmentEvent;

        var ledger = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverWorkActivity>>();
        var activities = new[]
        {
            new DriverWorkActivity("ride-1:Accepted", "driver-payslip-pdf", "ride-1", "Accepted", new DateTimeOffset(2026, 10, 3, 4, 0, 0, TimeSpan.Zero)),
            new DriverWorkActivity("ride-1:Completed", "driver-payslip-pdf", "ride-1", "Completed", new DateTimeOffset(2026, 10, 3, 5, 0, 0, TimeSpan.Zero)),
            new DriverWorkActivity("ride-2:Accepted", "driver-payslip-pdf", "ride-2", "Accepted", new DateTimeOffset(2026, 10, 4, 4, 0, 0, TimeSpan.Zero)),
            new DriverWorkActivity("ride-2:Completed", "driver-payslip-pdf", "ride-2", "Completed", new DateTimeOffset(2026, 10, 4, 5, 0, 0, TimeSpan.Zero)),
            new DriverWorkActivity("ride-3:Rejected", "driver-payslip-pdf", "ride-3", "Rejected", new DateTimeOffset(2026, 10, 5, 4, 0, 0, TimeSpan.Zero))
        };
        foreach (var activity in activities)
            ledger[$"driver-payslip-pdf|{activity.ActivityId}"] = activity;

        using var response = await driver.GetAsync("/api/v1/driver/payslips/2026/10.pdf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("TurnOva-Payslip-2026-10.pdf", response.Content.Headers.ContentDisposition?.FileName ?? "");

        var pdf = Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("Total days worked: 2", pdf);
        Assert.Contains("Rides accepted: 2", pdf);
        Assert.Contains("Rides completed: 2", pdf);
        Assert.Contains("Rides rejected: 1", pdf);
        Assert.DoesNotContain("ride-1", pdf);
        Assert.DoesNotContain("ride-2", pdf);
        Assert.DoesNotContain("Customer:", pdf);
        Assert.DoesNotContain("Fare:", pdf);
        Assert.DoesNotContain("₹", pdf);
        Assert.DoesNotContain("INR", pdf);
        Assert.DoesNotContain("Net payable:", pdf);

        ledger["driver-payslip-pdf|ride-4:Completed"] = new DriverWorkActivity(
            "ride-4:Completed", "driver-payslip-pdf", "ride-4", "Completed",
            new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero));
        using var repeated = await driver.GetAsync("/api/v1/driver/payslips/2026/10.pdf");
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(pdf, Encoding.ASCII.GetString(await repeated.Content.ReadAsByteArrayAsync()));

        using var otherDriverResponse = await otherDriver.GetAsync(
            "/api/v1/driver/payslips/2026/10.pdf?driverId=driver-payslip-pdf");
        Assert.Equal(HttpStatusCode.Conflict, otherDriverResponse.StatusCode);
        Assert.NotEqual("application/pdf", otherDriverResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PayslipRejectsMissingUnionAssignmentAndUnionTransferWithinMonth()
    {
        using var factory = new TurnOvaApiFactory();
        using var noUnionDriver = factory.CreateClient("Driver", "driver-payslip-no-union");
        using var transferDriver = factory.CreateClient("Driver", "driver-payslip-transfer");

        using var noAssignment = await noUnionDriver.GetAsync("/api/v1/driver/payslips/2026/10.pdf");
        Assert.Equal(HttpStatusCode.Conflict, noAssignment.StatusCode);

        var oldUnion = new DriverUnion(
            "union-payslip-old", "Old Drivers Union", "HYD-OLD", "Hyderabad",
            "Old Manager", null, ["hyderabad-central"], ["cab"], "Active",
            DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow);
        var newUnion = new DriverUnion(
            "union-payslip-new", "New Drivers Union", "HYD-NEW", "Hyderabad",
            "New Manager", null, ["hyderabad-central"], ["cab"], "Active",
            DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow);
        var unions = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>();
        unions[oldUnion.UnionId] = oldUnion;
        unions[newUnion.UnionId] = newUnion;

        var audit = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
        audit["driver-payslip-transfer|assignment-before-period"] = new DriverUnionAuditEvent(
            "assignment-before-period", "driver-payslip-transfer", null, null,
            oldUnion.UnionId, oldUnion.Name, "Assigned", "Initial assignment", "Operations",
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        audit["driver-payslip-transfer|transfer-during-period"] = new DriverUnionAuditEvent(
            "transfer-during-period", "driver-payslip-transfer", oldUnion.UnionId, oldUnion.Name,
            newUnion.UnionId, newUnion.Name, "Transferred", "Union transfer", "Operations",
            new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));

        using var transfer = await transferDriver.GetAsync("/api/v1/driver/payslips/2026/10.pdf");
        Assert.Equal(HttpStatusCode.Conflict, transfer.StatusCode);
        Assert.NotEqual("application/pdf", transfer.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void MonthlyPayslipIsRenderedAsAValidPdfDocument()
    {
        var payslip = new DriverMonthlyPayslip(
            "driver-pdf-2026-10", "driver-pdf", "PDF Test Driver",
            "union-pdf", "Central Drivers Union", 2026, 10,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(5.5)),
            new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.FromHours(5.5)),
            12, 18, 15, 2, DateTimeOffset.UtcNow);

        var pdf = DriverPayslipPdf.Generate(payslip);
        var text = Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("MONTHLY DRIVER PAYSLIP", text);
        Assert.Contains("Total days worked: 12", text);
        Assert.Contains("Rides accepted: 18", text);
        Assert.Contains("Rides completed: 15", text);
        Assert.Contains("Rides rejected: 2", text);
        Assert.EndsWith("%%EOF", text);
    }

    [Fact]
    public async Task PayslipEndpointIsDriverOnlyAndDoesNotReturnHtml()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-payslip-endpoint");
        using var rider = factory.CreateClient("Rider", "rider-payslip-endpoint");

        var currentMonth = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330));
        var currentResponse = await driver.GetAsync($"/api/v1/driver/payslips/{currentMonth.Year}/{currentMonth.Month}.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, currentResponse.StatusCode);

        var futureMonth = currentMonth.AddMonths(2);
        var driverResponse = await driver.GetAsync($"/api/v1/driver/payslips/{futureMonth.Year}/{futureMonth.Month}.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, driverResponse.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await driver.GetAsync("/api/v1/driver/payslips/2026/13.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await driver.GetAsync("/api/v1/driver/payslips/2019/10.pdf")).StatusCode);

        var riderResponse = await rider.GetAsync($"/api/v1/driver/payslips/{futureMonth.Year}/{futureMonth.Month}.pdf");
        Assert.Equal(HttpStatusCode.Forbidden, riderResponse.StatusCode);
    }

    [Fact]
    public async Task RiderCanCreateRideAndRepeatRequestWithoutCreatingDuplicate()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-idempotency");
        using var firstRequest = CreateRideRequest("request-idempotency-001");

        var firstResponse = await rider.SendAsync(firstRequest);
        var firstRide = await ReadRideAsync(firstResponse);

        using var retryRequest = CreateRideRequest("request-idempotency-001");
        var retryResponse = await rider.SendAsync(retryRequest);
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
        await EnqueueLegacyDriverAsync(factory, "driver-lifecycle");
        var ride = await CreateRideAsync(rider, "request-lifecycle-001");

        var invalidStart = await driver.PostAsync($"/api/v1/rides/{ride.RideId}/start", null);
        Assert.Equal(HttpStatusCode.NotFound, invalidStart.StatusCode);

        var accepted = await driver.PostAsync($"/api/v1/rides/{ride.RideId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True((await ReadRideAsync(accepted)).LocationSharingActive);

        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{ride.RideId}/arrive", null)).StatusCode);
        await ConfirmRideAgreementAsync(factory, rider, driver, ride.RideId, "driver-lifecycle");
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
        await EnqueueLegacyDriverAsync(factory, "driver-assigned");
        SetLegacyDriverOnline(factory, "driver-other");
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
        await EnqueueLegacyDriverAsync(factory, "driver-race-a");
        await EnqueueLegacyDriverAsync(factory, "driver-race-b");
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
        await EnqueueLegacyDriverAsync(factory, "driver-cancel-before");
        var cancelBefore = await CreateRideAsync(rider, "request-cancel-before-001");
        await driver.PostAsync($"/api/v1/rides/{cancelBefore.RideId}/accept", null);

        var cancelled = await rider.PostAsync($"/api/v1/rides/{cancelBefore.RideId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await ReadRideAsync(cancelled)).Status);

        var secondRide = await CreateRideAsync(rider, "request-cancel-after-001");
        await driver.PostAsync($"/api/v1/rides/{secondRide.RideId}/accept", null);
        await driver.PostAsync($"/api/v1/rides/{secondRide.RideId}/arrive", null);
        await ConfirmRideAgreementAsync(factory, rider, driver, secondRide.RideId, "driver-cancel-before");
        await driver.PostAsync($"/api/v1/rides/{secondRide.RideId}/start", null);

        var lateCancellation = await rider.PostAsync($"/api/v1/rides/{secondRide.RideId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, lateCancellation.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    public async Task RideRequestRejectsPassengerCountsOutsideSupportedRange(int passengers)
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", $"rider-passengers-{passengers}");
        using var request = CreateRideRequest($"invalid-passengers-{passengers}", passengers: passengers);

        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("", "HITEC City, Hyderabad")]
    [InlineData(null, "HITEC City, Hyderabad")]
    [InlineData("KPHB, Hyderabad", " ")]
    [InlineData("KPHB, Hyderabad", null)]
    public async Task RideRequestRejectsMissingPickupOrDropoff(string? pickup, string? dropoff)
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-missing-location");
        using var request = CreateRideRequest("missing-location", pickup: pickup, dropoff: dropoff);

        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RideRequestRejectsMissingPickupCoordinates()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-missing-coordinates");
        using var request = CreateRideRequest("missing-coordinates", pickupLatitude: null);

        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(91d, 78.399d)]
    [InlineData(-91d, 78.399d)]
    [InlineData(17.493d, 181d)]
    public async Task RideRequestRejectsCoordinatesOutsideLatitudeLongitudeRanges(double latitude, double longitude)
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-invalid-coordinates");
        using var request = CreateRideRequest("invalid-coordinates", pickupLatitude: latitude, pickupLongitude: longitude);

        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RideRequestRejectsUnsupportedServiceClass()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-invalid-service-class");
        using var request = CreateRideRequest("invalid-service-class", serviceClass: "taxi");

        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RideRequestRejectsPickupOutsideConfiguredZones()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-outside-zone");
        using var request = CreateRideRequest("outside-zone", pickupLatitude: 0, pickupLongitude: 0);

        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MaximumSupportedPassengerCountIsAccepted()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-max-passengers");
        using var request = CreateRideRequest("max-passengers", passengers: 6);

        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(6, (await ReadRideAsync(response)).Passengers);
    }

    [Fact]
    public async Task InvalidRideRequestDoesNotConsumeIdempotencyKey()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-validation-idempotency");
        using var invalid = CreateRideRequest("validation-retry-key", passengers: 7);
        using var valid = CreateRideRequest("validation-retry-key");

        using var rejected = await rider.SendAsync(invalid);
        using var accepted = await rider.SendAsync(valid);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task RideRequestRequiresIdempotencyKeyAndRiderRole()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-validation");
        using var driver = factory.CreateClient("Driver", "driver-validation");

        using var requestWithoutKey = CreateRideRequest(null);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await rider.SendAsync(requestWithoutKey)).StatusCode);

        using var driverRequest = CreateRideRequest("driver-must-not-book-001");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await driver.SendAsync(driverRequest)).StatusCode);
    }

    [Fact]
    public async Task RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-union-discovery");
        using var driverRole = factory.CreateClient("Driver", "driver-discovery-role");
        using var anonymous = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var unions = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>();
        var assignments = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>();
        var now = DateTimeOffset.UtcNow;

        DriverUnion AddUnion(string unionId, string name, string status = "Active", string zoneId = "hyderabad-central")
        {
            var union = new DriverUnion(unionId, name, unionId.ToUpperInvariant(), "Hyderabad",
                null, null, [zoneId], ["cab"], status, now, now);
            unions[unionId] = union;
            return union;
        }

        void AddDriver(
            string driverId,
            string? unionId,
            double latitude,
            double longitude,
            int capacity = 4,
            string serviceClass = "cab",
            string status = "Approved",
            bool documentsApproved = true,
            DateTimeOffset? locationUpdatedAtUtc = null,
            string photoStatus = "Approved",
            string? photoApprovedByUnionId = null)
        {
            using var seededClient = factory.CreateClient("Driver", driverId);
            var current = profiles[driverId];
            profiles[driverId] = current with
            {
                FullName = driverId,
                ServiceClass = serviceClass,
                PassengerCapacity = capacity,
                Status = status,
                Documents =
                [
                    new DriverDocumentReference("DrivingLicence", $"test/{driverId}/licence", documentsApproved ? "Approved" : "Pending"),
                    new DriverDocumentReference("VehicleRegistration", $"test/{driverId}/registration", documentsApproved ? "Approved" : "Pending")
                ],
                AvailabilityStatus = "Online",
                CurrentLatitude = latitude,
                CurrentLongitude = longitude,
                LocationUpdatedAtUtc = locationUpdatedAtUtc ?? now,
                ProfilePhotoBase64 = photoStatus == "None" ? null : "/9j/",
                ProfilePhotoContentType = photoStatus == "None" ? null : "image/jpeg",
                ProfilePhotoApprovalStatus = photoStatus == "None" ? "Pending" : photoStatus,
                ProfilePhotoApprovedByUnionId = photoStatus == "Approved" ? photoApprovedByUnionId ?? unionId : null
            };

            if (unionId is not null)
            {
                var union = unions[unionId];
                assignments[driverId] = new DriverUnionAssignment(
                    driverId, unionId, union.Name, "Active", now, now, "Discovery integration fixture");
            }
        }

        var central = AddUnion("union-central", "Central Cab Union");
        var metro = AddUnion("union-metro", "Metro Cab Union");
        var inactive = AddUnion("union-inactive", "Inactive Cab Union", "Inactive");
        var outer = AddUnion("union-outer", "Outer Cab Union");
        var northZone = AddUnion("union-north", "North Cab Union", zoneId: "hyderabad-north");

        AddDriver("central-1", central.UnionId, 17.493, 78.400);
        AddDriver("central-2", central.UnionId, 17.493, 78.400);
        AddDriver("central-3", central.UnionId, 17.495, 78.404);
        AddDriver("central-4", central.UnionId, 17.496, 78.406);
        AddDriver("metro-photo-approved", metro.UnionId, 17.493, 78.401);
        AddDriver("metro-photo-pending", metro.UnionId, 17.493, 78.402, photoStatus: "Pending");
        AddDriver("metro-photo-approved-by-old-union", metro.UnionId, 17.493, 78.403,
            photoStatus: "Approved", photoApprovedByUnionId: "previous-union");
        AddDriver("inactive-union-driver", inactive.UnionId, 17.493, 78.399);
        AddDriver("unassigned-driver", null, 17.493, 78.399);
        AddDriver("stale-driver", central.UnionId, 17.493, 78.399,
            locationUpdatedAtUtc: now.AddMinutes(-5));
        AddDriver("outside-radius-driver", outer.UnionId, 17.590, 78.399);
        AddDriver("north-zone-union-driver", northZone.UnionId, 17.493, 78.399);
        AddDriver("too-small-driver", central.UnionId, 17.493, 78.399, capacity: 2);
        AddDriver("unverified-documents-driver", central.UnionId, 17.493, 78.399, documentsApproved: false);
        AddDriver("not-approved-driver", central.UnionId, 17.493, 78.399, status: "Pending");
        AddDriver("wrong-class-driver", central.UnionId, 17.493, 78.399, serviceClass: "bike");

        using var response = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driverRole.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1")).StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("hyderabad-central", root.GetProperty("pickupZoneId").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("journeyDistanceKm").ValueKind);

        var groups = root.GetProperty("unions");
        Assert.Equal(2, groups.GetArrayLength());
        Assert.DoesNotContain(groups.EnumerateArray(), group =>
            group.GetProperty("unionId").GetString() == northZone.UnionId);
        Assert.DoesNotContain(groups.EnumerateArray()
            .SelectMany(group => group.GetProperty("drivers").EnumerateArray()), driver =>
            driver.GetProperty("driverId").GetString() == "north-zone-union-driver");
        var centralGroup = groups.EnumerateArray()
            .Single(group => group.GetProperty("unionId").GetString() == central.UnionId);
        var centralDrivers = centralGroup.GetProperty("drivers");
        Assert.Equal(3, centralDrivers.GetArrayLength());
        var centralIds = centralDrivers.EnumerateArray()
            .Select(driver => driver.GetProperty("driverId").GetString()).ToArray();
        Assert.Equal(new[] { "central-1", "central-2", "central-3" }, centralIds);
        var distances = centralDrivers.EnumerateArray()
            .Select(driver => driver.GetProperty("distanceToPickupKm").GetDouble()).ToArray();
        Assert.Equal(distances.OrderBy(distance => distance), distances);
        Assert.Equal(distances[0], distances[1]);

        var metroGroup = groups.EnumerateArray()
            .Single(group => group.GetProperty("unionId").GetString() == metro.UnionId);
        var metroDrivers = metroGroup.GetProperty("drivers").EnumerateArray().ToArray();
        Assert.Equal(3, metroDrivers.Length);
        var approvedPhoto = metroDrivers.Single(driver =>
            driver.GetProperty("driverId").GetString() == "metro-photo-approved");
        Assert.Equal("/9j/", approvedPhoto.GetProperty("profilePhotoBase64").GetString());
        var pendingPhoto = metroDrivers.Single(driver =>
            driver.GetProperty("driverId").GetString() == "metro-photo-pending");
        Assert.Equal(JsonValueKind.Null, pendingPhoto.GetProperty("profilePhotoBase64").ValueKind);
        var wrongUnionPhoto = metroDrivers.Single(driver =>
            driver.GetProperty("driverId").GetString() == "metro-photo-approved-by-old-union");
        Assert.Equal(JsonValueKind.Null, wrongUnionPhoto.GetProperty("profilePhotoBase64").ValueKind);

        Assert.False(body.Contains("phoneNumber", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("vehicleRegistration", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("currentLatitude", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("currentLongitude", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("queuePosition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RiderDiscoveryExcludesReservedDriversAndLegacyFifoRiderRoutesAreRetired()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-discovery-reservation");
        using var seededDriver = factory.CreateClient("Driver", "driver-discovery-reserved");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var unions = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>();
        var assignments = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>();
        var now = DateTimeOffset.UtcNow;
        var union = new DriverUnion("union-reservation", "Reservation Union", "RESERVATION",
            "Hyderabad", null, null, ["hyderabad-central"], ["cab"], "Active", now, now);
        unions[union.UnionId] = union;
        var profile = profiles["driver-discovery-reserved"];
        profiles[profile.DriverId] = profile with
        {
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", "test/licence", "Approved"),
                new DriverDocumentReference("VehicleRegistration", "test/registration", "Approved")
            ],
            AvailabilityStatus = "Online",
            CurrentLatitude = 17.493,
            CurrentLongitude = 78.399,
            LocationUpdatedAtUtc = now
        };
        assignments[profile.DriverId] = new DriverUnionAssignment(
            profile.DriverId, union.UnionId, union.Name, "Active", now, now, "Reservation fixture");
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.True(await reservations.TryAcquireAsync(profile.DriverId, "active-ride"));

        using var discovery = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        using var json = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("unions").EnumerateArray());

        Assert.Equal(HttpStatusCode.Gone,
            (await rider.GetAsync("/api/v1/rides/nearby?latitude=17.493&longitude=78.399&serviceClass=cab&passengers=1")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task RiderDiscoveryFailsClosedWhenNearbyRadiusIsMissingOrInvalid(double? radiusKm)
    {
        using var factory = new TurnOvaApiFactory(nearbyRadiusKm: radiusKm);
        using var rider = factory.CreateClient("Rider", "rider-discovery-radius-invalid");

        using var response = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task CustomerFacingFifoRideCreationIsDisabledOutsideTestCompatibilityMode()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "rider-fifo-disabled");

        using var request = CreateRideRequest("legacy-fifo-disabled-001");
        using var response = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DriverCannotGoOnlineOutsideAssignedZoneAndRiderCannotSearchOutsideConfiguredZones()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-online-outside-zone");
        using var rider = factory.CreateClient("Rider", "rider-nearby-outside-zone");
        SeedActiveCabUnion(factory, "driver-online-outside-zone");

        using var online = await driver.PostAsJsonAsync("/api/v1/driver/availability",
            new { isOnline = true, latitude = 0d, longitude = 0d });
        Assert.Equal(HttpStatusCode.Conflict, online.StatusCode);
        var profile = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()
            ["driver-online-outside-zone"];
        Assert.Equal("Offline", profile.AvailabilityStatus);

        var queue = await driver.GetFromJsonAsync<List<QueueEntryTest>>(
            "/api/v1/zones/hyderabad-central/queues/cab");
        Assert.NotNull(queue);
        Assert.DoesNotContain(queue, entry => entry.DriverId == "driver-online-outside-zone");

        using var discovery = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=0&longitude=0&passengers=1");
        Assert.Equal(HttpStatusCode.BadRequest, discovery.StatusCode);
    }

    [Fact]
    public async Task DriverLocationOutsideAssignedZoneTakesDriverOfflineAndRemovesQueueMembership()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-location-zone-exit");
        SeedActiveCabUnion(factory, "driver-location-zone-exit");

        using var online = await driver.PostAsJsonAsync("/api/v1/driver/availability",
            new { isOnline = true, latitude = 17.493, longitude = 78.399 });
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);

        using var location = await driver.PostAsJsonAsync("/api/v1/driver/location",
            new { latitude = 0d, longitude = 0d });
        Assert.Equal(HttpStatusCode.Conflict, location.StatusCode);

        var profile = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()
            ["driver-location-zone-exit"];
        Assert.Equal("Offline", profile.AvailabilityStatus);
        Assert.Null(profile.CurrentLatitude);
        Assert.Null(profile.CurrentLongitude);
        Assert.Null(profile.LocationUpdatedAtUtc);

        var queue = await driver.GetFromJsonAsync<List<QueueEntryTest>>(
            "/api/v1/zones/hyderabad-central/queues/cab");
        Assert.NotNull(queue);
        Assert.DoesNotContain(queue, entry => entry.DriverId == "driver-location-zone-exit");
    }

    [Fact]
    public async Task DriverAvailabilityLocationRequiresBothCoordinatesAndKeepsOnlineStateOnBadPayload()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-location-invalid-payload");
        SeedActiveCabUnion(factory, "driver-location-invalid-payload");

        using var online = await driver.PostAsJsonAsync("/api/v1/driver/availability",
            new { isOnline = true, latitude = 17.493, longitude = 78.399 });
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);

        using var invalid = await driver.PostAsJsonAsync("/api/v1/driver/location", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var profile = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()
            ["driver-location-invalid-payload"];
        Assert.Equal("Online", profile.AvailabilityStatus);
        Assert.Equal(17.493, profile.CurrentLatitude);
        Assert.Equal(78.399, profile.CurrentLongitude);
    }

    [Fact]
    public async Task DriverCannotGoOnlineWithoutValidCurrentLocation()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-online-without-location");

        using var response = await driver.PostAsJsonAsync("/api/v1/driver/availability",
            new { isOnline = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var profile = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()
            ["driver-online-without-location"];
        Assert.Equal("Offline", profile.AvailabilityStatus);
    }

    [Fact]
    public async Task RiderTripHistoryReturnsOnlyAuthenticatedRidersRidesNewestFirstAndOmitsPrivateFields()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-history-owner");
        using var otherRider = factory.CreateClient("Rider", "rider-history-other");
        using var driver = factory.CreateClient("Driver", "driver-history-access");

        var first = await CreateRideAsync(rider, "rider-history-first");
        var second = await CreateRideAsync(rider, "rider-history-second");
        var other = await CreateRideAsync(otherRider, "rider-history-other");

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[first.RideId] = rides[first.RideId] with
        {
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            DriverId = "driver-private-id",
            PickupLatitude = 17.493,
            PickupLongitude = 78.399
        };
        rides[second.RideId] = rides[second.RideId] with
        {
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            DriverId = "driver-private-id",
            PickupLatitude = 17.494,
            PickupLongitude = 78.400
        };

        using var response = await rider.GetAsync("/api/v1/rides/mine?riderId=rider-history-other");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var history = JsonSerializer.Deserialize<List<RiderRideHistoryItemTest>>(
            body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(history);
        Assert.Equal(2, history.Count);
        Assert.Equal(second.RideId, history[0].RideId);
        Assert.Equal(first.RideId, history[1].RideId);
        Assert.DoesNotContain(history, item => item.RideId == other.RideId);

        Assert.False(body.Contains("riderId", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("driverId", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("pickupLatitude", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("pickupLongitude", StringComparison.OrdinalIgnoreCase));

        var otherHistory = await otherRider.GetFromJsonAsync<List<RiderRideHistoryItemTest>>("/api/v1/rides/mine");
        Assert.NotNull(otherHistory);
        Assert.Single(otherHistory);
        Assert.Equal(other.RideId, otherHistory[0].RideId);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync("/api/v1/rides/mine")).StatusCode);
    }

    [Fact]
    public async Task NewRideDoesNotExposeAnInventedFareBeforeFarePolicyIsApproved()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-fare-policy-placeholder");

        using var request = CreateRideRequest("rider-fare-policy-placeholder");
        using var response = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("estimatedFareInr").ValueKind);
    }

    [Fact]
    public async Task RiderWithNoTripsReceivesAnEmptyHistoryList()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-history-empty");

        var history = await rider.GetFromJsonAsync<List<RiderRideHistoryItemTest>>("/api/v1/rides/mine");
        Assert.NotNull(history);
        Assert.Empty(history);
    }

    [Fact]
    public async Task DriverTripHistoryReturnsOnlyAuthenticatedDriversRidesNewestFirstAndOmitsPrivateFields()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "driver-history-rider");
        using var otherRider = factory.CreateClient("Rider", "driver-history-other-rider");
        using var driver = factory.CreateClient("Driver", "driver-history-owner");
        using var otherDriver = factory.CreateClient("Driver", "driver-history-other");

        var first = await CreateRideAsync(rider, "driver-history-first");
        var second = await CreateRideAsync(rider, "driver-history-second");
        var other = await CreateRideAsync(otherRider, "driver-history-other");

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[first.RideId] = rides[first.RideId] with
        {
            DriverId = "driver-history-owner",
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Status = "Completed",
            EstimatedFareInr = 125m,
            PickupLatitude = 17.493,
            PickupLongitude = 78.399,
            DriverLatitude = 17.494,
            DriverLongitude = 78.400,
            LocationSharingActive = false
        };
        rides[second.RideId] = rides[second.RideId] with
        {
            DriverId = "driver-history-owner",
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AcceptedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            Status = "InProgress",
            EstimatedFareInr = 175m,
            PickupLatitude = 17.495,
            PickupLongitude = 78.401,
            DriverLatitude = 17.496,
            DriverLongitude = 78.402,
            LocationSharingActive = true
        };
        rides[other.RideId] = rides[other.RideId] with
        {
            DriverId = "driver-history-other",
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        };

        using var response = await driver.GetAsync("/api/v1/driver/rides/mine?driverId=driver-history-other");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var history = JsonSerializer.Deserialize<List<DriverRideHistoryItemTest>>(
            body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(history);
        Assert.Equal(2, history.Count);
        Assert.Equal(second.RideId, history[0].RideId);
        Assert.Equal(first.RideId, history[1].RideId);
        Assert.Equal("InProgress", history[0].Status);
        Assert.True(history[0].LocationSharingActive);
        Assert.False(history[1].LocationSharingActive);

        Assert.False(body.Contains("driverId", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("riderId", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("pickupLatitude", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("pickupLongitude", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("driverLatitude", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("driverLongitude", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("estimatedFareInr", StringComparison.OrdinalIgnoreCase));

        var otherHistory = await otherDriver.GetFromJsonAsync<List<DriverRideHistoryItemTest>>("/api/v1/driver/rides/mine");
        Assert.NotNull(otherHistory);
        Assert.Single(otherHistory);
        Assert.Equal(other.RideId, otherHistory[0].RideId);
        Assert.Equal(HttpStatusCode.Forbidden, (await rider.GetAsync("/api/v1/driver/rides/mine")).StatusCode);
    }

    [Fact]
    public async Task DriverWithNoAssignedTripsReceivesAnEmptyHistoryList()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-history-empty");

        var history = await driver.GetFromJsonAsync<List<DriverRideHistoryItemTest>>("/api/v1/driver/rides/mine");
        Assert.NotNull(history);
        Assert.Empty(history);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.CreateClient("Rider", "driver-history-empty-rider").GetAsync("/api/v1/driver/rides/mine")).StatusCode);
    }

    [Fact]
    public async Task RiderCanBookSelectedCabDriverWithStopsAndIdempotentRetry()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "selected-booking-rider");
        using var driver = factory.CreateClient("Driver", "selected-booking-driver");
        SeedBookableDriver(factory, "selected-booking-driver", "selected-booking-union");

        using var request = CreateSelectedBookingRequest("selected-booking-key", "selected-booking-driver",
            "selected-booking-union", "KPHB", "HITEC City", ["JNTU", "Madhapur"]);
        using var response = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rideId = json.RootElement.GetProperty("rideId").GetString()!;
        Assert.Equal("Negotiating", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("selected-booking-union", json.RootElement.GetProperty("unionId").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("stops").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("driverLatitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("driverLongitude").ValueKind);
        Assert.False(json.RootElement.GetProperty("locationSharingActive").GetBoolean());
        Assert.Equal("Approved Test Driver", json.RootElement.GetProperty("driverDisplayName").GetString());
        Assert.Equal("Maruti", json.RootElement.GetProperty("driverVehicleMake").GetString());
        Assert.Equal("Dzire", json.RootElement.GetProperty("driverVehicleModel").GetString());
        Assert.Equal("Selected Booking Union", json.RootElement.GetProperty("unionName").GetString());
        Assert.True(json.RootElement.GetProperty("driverDistanceToPickupKm").GetDouble() >= 0);

        using var retry = CreateSelectedBookingRequest("selected-booking-key", "selected-booking-driver",
            "selected-booking-union", "KPHB", "HITEC City", ["JNTU", "Madhapur"]);
        using var retryResponse = await rider.SendAsync(retry);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using var retryJson = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync());
        Assert.Equal(rideId, retryJson.RootElement.GetProperty("rideId").GetString());

        using var changedPayload = CreateSelectedBookingRequest("selected-booking-key", "selected-booking-driver",
            "selected-booking-union", "KPHB", "Gachibowli", ["JNTU"]);
        Assert.Equal(HttpStatusCode.Conflict, (await rider.SendAsync(changedPayload)).StatusCode);
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("selected-booking-driver"));
    }

    [Fact]
    public async Task SelectedBookingReservationPreventsConcurrentBookingsAndRiderCancelReleasesIt()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var riderA = factory.CreateClient("Rider", "selected-rider-a");
        using var riderB = factory.CreateClient("Rider", "selected-rider-b");
        using var driver = factory.CreateClient("Driver", "selected-shared-driver");
        SeedBookableDriver(factory, "selected-shared-driver", "selected-shared-union");

        using var requestA = CreateSelectedBookingRequest("selected-key-a", "selected-shared-driver",
            "selected-shared-union", "KPHB", "HITEC City", []);
        using var requestB = CreateSelectedBookingRequest("selected-key-b", "selected-shared-driver",
            "selected-shared-union", "KPHB", "Madhapur", []);
        var responses = await Task.WhenAll(riderA.SendAsync(requestA), riderB.SendAsync(requestB));
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.Conflict));
        var created = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var rideId = createdJson.RootElement.GetProperty("rideId").GetString()!;
        foreach (var response in responses) response.Dispose();

        var owner = createdJson.RootElement.GetProperty("riderId").GetString();
        var ownerClient = owner == "selected-rider-a" ? riderA : riderB;
        Assert.Equal(HttpStatusCode.OK,
            (await ownerClient.PostAsync($"/api/v1/rides/{rideId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await ownerClient.PostAsync($"/api/v1/rides/{rideId}/cancel", null)).StatusCode);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("selected-shared-driver"));
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync("/api/v1/driver/availability", JsonContent.Create(new { isOnline = false }))).StatusCode);
    }

    [Fact]
    public async Task DriverDecliningSelectedBookingCancelsRideAndReleasesReservation()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "decline-booking-rider");
        using var driver = factory.CreateClient("Driver", "decline-booking-driver");
        SeedBookableDriver(factory, "decline-booking-driver", "decline-booking-union");

        using var request = CreateSelectedBookingRequest("decline-booking-key", "decline-booking-driver",
            "decline-booking-union", "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        using var declined = await driver.PostAsync($"/api/v1/rides/{rideId}/reject", null);
        Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/reject", null)).StatusCode);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("decline-booking-driver"));
        using var rideResponse = await rider.GetAsync($"/api/v1/rides/{rideId}");
        using var rideJson = JsonDocument.Parse(await rideResponse.Content.ReadAsStringAsync());
        Assert.Equal("Cancelled", rideJson.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnionTransferIsBlockedWhileDriverHasSelectedNegotiation()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "transfer-active-rider");
        using var ops = factory.CreateClient("Rider", "transfer-active-ops");
        using var driver = factory.CreateClient("Driver", "transfer-active-driver");
        SeedBookableDriver(factory, "transfer-active-driver", "transfer-active-union");

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles["transfer-active-driver"] = profiles["transfer-active-driver"] with
        {
            ProfilePhotoBase64 = Convert.ToBase64String([1, 2, 3]),
            ProfilePhotoContentType = "image/jpeg"
        };
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()["transfer-target-union"] =
            new DriverUnion("transfer-target-union", "Transfer Target Union", "TRANSFER-TARGET", "Hyderabad",
                null, null, ["hyderabad-central"], ["cab"], "Active", now, now);

        using var bookingRequest = CreateSelectedBookingRequest("transfer-active-booking", "transfer-active-driver",
            "transfer-active-union", "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(bookingRequest);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);

        using var transfer = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/ops/drivers/transfer-active-driver/union-assignment")
        {
            Content = JsonContent.Create(new { unionId = "transfer-target-union", reason = "Approved operational transfer" })
        };
        transfer.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var transferResponse = await ops.SendAsync(transfer);
        Assert.Equal(HttpStatusCode.Conflict, transferResponse.StatusCode);

        var assignment = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>()
            ["transfer-active-driver"];
        Assert.Equal("transfer-active-union", assignment.UnionId);
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("transfer-active-driver"));
    }

    [Fact]
    public async Task UnionTransferIsBlockedWhileDriverHasAcceptedActiveRide()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "transfer-active-ride-rider");
        using var driver = factory.CreateClient("Driver", "transfer-active-ride-driver");
        using var ops = factory.CreateClient("Rider", "transfer-active-ride-ops");
        SeedBookableDriver(factory, "transfer-active-ride-driver", "transfer-active-ride-union");

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles["transfer-active-ride-driver"] = profiles["transfer-active-ride-driver"] with
        {
            ProfilePhotoBase64 = Convert.ToBase64String([1, 2, 3]),
            ProfilePhotoContentType = "image/jpeg"
        };
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()["transfer-active-ride-target-union"] =
            new DriverUnion("transfer-active-ride-target-union", "Transfer Target Union", "TRANSFER-TARGET", "Hyderabad",
                null, null, ["hyderabad-central"], ["cab"], "Active", now, now);

        using var bookingRequest = CreateSelectedBookingRequest("transfer-active-ride-booking",
            "transfer-active-ride-driver", "transfer-active-ride-union", "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(bookingRequest);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        using var proposal = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 420m, currency = "INR" })
        };
        proposal.Headers.Add("Idempotency-Key", "transfer-active-ride-fare");
        Assert.Equal(HttpStatusCode.Created, (await rider.SendAsync(proposal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
        using var accepted = await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var acceptedJson = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        Assert.Equal("Accepted", acceptedJson.RootElement.GetProperty("status").GetString());
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("transfer-active-ride-driver"));

        using var transfer = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/ops/drivers/transfer-active-ride-driver/union-assignment")
        {
            Content = JsonContent.Create(new
            {
                unionId = "transfer-active-ride-target-union",
                reason = "Approved operational transfer"
            })
        };
        transfer.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var transferResponse = await ops.SendAsync(transfer);
        Assert.Equal(HttpStatusCode.Conflict, transferResponse.StatusCode);

        var assignment = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>()
            ["transfer-active-ride-driver"];
        Assert.Equal("transfer-active-ride-union", assignment.UnionId);
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("transfer-active-ride-driver"));
    }

    [Fact]
    public async Task SelectedBookingRejectsDriverPairedWithDifferentUnion()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "wrong-union-rider");
        using var driver = factory.CreateClient("Driver", "wrong-union-driver");
        SeedBookableDriver(factory, "wrong-union-driver", "driver-home-union");
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()["other-active-union"] =
            new DriverUnion("other-active-union", "Other Active Union", "OTHER", "Hyderabad", null, null,
                ["hyderabad-central"], ["cab"], "Active", now, now);

        using var request = CreateSelectedBookingRequest("wrong-union-key", "wrong-union-driver",
            "other-active-union", "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("wrong-union-driver"));
    }

    [Fact]
    public async Task ExpiredSelectedNegotiationBecomesTerminalAndReleasesDriverReservation()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "expired-booking-rider");
        using var driver = factory.CreateClient("Driver", "expired-booking-driver");
        SeedBookableDriver(factory, "expired-booking-driver", "expired-booking-union");

        using var request = CreateSelectedBookingRequest("expired-booking-key", "expired-booking-driver",
            "expired-booking-union", "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rideId = json.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with { NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };

        using var expired = await rider.GetAsync($"/api/v1/rides/{rideId}");
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        using var expiredJson = JsonDocument.Parse(await expired.Content.ReadAsStringAsync());
        Assert.Equal("Expired", expiredJson.RootElement.GetProperty("status").GetString());
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("expired-booking-driver"));
    }

    [Fact]
    public async Task DiscoveryExpiresAnExpiredNegotiationBeforeReleasingItsDriverReservation()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "discovery-expiry-rider");
        using var driver = factory.CreateClient("Driver", "discovery-expiry-driver");
        SeedBookableDriver(factory, "discovery-expiry-driver", "discovery-expiry-union");

        using var request = CreateSelectedBookingRequest("discovery-expiry-key", "discovery-expiry-driver",
            "discovery-expiry-union", "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with { NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };

        using var discovery = await rider.GetAsync("/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        using var discoveryJson = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
        var discoveredDriverIds = discoveryJson.RootElement.GetProperty("unions").EnumerateArray()
            .SelectMany(group => group.GetProperty("drivers").EnumerateArray())
            .Select(item => item.GetProperty("driverId").GetString());
        Assert.Contains("discovery-expiry-driver", discoveredDriverIds);

        using var expiredRide = await rider.GetAsync($"/api/v1/rides/{rideId}");
        using var expiredJson = JsonDocument.Parse(await expiredRide.Content.ReadAsStringAsync());
        Assert.Equal("Expired", expiredJson.RootElement.GetProperty("status").GetString());
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("discovery-expiry-driver"));
    }

    [Fact]
    public async Task SelectedDriverCanAcceptOnlyAfterMutualAgreementAndReservationBecomesPermanent()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "selected-accept-rider");
        using var driver = factory.CreateClient("Driver", "selected-accept-driver");
        SeedBookableDriver(factory, "selected-accept-driver", "selected-accept-union");

        using var request = CreateSelectedBookingRequest("selected-accept-key", "selected-accept-driver",
            "selected-accept-union", "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        Assert.Equal(HttpStatusCode.Conflict,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null)).StatusCode);

        using var proposal = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 420m, currency = "INR" })
        };
        proposal.Headers.Add("Idempotency-Key", "selected-accept-fare");
        Assert.Equal(HttpStatusCode.Created, (await rider.SendAsync(proposal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);

        using var accepted = await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var acceptedJson = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        Assert.Equal("Accepted", acceptedJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null)).StatusCode);
        if (acceptedJson.RootElement.TryGetProperty("negotiationExpiresAtUtc", out var expiry))
            Assert.Equal(JsonValueKind.Null, expiry.ValueKind);
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("selected-accept-driver"));
    }

    [Fact]
    public async Task SelectedCabTripSharesLocationOnlyForAssignedDriverUntilCompletion()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "selected-trip-rider");
        using var driver = factory.CreateClient("Driver", "selected-trip-driver");
        using var otherDriver = factory.CreateClient("Driver", "selected-trip-other-driver");
        SeedBookableDriver(factory, "selected-trip-driver", "selected-trip-union");

        using var bookingRequest = CreateSelectedBookingRequest("selected-trip-key", "selected-trip-driver",
            "selected-trip-union", "KPHB", "HITEC City", ["JNTU"]);
        using var booking = await rider.SendAsync(bookingRequest);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        using var beforeAcceptance = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location", new { latitude = 17.494, longitude = 78.400 });
        Assert.Equal(HttpStatusCode.NotFound, beforeAcceptance.StatusCode);

        using var proposal = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 425m, currency = "INR" })
        };
        proposal.Headers.Add("Idempotency-Key", "selected-trip-fare-v1");
        Assert.Equal(HttpStatusCode.Created, (await rider.SendAsync(proposal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);

        using var accepted = await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var acceptedRide = await ReadRideAsync(accepted);
        Assert.Equal("Accepted", acceptedRide.Status);
        Assert.True(acceptedRide.LocationSharingActive);

        using var invalidCoordinates = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location", new { latitude = 91d, longitude = 78.400 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCoordinates.StatusCode);

        using var wrongDriverLocation = await otherDriver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location", new { latitude = 17.495, longitude = 78.401 });
        Assert.Equal(HttpStatusCode.NotFound, wrongDriverLocation.StatusCode);

        using var locationUpdate = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location", new { latitude = 17.495, longitude = 78.401 });
        Assert.Equal(HttpStatusCode.NoContent, locationUpdate.StatusCode);

        using var riderRideResponse = await rider.GetAsync($"/api/v1/rides/{rideId}");
        Assert.Equal(HttpStatusCode.OK, riderRideResponse.StatusCode);
        using var riderRideJson = JsonDocument.Parse(await riderRideResponse.Content.ReadAsStringAsync());
        Assert.Equal(17.495, riderRideJson.RootElement.GetProperty("driverLatitude").GetDouble(), 6);
        Assert.Equal(78.401, riderRideJson.RootElement.GetProperty("driverLongitude").GetDouble(), 6);

        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/arrive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/start", null)).StatusCode);
        using var completed = await driver.PostAsync($"/api/v1/rides/{rideId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var completedRide = await ReadRideAsync(completed);
        Assert.Equal("Completed", completedRide.Status);
        Assert.False(completedRide.LocationSharingActive);

        using var afterCompletion = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location", new { latitude = 17.496, longitude = 78.402 });
        Assert.Equal(HttpStatusCode.NotFound, afterCompletion.StatusCode);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("selected-trip-driver"));
    }

    [Fact]
    public async Task SelectedBookingRejectsMoreThanTenIntermediateStops()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "too-many-stops-rider");
        using var driver = factory.CreateClient("Driver", "too-many-stops-driver");
        SeedBookableDriver(factory, "too-many-stops-driver", "too-many-stops-union");

        var stops = Enumerable.Range(1, 11).Select(index => $"Stop {index}").ToArray();
        using var request = CreateSelectedBookingRequest("too-many-stops-key", "too-many-stops-driver",
            "too-many-stops-union", "KPHB", "HITEC City", stops);
        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("too-many-stops-driver"));
    }

    [Fact]
    public async Task SelectedBookingFailsClosedWhenNegotiationLifetimeIsNotConfigured()
    {
        using var factory = new TurnOvaApiFactory(
            legacyFifoRiderRequestsEnabled: false, selectedDriverNegotiationLifetimeMinutes: null);
        using var rider = factory.CreateClient("Rider", "missing-expiry-rider");
        using var driver = factory.CreateClient("Driver", "missing-expiry-driver");
        SeedBookableDriver(factory, "missing-expiry-driver", "missing-expiry-union");

        using var request = CreateSelectedBookingRequest("missing-expiry-key", "missing-expiry-driver",
            "missing-expiry-union", "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("missing-expiry-driver"));
    }

    [Fact]
    public async Task SelectedBookingRecoversAfterInterruptedRideWriteUsingSameIdempotencyKey()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "recovery-booking-rider");
        using var driver = factory.CreateClient("Driver", "recovery-booking-driver");
        SeedBookableDriver(factory, "recovery-booking-driver", "recovery-booking-union");

        using var request = CreateSelectedBookingRequest("recovery-booking-key", "recovery-booking-driver",
            "recovery-booking-union", "KPHB", "HITEC City", ["JNTU"]);
        using var firstResponse = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var rideId = firstJson.RootElement.GetProperty("rideId").GetString()!;

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        Assert.True(rides.TryRemove(rideId, out _));
        var idempotency = factory.Services.GetRequiredService<ConcurrentDictionary<string, RideIdempotencyRecord>>();
        var idempotencyIndex = idempotency.Keys.Single();
        idempotency[idempotencyIndex] = idempotency[idempotencyIndex] with
        {
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
        };

        using var retry = CreateSelectedBookingRequest("recovery-booking-key", "recovery-booking-driver",
            "recovery-booking-union", "KPHB", "HITEC City", ["JNTU"]);
        using var recovered = await rider.SendAsync(retry);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        using var recoveredJson = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync());
        Assert.Equal(rideId, recoveredJson.RootElement.GetProperty("rideId").GetString());
        Assert.Equal(1, recoveredJson.RootElement.GetProperty("stops").GetArrayLength());
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("recovery-booking-driver"));
    }

    [Fact]
    public async Task DiscoveryReleasesPermanentReservationWhenRideIsAlreadyTerminal()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "terminal-reservation-rider");
        using var driver = factory.CreateClient("Driver", "terminal-reservation-driver");
        SeedBookableDriver(factory, "terminal-reservation-driver", "terminal-reservation-union");

        using var request = CreateSelectedBookingRequest("terminal-reservation-key", "terminal-reservation-driver",
            "terminal-reservation-union", "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        using var proposal = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 350m, currency = "INR" })
        };
        proposal.Headers.Add("Idempotency-Key", "terminal-reservation-agreement");
        using var proposalResponse = await rider.SendAsync(proposal);
        Assert.Equal(HttpStatusCode.Created, proposalResponse.StatusCode);
        using var proposalJson = JsonDocument.Parse(await proposalResponse.Content.ReadAsStringAsync());
        var version = proposalJson.RootElement.GetProperty("agreement").GetProperty("version").GetInt32();

        using var riderConfirm = await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/{version}/confirm", null);
        using var driverConfirm = await driver.PostAsync($"/api/v1/rides/{rideId}/agreements/{version}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, riderConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, driverConfirm.StatusCode);
        using var accept = await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("terminal-reservation-driver"));

        // Simulate a process interruption after the terminal ride write but before reservation release.
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with { Status = "Completed", NegotiationExpiresAtUtc = null };

        using var discovery = await rider.GetAsync("/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        using var discoveryJson = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
        var discoveredDriverIds = discoveryJson.RootElement.GetProperty("unions").EnumerateArray()
            .SelectMany(group => group.GetProperty("drivers").EnumerateArray())
            .Select(item => item.GetProperty("driverId").GetString());
        Assert.Contains("terminal-reservation-driver", discoveredDriverIds);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("terminal-reservation-driver"));
    }

    private static void SeedActiveCabUnion(TurnOvaApiFactory factory, string driverId)
    {
        var now = DateTimeOffset.UtcNow;
        var unionId = "availability-union-" + driverId;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[unionId] =
            new DriverUnion(unionId, "Availability Test Union", "AVAIL-" + driverId, "Hyderabad", null, null,
                ["hyderabad-central"], ["cab"], "Active", now, now);
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>()[driverId] =
            new DriverUnionAssignment(driverId, unionId, "Availability Test Union", "Active", now, now, "test");
    }

    private static void SeedBookableDriver(TurnOvaApiFactory factory, string driverId, string unionId)
    {
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[unionId] =
            new DriverUnion(unionId, "Selected Booking Union", "SEL-" + driverId, "Hyderabad", null, null,
                ["hyderabad-central"], ["cab"], "Active", now, now);
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>()[driverId] =
            new DriverUnionAssignment(driverId, unionId, "Selected Booking Union", "Active", now, now, "test");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var profile = profiles[driverId];
        profiles[driverId] = profile with
        {
            AvailabilityStatus = "Online",
            CurrentLatitude = 17.493,
            CurrentLongitude = 78.399,
            LocationUpdatedAtUtc = now,
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", "test/licence", "Approved"),
                new DriverDocumentReference("VehicleRegistration", "test/vehicle-registration", "Approved")
            ],
            ProfilePhotoApprovalStatus = "Approved",
            ProfilePhotoApprovedByUnionId = unionId
        };
    }

    private static HttpRequestMessage CreateSelectedBookingRequest(
        string key, string driverId, string unionId, string pickup, string dropoff, string[] stops)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rider/bookings")
        {
            Content = JsonContent.Create(new
            {
                selectedDriverId = driverId, unionId, pickup, dropoff, stops,
                passengers = 1, pickupLatitude = 17.493, pickupLongitude = 78.399
            })
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    [Fact]
    public async Task RideAgreementRequiresBothPartiesToConfirmCurrentImmutableVersionBeforeStart()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "agreement-rider");
        using var driver = factory.CreateClient("Driver", "agreement-driver");
        const string rideId = "RIDE-AGREEMENT-001";
        SeedAgreementRide(factory, rideId, "agreement-rider", "agreement-driver");

        using var riderProposal = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 450.00m, currency = "INR" })
        };
        riderProposal.Headers.Add("Idempotency-Key", "agreement-rider-proposal-001");
        using var proposalResponse = await rider.SendAsync(riderProposal);
        Assert.Equal(HttpStatusCode.Created, proposalResponse.StatusCode);
        using var proposalJson = JsonDocument.Parse(await proposalResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, proposalJson.RootElement.GetProperty("agreement").GetProperty("version").GetInt32());

        using var retry = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 450.00m, currency = "INR" })
        };
        retry.Headers.Add("Idempotency-Key", "agreement-rider-proposal-001");
        using var retryResponse = await rider.SendAsync(retry);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using var retryJson = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, retryJson.RootElement.GetProperty("agreement").GetProperty("version").GetInt32());

        using var counter = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 500.00m, currency = "INR" })
        };
        counter.Headers.Add("Idempotency-Key", "agreement-driver-counter-001");
        using var counterResponse = await driver.SendAsync(counter);
        Assert.Equal(HttpStatusCode.Created, counterResponse.StatusCode);
        using var counterJson = JsonDocument.Parse(await counterResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, counterJson.RootElement.GetProperty("agreement").GetProperty("version").GetInt32());

        using var staleConfirmation = await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, staleConfirmation.StatusCode);

        using var arrive = await driver.PostAsync($"/api/v1/rides/{rideId}/arrive", null);
        Assert.Equal(HttpStatusCode.OK, arrive.StatusCode);
        using var blockedStart = await driver.PostAsync($"/api/v1/rides/{rideId}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, blockedStart.StatusCode);

        using var riderConfirm = await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/2/confirm", null);
        Assert.Equal(HttpStatusCode.OK, riderConfirm.StatusCode);
        using var stillBlocked = await driver.PostAsync($"/api/v1/rides/{rideId}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, stillBlocked.StatusCode);

        using var driverConfirm = await driver.PostAsync($"/api/v1/rides/{rideId}/agreements/2/confirm", null);
        Assert.Equal(HttpStatusCode.OK, driverConfirm.StatusCode);
        using var allowedStart = await driver.PostAsync($"/api/v1/rides/{rideId}/start", null);
        Assert.Equal(HttpStatusCode.OK, allowedStart.StatusCode);

        using var inTripCounter = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 550.00m, currency = "INR" })
        };
        inTripCounter.Headers.Add("Idempotency-Key", "agreement-driver-counter-during-trip");
        using var inTripCounterResponse = await driver.SendAsync(inTripCounter);
        Assert.Equal(HttpStatusCode.Created, inTripCounterResponse.StatusCode);
        using var blockedCompletion = await driver.PostAsync($"/api/v1/rides/{rideId}/complete", null);
        Assert.Equal(HttpStatusCode.Conflict, blockedCompletion.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/3/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/agreements/3/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/complete", null)).StatusCode);

        using var historyResponse = await rider.GetAsync($"/api/v1/rides/{rideId}/agreements");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        var versions = history.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, versions.Length);
        Assert.Equal(450.00m, versions[0].GetProperty("amount").GetDecimal());
        Assert.Equal(500.00m, versions[1].GetProperty("amount").GetDecimal());
        Assert.Equal(550.00m, versions[2].GetProperty("amount").GetDecimal());
        Assert.True(versions[1].GetProperty("riderConfirmed").GetBoolean());
        Assert.True(versions[1].GetProperty("driverConfirmed").GetBoolean());
        Assert.True(versions[2].GetProperty("riderConfirmed").GetBoolean());
        Assert.True(versions[2].GetProperty("driverConfirmed").GetBoolean());
        Assert.False(versions[0].GetProperty("riderConfirmed").GetBoolean());
        Assert.False(versions[0].GetProperty("driverConfirmed").GetBoolean());
    }

    [Theory]
    [InlineData(0, "INR")]
    [InlineData(-1, "INR")]
    [InlineData(10.123, "INR")]
    [InlineData(10, "inr")]
    [InlineData(10, "US")]
    public async Task RideAgreementRejectsInvalidAmountOrCurrency(decimal amount, string currency)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "agreement-validation-rider");
        using var driver = factory.CreateClient("Driver", "agreement-validation-driver");
        const string rideId = "RIDE-AGREEMENT-VALIDATION";
        SeedAgreementRide(factory, rideId, "agreement-validation-rider", "agreement-validation-driver");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount, currency })
        };
        request.Headers.Add("Idempotency-Key", "agreement-validation-001");
        using var response = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentAgreementProposalsCannotCreateTheSameCurrentVersion()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "agreement-race-rider");
        using var driver = factory.CreateClient("Driver", "agreement-race-driver");
        const string rideId = "RIDE-AGREEMENT-RACE";
        SeedAgreementRide(factory, rideId, "agreement-race-rider", "agreement-race-driver");

        HttpRequestMessage Proposal(string key, decimal amount)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
            {
                Content = JsonContent.Create(new { amount, currency = "INR" })
            };
            request.Headers.Add("Idempotency-Key", key);
            return request;
        }

        using var first = Proposal("agreement-race-a", 300m);
        using var second = Proposal("agreement-race-b", 325m);
        var responses = await Task.WhenAll(rider.SendAsync(first), driver.SendAsync(second));
        Assert.All(responses, response =>
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        foreach (var response in responses) response.Dispose();

        var stored = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()
            .GetValueOrDefault(rideId)!.AgreementVersions;
        Assert.NotNull(stored);
        Assert.InRange(stored.Count, 1, 2);
        Assert.Equal(stored.Count, stored.Select(item => item.Version).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, stored.Count), stored.Select(item => item.Version).OrderBy(item => item));
    }

    [Fact]
    public async Task RideAgreementIsVisibleOnlyToTheAssignedRiderAndDriver()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "agreement-private-rider");
        using var driver = factory.CreateClient("Driver", "agreement-private-driver");
        using var other = factory.CreateClient("Rider", "agreement-private-other");
        const string rideId = "RIDE-AGREEMENT-PRIVATE";
        SeedAgreementRide(factory, rideId, "agreement-private-rider", "agreement-private-driver");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 250m, currency = "INR" })
        };
        request.Headers.Add("Idempotency-Key", "agreement-private-001");
        Assert.Equal(HttpStatusCode.Created, (await rider.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/rides/{rideId}/agreements")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await driver.GetAsync($"/api/v1/rides/{rideId}/agreements")).StatusCode);
    }


    [Theory]
    [InlineData(500.00, 400.00, 100.00)]
    [InlineData(500.00, 500.00, 0.00)]
    [InlineData(400.00, 500.00, -100.00)]
    [InlineData(400.75, 400.50, 0.25)]
    [InlineData(500.00, 0.00, 500.00)]
    public async Task DriverCanReportDirectPaymentWithSignedDeltaAndUnverifiedStatus(
        decimal agreedAmount, decimal amountReceived, decimal expectedDelta)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-report-driver";
        const string unionId = "payment-report-union";
        using var rider = factory.CreateClient("Rider", "payment-report-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, unionId, "payment-report-" + agreedAmount, agreedAmount);

        using var request = CreatePaymentReportRequest(rideId, "payment-report-key", amountReceived);
        using var response = await driver.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = json.RootElement;
        Assert.Equal(rideId, report.GetProperty("rideId").GetString());
        Assert.Equal(1, report.GetProperty("agreementVersion").GetInt32());
        Assert.Equal(unionId, report.GetProperty("unionId").GetString());
        Assert.Equal(agreedAmount, report.GetProperty("agreedAmount").GetDecimal());
        Assert.Equal(amountReceived, report.GetProperty("amountReceived").GetDecimal());
        Assert.Equal(expectedDelta, report.GetProperty("delta").GetDecimal());
        Assert.Equal("INR", report.GetProperty("currency").GetString());
        Assert.Equal(driverId, report.GetProperty("reportedByDriverId").GetString());
        Assert.Equal("DriverReportedUnverified", report.GetProperty("status").GetString());

        var stored = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId];
        Assert.Equal(expectedDelta, stored.PaymentReconciliation!.Delta);
        Assert.Equal("DriverReportedUnverified", stored.PaymentReconciliation.Status);
        var history = await driver.GetFromJsonAsync<List<JsonElement>>("/api/v1/driver/rides/mine");
        var historyItem = history!.Single(item => item.GetProperty("rideId").GetString() == rideId);
        Assert.Equal(expectedDelta,
            historyItem.GetProperty("paymentReconciliation").GetProperty("delta").GetDecimal());
    }

    [Fact]
    public async Task DriverPaymentReportUsesTheConfirmedAgreementCurrency()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-currency-driver";
        using var rider = factory.CreateClient("Rider", "payment-currency-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, "payment-currency-union", "payment-currency", 500m, "USD");

        using var request = CreatePaymentReportRequest(rideId, "payment-currency-key", 450m);
        using var response = await driver.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("USD", json.RootElement.GetProperty("currency").GetString());
        Assert.Equal(50m, json.RootElement.GetProperty("delta").GetDecimal());
    }

    [Theory]
    [InlineData(-1.00)]
    [InlineData(10.123)]
    public async Task DriverPaymentReportRejectsNegativeOrExcessPrecision(decimal amountReceived)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-invalid-driver";
        using var rider = factory.CreateClient("Rider", "payment-invalid-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, "payment-invalid-union", "payment-invalid-" + amountReceived, 500m);

        using var request = CreatePaymentReportRequest(rideId, "payment-invalid-key", amountReceived);
        using var response = await driver.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId]
            .PaymentReconciliation);
    }


    [Fact]
    public async Task DriverPaymentReportRequiresAnExplicitAmount()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-missing-amount-driver";
        using var rider = factory.CreateClient("Rider", "payment-missing-amount-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, "payment-missing-amount-union", "payment-missing-amount", 500m);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/driver/rides/{rideId}/payment-report")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Idempotency-Key", "payment-missing-amount-key");
        using var response = await driver.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId]
            .PaymentReconciliation);
    }

    [Fact]
    public async Task DriverPaymentReportRequiresAnIdempotencyKey()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-no-key-driver";
        using var rider = factory.CreateClient("Rider", "payment-no-key-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, "payment-no-key-union", "payment-no-key", 500m);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/driver/rides/{rideId}/payment-report")
        {
            Content = JsonContent.Create(new { amountReceived = 400m })
        };
        using var response = await driver.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DriverPaymentReportRequiresCompletedRideAndAssignedDriver()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-state-driver";
        using var rider = factory.CreateClient("Rider", "payment-state-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var negotiatingRideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, driverId, "payment-state-union", "payment-state-negotiating", 500m);

        using var beforeCompletion = CreatePaymentReportRequest(
            negotiatingRideId, "payment-state-before-completion", 400m);
        using var blocked = await driver.SendAsync(beforeCompletion);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        const string completedDriverId = "payment-owner-driver";
        using var completedRider = factory.CreateClient("Rider", "payment-owner-rider");
        using var completedDriver = factory.CreateClient("Driver", completedDriverId);
        var completedRideId = await CreateCompletedSelectedRideAsync(
            factory, completedRider, completedDriver, completedDriverId, "payment-owner-union", "payment-owner", 500m);
        using var otherDriver = factory.CreateClient("Driver", "payment-other-driver");
        using var wrongOwnerRequest = CreatePaymentReportRequest(
            completedRideId, "payment-wrong-owner", 400m);
        using var wrongOwner = await otherDriver.SendAsync(wrongOwnerRequest);
        Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);

        using var riderRequest = CreatePaymentReportRequest(
            completedRideId, "payment-rider-forbidden", 400m);
        using var riderForbidden = await completedRider.SendAsync(riderRequest);
        Assert.Equal(HttpStatusCode.Forbidden, riderForbidden.StatusCode);
    }

    [Fact]
    public async Task PaymentReportIsIdempotentAndCannotBeOverwritten()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-idempotent-driver";
        using var rider = factory.CreateClient("Rider", "payment-idempotent-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, "payment-idempotent-union", "payment-idempotent", 500m);

        using var firstRequest = CreatePaymentReportRequest(rideId, "payment-idempotent-key", 400m);
        using var first = await driver.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var retryRequest = CreatePaymentReportRequest(rideId, "payment-idempotent-key", 400m);
        using var retry = await driver.SendAsync(retryRequest);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        using var changedPayloadRequest = CreatePaymentReportRequest(rideId, "payment-idempotent-key", 450m);
        using var changedPayload = await driver.SendAsync(changedPayloadRequest);
        Assert.Equal(HttpStatusCode.Conflict, changedPayload.StatusCode);

        using var newKeyRequest = CreatePaymentReportRequest(rideId, "payment-idempotent-new-key", 400m);
        using var newKey = await driver.SendAsync(newKeyRequest);
        Assert.Equal(HttpStatusCode.Conflict, newKey.StatusCode);

        Assert.Equal(400m, factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId]
            .PaymentReconciliation!.AmountReceived);
    }

    [Fact]
    public async Task ConcurrentPaymentReportsCannotOverwriteTheFirstReport()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-race-driver";
        using var rider = factory.CreateClient("Rider", "payment-race-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, "payment-race-union", "payment-race", 500m);

        using var firstRequest = CreatePaymentReportRequest(rideId, "payment-race-a", 400m);
        using var secondRequest = CreatePaymentReportRequest(rideId, "payment-race-b", 450m);
        var responses = await Task.WhenAll(driver.SendAsync(firstRequest), driver.SendAsync(secondRequest));
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
        Assert.Contains(factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId]
            .PaymentReconciliation!.AmountReceived, new[] { 400m, 450m });
    }

    [Fact]
    public async Task UnionManagerSeesOnlyItsMinimalPaymentReconciliationRecords()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var riderA = factory.CreateClient("Rider", "payment-union-rider-a");
        using var driverA = factory.CreateClient("Driver", "payment-union-driver-a");
        const string unionA = "payment-union-a";
        var rideA = await CreateCompletedSelectedRideAsync(
            factory, riderA, driverA, "payment-union-driver-a", unionA, "payment-union-a", 500m);
        using var reportARequest = CreatePaymentReportRequest(rideA, "payment-union-report-a", 450m);
        using var reportA = await driverA.SendAsync(reportARequest);
        Assert.Equal(HttpStatusCode.OK, reportA.StatusCode);

        using var riderB = factory.CreateClient("Rider", "payment-union-rider-b");
        using var driverB = factory.CreateClient("Driver", "payment-union-driver-b");
        const string unionB = "payment-union-b";
        var rideB = await CreateCompletedSelectedRideAsync(
            factory, riderB, driverB, "payment-union-driver-b", unionB, "payment-union-b", 700m);
        using var reportBRequest = CreatePaymentReportRequest(rideB, "payment-union-report-b", 650m);
        using var reportB = await driverB.SendAsync(reportBRequest);
        Assert.Equal(HttpStatusCode.OK, reportB.StatusCode);

        using var managerA = factory.CreateUnionManagerClient(unionA, "payment-union-manager-a");
        using var managerB = factory.CreateUnionManagerClient(unionB, "payment-union-manager-b");
        using var managerWithoutUnion = factory.CreateClient("UnionManager", "payment-union-manager-unscoped");

        using var responseA = await managerA.GetAsync("/api/v1/union/reconciliation");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        using var jsonA = JsonDocument.Parse(await responseA.Content.ReadAsStringAsync());
        var recordsA = jsonA.RootElement.EnumerateArray().ToArray();
        var recordA = Assert.Single(recordsA);
        Assert.Equal(rideA, recordA.GetProperty("rideId").GetString());
        Assert.Equal(unionA, recordA.GetProperty("unionId").GetString());
        Assert.False(recordA.TryGetProperty("riderId", out _));
        Assert.False(recordA.TryGetProperty("pickup", out _));
        Assert.False(recordA.TryGetProperty("dropoff", out _));
        Assert.False(recordA.TryGetProperty("stops", out _));
        Assert.False(recordA.TryGetProperty("phoneNumber", out _));
        Assert.False(recordA.TryGetProperty("driverLatitude", out _));
        Assert.False(recordA.TryGetProperty("privateChat", out _));

        using var responseB = await managerB.GetAsync("/api/v1/union/reconciliation");
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        using var jsonB = JsonDocument.Parse(await responseB.Content.ReadAsStringAsync());
        Assert.Equal(rideB, Assert.Single(jsonB.RootElement.EnumerateArray())
            .GetProperty("rideId").GetString());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await managerWithoutUnion.GetAsync("/api/v1/union/reconciliation")).StatusCode);
    }

    private static async Task<string> CreateSelectedRideWithConfirmedAgreementAsync(
        TurnOvaApiFactory factory, HttpClient rider, HttpClient driver,
        string driverId, string unionId, string bookingKey, decimal agreedAmount, string currency = "INR")
    {
        SeedBookableDriver(factory, driverId, unionId);
        using var bookingRequest = CreateSelectedBookingRequest(
            bookingKey, driverId, unionId, "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(bookingRequest);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        using var proposal = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = agreedAmount, currency })
        };
        proposal.Headers.Add("Idempotency-Key", "payment-agreement-" + bookingKey);
        using var proposalResponse = await rider.SendAsync(proposal);
        Assert.Equal(HttpStatusCode.Created, proposalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
        return rideId;
    }

    private static async Task<string> CreateCompletedSelectedRideAsync(
        TurnOvaApiFactory factory, HttpClient rider, HttpClient driver,
        string driverId, string unionId, string bookingKey, decimal agreedAmount, string currency = "INR")
    {
        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, driverId, unionId, bookingKey, agreedAmount, currency);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/arrive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/complete", null)).StatusCode);
        return rideId;
    }

    private static HttpRequestMessage CreatePaymentReportRequest(string rideId, string idempotencyKey, decimal amountReceived)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/driver/rides/{rideId}/payment-report")
        {
            Content = JsonContent.Create(new { amountReceived })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task ConfirmRideAgreementAsync(
        TurnOvaApiFactory factory, HttpClient rider, HttpClient driver, string rideId, string driverId)
    {
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()["agreement-union-" + driverId] =
            new DriverUnion("agreement-union-" + driverId, "Agreement Test Union", "AGR-" + driverId, "Hyderabad", null, null,
                ["hyderabad-central"], ["cab"], "Active", now, now);
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>()[driverId] =
            new DriverUnionAssignment(driverId, "agreement-union-" + driverId, "Agreement Test Union", "Active", now, now, "test");

        using var proposal = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 500m, currency = "INR" })
        };
        proposal.Headers.Add("Idempotency-Key", "lifecycle-agreement-" + rideId);
        Assert.Equal(HttpStatusCode.Created, (await rider.SendAsync(proposal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
    }

    private static void SeedAgreementRide(TurnOvaApiFactory factory, string rideId, string riderId, string driverId)
    {
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()["agreement-union"] =
            new DriverUnion("agreement-union", "Agreement Test Union", "AGR", "Hyderabad", null, null,
                ["hyderabad-central"], ["cab"], "Active", now, now);
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>()[driverId] =
            new DriverUnionAssignment(driverId, "agreement-union", "Agreement Test Union", "Active", now, now, "test");
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId] =
            new DemoRide(rideId, "KPHB", "HITEC City", 1, "Accepted", now, null, riderId, driverId,
                17.493, 78.399, null, null, now, null, false, "cab", "hyderabad-central");
    }

    private static void SetLegacyDriverOnline(TurnOvaApiFactory factory, string driverId)
    {
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        if (!profiles.TryGetValue(driverId, out var profile))
            throw new InvalidOperationException($"Test driver profile '{driverId}' was not created.");
        profiles[driverId] = profile with
        {
            AvailabilityStatus = "Online",
            CurrentLatitude = 17.493,
            CurrentLongitude = 78.399,
            LocationUpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task EnqueueLegacyDriverAsync(TurnOvaApiFactory factory, string driverId)
    {
        SetLegacyDriverOnline(factory, driverId);
        await factory.Services.GetRequiredService<IQueueStore>().AddAsync(
            new DriverQueueEntry(driverId, "hyderabad-central", "cab", DateTimeOffset.UtcNow.AddMinutes(-1), 0, true),
            CancellationToken.None);
    }

    private static HttpRequestMessage CreateRideRequest(
        string? idempotencyKey,
        int passengers = 1,
        string? pickup = "KPHB, Hyderabad",
        string? dropoff = "HITEC City, Hyderabad",
        double? pickupLatitude = 17.493,
        double? pickupLongitude = 78.399,
        string serviceClass = "cab")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rides/request")
        {
            Content = JsonContent.Create(new
            {
                pickup,
                dropoff,
                passengers,
                pickupLatitude,
                pickupLongitude,
                serviceClass
            })
        };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task<TestRide> CreateRideAsync(HttpClient rider, string key)
    {
        using var response = await rider.SendAsync(CreateRideRequest(key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadRideAsync(response);
    }

    private static async Task<TestRide> ReadRideAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TestRide>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The API returned an empty ride response.");
    }

    private sealed record RiderRideHistoryItemTest(string RideId, string Pickup, string Dropoff, int Passengers, string ServiceClass, string Status, DateTimeOffset RequestedAt, DateTimeOffset? AcceptedAt);
    private sealed record DriverRideHistoryItemTest(string RideId, string Pickup, string Dropoff, int Passengers, string ServiceClass, string Status, DateTimeOffset RequestedAt, DateTimeOffset? AcceptedAt, bool LocationSharingActive);
    private sealed record TestRide(string RideId, string Status, bool LocationSharingActive, int Passengers);
    private sealed record QueueEntryTest(string DriverId, int Position, DateTimeOffset JoinedAt);

    private sealed class TurnOvaApiFactory : WebApplicationFactory<Program>
    {
        private readonly bool _legacyFifoRiderRequestsEnabled;
        private readonly double? _nearbyRadiusKm;
        private readonly int? _selectedDriverNegotiationLifetimeMinutes;

        public TurnOvaApiFactory(bool legacyFifoRiderRequestsEnabled = true, double? nearbyRadiusKm = 10,
            int? selectedDriverNegotiationLifetimeMinutes = 15)
        {
            _legacyFifoRiderRequestsEnabled = legacyFifoRiderRequestsEnabled;
            _nearbyRadiusKm = nearbyRadiusKm;
            _selectedDriverNegotiationLifetimeMinutes = selectedDriverNegotiationLifetimeMinutes;
        }

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
                    ["DriverOnboarding:AdminApiKey"] = "integration-test-admin-key-32chars",
                    ["Cors:AllowedOrigins:0"] = "https://app.turnova.test",
                    ["Dispatch:NearbyRadiusKm"] = _nearbyRadiusKm?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Dispatch:SelectedDriverNegotiationLifetimeMinutes"] =
                        _selectedDriverNegotiationLifetimeMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Dispatch:LegacyFifoRiderRequestsEnabled"] = _legacyFifoRiderRequestsEnabled ? "true" : "false"
                }));
        }

        public HttpClient CreateClient(string role, string subject, string? unionId = null)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(role, subject, unionId));
            if (role == "Driver")
            {
                var profiles = Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
                profiles[subject] = new DriverOnboardingProfile(
                    subject, subject, "hyderabad-central", "Approved Test Driver", "+919876543210",
                    "Maruti", "Dzire", "TS09AB1234", "cab", 3,
                    [
                        new DriverDocumentReference("DrivingLicence", "test/licence", "Approved"),
                        new DriverDocumentReference("VehicleRegistration", "test/vehicle-registration", "Approved")
                    ],
                    "Approved", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    ProfilePhotoBase64: "/9j/2Q==",
                    ProfilePhotoContentType: "image/jpeg",
                    ProfilePhotoCapturedAtUtc: DateTimeOffset.UtcNow,
                    ProfilePhotoApprovalStatus: "Approved");
            }
            return client;
        }

        public HttpClient CreateUnionManagerClient(string unionId, string subject) =>
            CreateClient("UnionManager", subject, unionId);

        private static string CreateToken(string role, string subject, string? unionId)
        {
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256);
            var claims = new List<Claim>
            {
                new("sub", subject),
                new("role", role),
                new("name", subject)
            };
            if (!string.IsNullOrWhiteSpace(unionId))
                claims.Add(new Claim("unionId", unionId));
            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
