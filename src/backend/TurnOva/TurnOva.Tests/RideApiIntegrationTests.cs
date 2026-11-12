using TurnOva.API;
using TurnOva.API.Observability;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using UglyToad.PdfPig;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TurnOva.Application.Dispatch;
using TurnOva.Application.Assistance;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Dispatch;

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
        Assert.False(json.RootElement.TryGetProperty("database", out _));
    }

    [Fact]
    public async Task ReadinessReturns503WhenStorageIsUnavailableWhileLivenessRemainsHealthy()
    {
        using var factory = new TurnOvaApiFactory(storageReadinessUnavailable: true);
        using var client = factory.CreateClient("Rider", "rider-storage-unavailable");

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var ready = await client.GetAsync("/health/ready");
        var body = await ready.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Contains("Storage dependency unavailable", body);
        Assert.DoesNotContain("test-only-storage-failure", body);
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
    public async Task AuthenticatedDriverAcceptQueueReadFailurePersistsSafeExceptionAuditAndReturnsGeneric500()
    {
        const string driverId = "dispatch-audit-driver";
        var queueStore = new QueueReadFailureStore(new InMemoryQueueStore());
        using var factory = new TurnOvaApiFactory(queueStore: queueStore);
        using var rider = factory.CreateClient("Rider", "dispatch-audit-rider");
        using var driver = factory.CreateClient("Driver", driverId);

        await EnqueueLegacyDriverAsync(factory, driverId);
        using var createRide = await rider.SendAsync(CreateRideRequest("dispatch-audit-ride"));
        Assert.Equal(HttpStatusCode.Created, createRide.StatusCode);
        var ride = await ReadRideAsync(createRide);

        using var unauthorizedAttempt = await rider.PostAsync($"/api/v1/rides/{ride.RideId}/accept", null);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedAttempt.StatusCode);
        Assert.Empty((await queueStore.GetAuditEventsAsync(
            "hyderabad-central", "cab", CancellationToken.None))
            .Where(item => item.EventType == QueueAuditEventTypes.DispatchException));

        queueStore.FailNextRead();
        using var response = await driver.PostAsync($"/api/v1/rides/{ride.RideId}/accept", null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("An unexpected error occurred.", body);
        Assert.DoesNotContain("sensitive-queue-read-failure", body);
        using var problem = JsonDocument.Parse(body);
        var traceId = problem.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));

        var auditEvents = await queueStore.GetAuditEventsAsync(
            "hyderabad-central", "cab", CancellationToken.None);
        var audit = Assert.Single(auditEvents.Where(item =>
            item.EventType == QueueAuditEventTypes.DispatchException));
        Assert.Equal("hyderabad-central|cab", audit.ZoneServiceKey);
        Assert.Equal(driverId, audit.DriverId);
        Assert.Equal(traceId, audit.TraceId);
        Assert.Equal(QueueAuditReasonCategories.QueueReadFailed, audit.ReasonCategory);
        Assert.Equal(TimeSpan.Zero, audit.OccurredAtUtc.Offset);
        Assert.Null(audit.OfferId);

        var unchangedRide = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[ride.RideId];
        Assert.Equal("Requested", unchangedRide.Status);
        var unchangedQueue = await queueStore.GetEligibleAsync(
            "hyderabad-central", "cab", CancellationToken.None);
        Assert.Equal(driverId, Assert.Single(unchangedQueue).DriverId);
        Assert.DoesNotContain(auditEvents, item =>
            item.EventType is QueueAuditEventTypes.Left or
                QueueAuditEventTypes.MovedToBack or QueueAuditEventTypes.RotatedToBack);

        var auditJson = JsonSerializer.Serialize(audit, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("sensitive-queue-read-failure", auditJson);
        foreach (var forbiddenField in new[]
        {
            "exceptionMessage", "requestBody", "token", "phone", "email",
            "latitude", "longitude", "pickup", "dropoff", "fare"
        })
            Assert.DoesNotContain(forbiddenField, auditJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticatedSelectedDriverReservationFailurePersistsSafeExceptionAuditAndPreservesLease()
    {
        const string driverId = "dispatch-selected-audit-driver";
        const string unionId = "dispatch-selected-audit-union";
        var queueStore = new InMemoryQueueStore();
        var reservationStore = new ReservationMakePermanentFailureStore(
            new InMemoryDriverRideReservationStore());
        using var factory = new TurnOvaApiFactory(
            legacyFifoRiderRequestsEnabled: false,
            queueStore: queueStore,
            reservationStore: reservationStore);
        using var rider = factory.CreateClient("Rider", "dispatch-selected-audit-rider");
        using var driver = factory.CreateClient("Driver", driverId);

        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, driverId, unionId, "dispatch-selected-audit-booking", 420m);
        var reservationBefore = await reservationStore.GetActiveAsync(driverId);
        Assert.NotNull(reservationBefore);
        Assert.Equal(rideId, reservationBefore.RideId);
        Assert.NotNull(reservationBefore.ExpiresAtUtc);

        reservationStore.FailNextMakePermanent();
        using var response = await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("An unexpected error occurred.", body);
        Assert.DoesNotContain("sensitive-reservation-store-failure", body);
        using var problem = JsonDocument.Parse(body);
        var traceId = problem.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));

        var auditEvents = await queueStore.GetAuditEventsAsync(
            "hyderabad-central", "cab", CancellationToken.None);
        var audit = Assert.Single(auditEvents);
        Assert.Equal("hyderabad-central|cab", audit.ZoneServiceKey);
        Assert.Equal(driverId, audit.DriverId);
        Assert.Equal(QueueAuditEventTypes.DispatchException, audit.EventType);
        Assert.Equal(traceId, audit.TraceId);
        Assert.Equal(QueueAuditReasonCategories.DriverReservationFailed, audit.ReasonCategory);
        Assert.Equal(TimeSpan.Zero, audit.OccurredAtUtc.Offset);
        Assert.Null(audit.OfferId);

        var unchangedRide = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId];
        Assert.Equal("AgreementConfirmed", unchangedRide.Status);
        Assert.Equal(reservationBefore, await reservationStore.GetActiveAsync(driverId));

        var auditJson = JsonSerializer.Serialize(audit, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("sensitive-reservation-store-failure", auditJson);
        foreach (var forbiddenField in new[]
        {
            "exceptionMessage", "requestBody", "token", "phone", "email",
            "latitude", "longitude", "pickup", "dropoff", "fare"
        })
            Assert.DoesNotContain(forbiddenField, auditJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectedDriverAcceptRecoversWhenReservationPromotionCommitsBeforeTransientFailure()
    {
        const string driverId = "dispatch-ambiguous-reservation-driver";
        const string unionId = "dispatch-ambiguous-reservation-union";
        var queueStore = new InMemoryQueueStore();
        var reservationStore = new ReservationMakePermanentFailureStore(
            new InMemoryDriverRideReservationStore());
        using var factory = new TurnOvaApiFactory(
            legacyFifoRiderRequestsEnabled: false,
            queueStore: queueStore,
            reservationStore: reservationStore);
        using var rider = factory.CreateClient("Rider", "dispatch-ambiguous-reservation-rider");
        using var driver = factory.CreateClient("Driver", driverId);

        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, driverId, unionId, "dispatch-ambiguous-reservation-booking", 420m);
        reservationStore.FailAfterNextMakePermanent();

        using var response = await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accepted = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId];
        Assert.Equal("Accepted", accepted.Status);

        var reservation = await reservationStore.GetActiveAsync(driverId);
        Assert.NotNull(reservation);
        Assert.Equal(rideId, reservation.RideId);
        Assert.Null(reservation.ExpiresAtUtc);
        var recoveredAudit = Assert.Single(await queueStore.GetAuditEventsAsync(
            "hyderabad-central", "cab", CancellationToken.None));
        Assert.Equal(QueueAuditEventTypes.DispatchException, recoveredAudit.EventType);
        Assert.Equal(driverId, recoveredAudit.DriverId);
        Assert.Equal(QueueAuditReasonCategories.DriverReservationPromotionRecovered, recoveredAudit.ReasonCategory);
        Assert.False(string.IsNullOrWhiteSpace(recoveredAudit.TraceId));
        Assert.Equal(TimeSpan.Zero, recoveredAudit.OccurredAtUtc.Offset);
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
    public async Task ConcurrentInitialOnboardingSubmissionsLeaveOneCompleteProfile()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-onboarding-create-race");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles.TryRemove("driver-onboarding-create-race", out _);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => driver.PostAsJsonAsync("/api/v1/driver/onboarding", ValidOnboardingRequest())));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        Assert.All(responses, response =>
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Accepted, HttpStatusCode.Conflict }));

        var profile = profiles["driver-onboarding-create-race"];
        Assert.Equal("Pending", profile.Status);
        Assert.Equal("cab", profile.ServiceClass);
        Assert.Equal("Test Driver", profile.FullName);
        Assert.Contains(profile.Documents, document => document.DocumentType == "DrivingLicence");
        Assert.Contains(profile.Documents, document => document.DocumentType == "VehicleRegistration");

        foreach (var response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task OnboardingResubmissionPreservesLatestAvailabilityAndLocationFields()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-onboarding-preserve-latest");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles.TryRemove("driver-onboarding-preserve-latest", out _);

        Assert.Equal(HttpStatusCode.Accepted,
            (await driver.PostAsJsonAsync("/api/v1/driver/onboarding", ValidOnboardingRequest())).StatusCode);
        var submitted = profiles["driver-onboarding-preserve-latest"];
        var submittedAtUtc = submitted.SubmittedAtUtc;
        var locationUpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        profiles["driver-onboarding-preserve-latest"] = submitted with
        {
            AvailabilityStatus = "Offline",
            CurrentLatitude = 17.493,
            CurrentLongitude = 78.399,
            LocationUpdatedAtUtc = locationUpdatedAtUtc
        };

        var resubmission = await driver.PostAsJsonAsync("/api/v1/driver/onboarding",
            new
            {
                profilePhotoBase64 = "/9j/2Q==",
                zoneId = "hyderabad-central",
                fullName = "Updated Driver",
                phoneNumber = "+919876543211",
                vehicleMake = "Maruti",
                vehicleModel = "Dzire",
                vehicleRegistration = "TS09AB5678",
                serviceClass = "cab",
                passengerCapacity = 4,
                documents = new[]
                {
                    new { documentType = "DrivingLicence", storageReference = "drivers/test/licence-v2" },
                    new { documentType = "VehicleRegistration", storageReference = "drivers/test/rc-v2" }
                }
            });

        Assert.Equal(HttpStatusCode.Accepted, resubmission.StatusCode);
        var updated = profiles["driver-onboarding-preserve-latest"];
        Assert.Equal("Updated Driver", updated.FullName);
        Assert.Equal("Pending", updated.Status);
        Assert.Equal(submittedAtUtc, updated.SubmittedAtUtc);
        Assert.Equal("Offline", updated.AvailabilityStatus);
        Assert.Equal(17.493, updated.CurrentLatitude);
        Assert.Equal(78.399, updated.CurrentLongitude);
        Assert.Equal(locationUpdatedAtUtc, updated.LocationUpdatedAtUtc);
        Assert.Contains(updated.Documents, document => document.StorageReference == "drivers/test/licence-v2");
    }

    [Fact]
    public async Task DriverSelfieReplacementPreservesLatestAvailabilityAndLocationFields()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-selfie-preserve-latest");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles.TryRemove("driver-selfie-preserve-latest", out _);

        Assert.Equal(HttpStatusCode.Accepted,
            (await driver.PostAsJsonAsync("/api/v1/driver/onboarding", ValidOnboardingRequest())).StatusCode);
        var submitted = profiles["driver-selfie-preserve-latest"];
        var locationUpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        profiles["driver-selfie-preserve-latest"] = submitted with
        {
            AvailabilityStatus = "Offline",
            CurrentLatitude = 17.401,
            CurrentLongitude = 78.402,
            LocationUpdatedAtUtc = locationUpdatedAtUtc,
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoReviewedAtUtc = locationUpdatedAtUtc,
            ProfilePhotoReviewReason = "Previous Operations rejection",
            ProfilePhotoOperationsReviewActorId = "ops-actor-before-new-selfie",
            ProfilePhotoOperationsReviewUnionId = "legacy-review-union",
            ProfilePhotoOperationsReviewUnionName = "Legacy Review Union"
        };

        var response = await driver.PutAsJsonAsync("/api/v1/driver/onboarding/selfie",
            new { profilePhotoBase64 = "/9j/4AAQSkZJRg==" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = profiles["driver-selfie-preserve-latest"];
        Assert.Equal("/9j/4AAQSkZJRg==", updated.ProfilePhotoBase64);
        Assert.Equal("Pending", updated.ProfilePhotoApprovalStatus);
        Assert.Equal("Offline", updated.AvailabilityStatus);
        Assert.Equal(17.401, updated.CurrentLatitude);
        Assert.Equal(78.402, updated.CurrentLongitude);
        Assert.Equal(locationUpdatedAtUtc, updated.LocationUpdatedAtUtc);
        Assert.Null(updated.ProfilePhotoReviewedAtUtc);
        Assert.Null(updated.ProfilePhotoReviewReason);
        Assert.Null(updated.ProfilePhotoOperationsReviewActorId);
    }

    [Fact]
    public async Task DriverSelfieReplacementFailsClosedWhenOperationsReviewMetadataIsIncomplete()
    {
        using var factory = new TurnOvaApiFactory();
        const string driverId = "driver-selfie-incomplete-ops-review";
        using var driver = factory.CreateClient("Driver", driverId);
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles.TryRemove(driverId, out _);

        Assert.Equal(HttpStatusCode.Accepted,
            (await driver.PostAsJsonAsync("/api/v1/driver/onboarding", ValidOnboardingRequest())).StatusCode);
        var reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var incomplete = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoReviewedAtUtc = reviewedAt,
            ProfilePhotoReviewReason = "Previous Operations rejection",
            ProfilePhotoOperationsReviewActorId = "",
            ProfilePhotoOperationsReviewUnionId = "",
            ProfilePhotoOperationsReviewUnionName = ""
        };
        profiles[driverId] = incomplete;

        using var response = await driver.PutAsJsonAsync("/api/v1/driver/onboarding/selfie",
            new { profilePhotoBase64 = "/9j/4AAQSkZJRg==" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(incomplete, profiles[driverId]);
        Assert.DoesNotContain(
            factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>().Values,
            item => item.DriverId == driverId);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>().HasActiveAsync(driverId));
    }

    [Fact]
    public async Task OperationsSelfieRetryFailsClosedWhenReviewMarkerBelongsToAnotherUnion()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "ops-selfie-stale-union-driver";
        const string currentUnionId = "ops-selfie-current-union";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, currentUnionId);
        using var ops = factory.CreateClient("Ops", "ops-selfie-stale-union-operator");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var original = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ProfilePhotoReviewReason = "Image is unclear",
            ProfilePhotoOperationsReviewActorId = "original-ops-actor",
            ProfilePhotoOperationsReviewUnionId = "previous-union",
            ProfilePhotoOperationsReviewUnionName = "Previous Union"
        };
        profiles[driverId] = original;

        using var response = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{currentUnionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = "Image is unclear" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(original, profiles[driverId]);
        Assert.DoesNotContain(
            factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>().Values,
            item => item.DriverId == driverId);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>().HasActiveAsync(driverId));
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
    public async Task ConcurrentSelectedBookingAndDriverOfflineTransitionCannotBothSucceed()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "availability-race-rider");
        using var driver = factory.CreateClient("Driver", "availability-race-driver");
        SeedBookableDriver(factory, "availability-race-driver", "availability-race-union");

        using var bookingRequest = CreateSelectedBookingRequest(
            "availability-race-booking", "availability-race-driver", "availability-race-union",
            "KPHB", "HITEC City", []);
        var responses = await Task.WhenAll(
            rider.SendAsync(bookingRequest),
            driver.PostAsJsonAsync("/api/v1/driver/availability", new { isOnline = false }));
        using var booking = responses[0];
        using var offline = responses[1];

        var bookingSucceeded = booking.StatusCode == HttpStatusCode.Created;
        var offlineSucceeded = offline.StatusCode == HttpStatusCode.OK;
        Assert.NotEqual(bookingSucceeded, offlineSucceeded);

        var profile = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()
            ["availability-race-driver"];
        var reservation = await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync("availability-race-driver");
        if (bookingSucceeded)
        {
            Assert.Equal(HttpStatusCode.Conflict, offline.StatusCode);
            Assert.Equal("Online", profile.AvailabilityStatus);
            Assert.NotNull(reservation);
            Assert.StartsWith("RIDE-SEL-", reservation.RideId, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, booking.StatusCode);
            Assert.Equal("Offline", profile.AvailabilityStatus);
            Assert.Null(reservation);
        }
    }

    [Fact]
    public async Task ConcurrentSelectedBookingAndUnionTransferCannotBothSucceed()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "union-transfer-race-rider");
        using var ops = factory.CreateClient("Rider", "union-transfer-race-ops");
        const string driverId = "union-transfer-race-driver";
        const string originalUnionId = "union-transfer-race-original";
        const string targetUnionId = "union-transfer-race-target";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, originalUnionId);
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[targetUnionId] =
            new DriverUnion(targetUnionId, "Transfer Target Union", "TRANSFER-RACE", "Hyderabad",
                null, null, ["hyderabad-central"], ["cab"], "Active", now, now);

        using var bookingRequest = CreateSelectedBookingRequest(
            "union-transfer-race-booking", driverId, originalUnionId, "KPHB", "HITEC City", []);
        using var transferRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/ops/drivers/{driverId}/union-assignment")
        {
            Content = JsonContent.Create(new
            {
                unionId = targetUnionId,
                reason = "Concurrency regression test"
            })
        };
        transferRequest.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");

        var responses = await Task.WhenAll(
            rider.SendAsync(bookingRequest),
            ops.SendAsync(transferRequest));
        using var booking = responses[0];
        using var transfer = responses[1];

        var bookingSucceeded = booking.StatusCode == HttpStatusCode.Created;
        var transferSucceeded = transfer.StatusCode == HttpStatusCode.OK;
        Assert.NotEqual(bookingSucceeded, transferSucceeded);

        var assignment = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>()
            [driverId];
        var reservation = await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync(driverId);
        if (bookingSucceeded)
        {
            Assert.Equal(HttpStatusCode.Conflict, transfer.StatusCode);
            Assert.Equal(originalUnionId, assignment.UnionId);
            Assert.NotNull(reservation);
            Assert.StartsWith("RIDE-SEL-", reservation.RideId, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, booking.StatusCode);
            Assert.Equal(targetUnionId, assignment.UnionId);
            Assert.Null(reservation);
            var profile = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()
                [driverId];
            Assert.Equal("Pending", profile.ProfilePhotoApprovalStatus);
        }
    }

    [Fact]
    public async Task AcceptedSelectedRideBlocksAnotherBookingAndDriverAvailabilityChange()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var riderA = factory.CreateClient("Rider", "active-reservation-rider-a");
        using var riderB = factory.CreateClient("Rider", "active-reservation-rider-b");
        using var driver = factory.CreateClient("Driver", "active-reservation-driver");
        SeedBookableDriver(factory, "active-reservation-driver", "active-reservation-union");

        using var firstRequest = CreateSelectedBookingRequest(
            "active-reservation-booking-a", "active-reservation-driver", "active-reservation-union",
            "KPHB", "HITEC City", []);
        using var firstBooking = await riderA.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstBooking.StatusCode);
        using var firstJson = JsonDocument.Parse(await firstBooking.Content.ReadAsStringAsync());
        var rideId = firstJson.RootElement.GetProperty("rideId").GetString()!;

        using var proposal = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
        {
            Content = JsonContent.Create(new { amount = 420m, currency = "INR" })
        };
        proposal.Headers.Add("Idempotency-Key", "active-reservation-agreement");
        Assert.Equal(HttpStatusCode.Created, (await riderA.SendAsync(proposal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await riderA.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null)).StatusCode);
        using var accepted = await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var secondRequest = CreateSelectedBookingRequest(
            "active-reservation-booking-b", "active-reservation-driver", "active-reservation-union",
            "KPHB", "Madhapur", []);
        using var secondBooking = await riderB.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Conflict, secondBooking.StatusCode);
        using var offline = await driver.PostAsJsonAsync(
            "/api/v1/driver/availability", new { isOnline = false });
        Assert.Equal(HttpStatusCode.Conflict, offline.StatusCode);

        var profile = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()
            ["active-reservation-driver"];
        Assert.Equal("Online", profile.AvailabilityStatus);
        var reservation = await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync("active-reservation-driver");
        Assert.NotNull(reservation);
        Assert.Equal(rideId, reservation.RideId);
        Assert.Null(reservation.ExpiresAtUtc);
    }

    [Fact]
    public async Task OneRiderCannotCreateConcurrentSelectedBookingsForDifferentDrivers()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "single-active-rider");
        using var driverA = factory.CreateClient("Driver", "single-active-driver-a");
        using var driverB = factory.CreateClient("Driver", "single-active-driver-b");
        SeedBookableDriver(factory, "single-active-driver-a", "single-active-union-a");
        SeedBookableDriver(factory, "single-active-driver-b", "single-active-union-b");

        using var requestA = CreateSelectedBookingRequest(
            "single-active-key-a", "single-active-driver-a", "single-active-union-a", "KPHB", "HITEC City", []);
        using var requestB = CreateSelectedBookingRequest(
            "single-active-key-b", "single-active-driver-b", "single-active-union-b", "KPHB", "Madhapur", []);

        var responses = await Task.WhenAll(rider.SendAsync(requestA), rider.SendAsync(requestB));
        using var responseA = responses[0];
        using var responseB = responses[1];
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        var activeReservation = await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("single-active-rider");
        Assert.NotNull(activeReservation);
        var activeRides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()
            .Values.Where(ride => ride.RiderId == "single-active-rider" &&
                (ride.Status is "Negotiating" or "AgreementConfirmed" or "Accepted" or "Arrived" or "InProgress"))
            .ToArray();
        Assert.Single(activeRides);
        Assert.Equal(activeRides[0].RideId, activeReservation.RideId);
        var driverReservationStore = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        var driverLeaseResults = await Task.WhenAll(
            driverReservationStore.HasActiveAsync("single-active-driver-a"),
            driverReservationStore.HasActiveAsync("single-active-driver-b"));
        Assert.Single(driverLeaseResults, active => active);
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Matching")]
    [InlineData("Offered")]
    [InlineData("Negotiating")]
    [InlineData("AgreementConfirmed")]
    [InlineData("Accepted")]
    [InlineData("Arrived")]
    [InlineData("InProgress")]
    public async Task ExistingLegacyActiveRideWithoutRiderReservationBlocksSelectedBooking(string legacyStatus)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "legacy-active-rider");
        using var driver = factory.CreateClient("Driver", "legacy-active-new-driver");
        SeedBookableDriver(factory, "legacy-active-new-driver", "legacy-active-new-union");

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides["LEGACY-ACTIVE-RIDE"] = new DemoRide(
            "LEGACY-ACTIVE-RIDE", "KPHB", "HITEC City", 1, legacyStatus,
            DateTimeOffset.UtcNow.AddHours(-1), null, "legacy-active-rider", "legacy-active-driver",
            null, null, null, null, DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow.AddMinutes(15));

        using var request = CreateSelectedBookingRequest(
            "legacy-active-new-key", "legacy-active-new-driver", "legacy-active-new-union", "KPHB", "Madhapur", []);
        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("legacy-active-rider"));
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("legacy-active-new-driver"));
        Assert.Single(rides.Values.Where(ride => ride.RiderId == "legacy-active-rider"));
    }

    [Fact]
    public async Task DifferentRidersCanBookDifferentDriversConcurrently()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var riderA = factory.CreateClient("Rider", "independent-rider-a");
        using var riderB = factory.CreateClient("Rider", "independent-rider-b");
        using var driverA = factory.CreateClient("Driver", "independent-driver-a");
        using var driverB = factory.CreateClient("Driver", "independent-driver-b");
        SeedBookableDriver(factory, "independent-driver-a", "independent-union-a");
        SeedBookableDriver(factory, "independent-driver-b", "independent-union-b");

        using var requestA = CreateSelectedBookingRequest(
            "independent-key-a", "independent-driver-a", "independent-union-a", "KPHB", "HITEC City", []);
        using var requestB = CreateSelectedBookingRequest(
            "independent-key-b", "independent-driver-b", "independent-union-b", "KPHB", "Madhapur", []);
        var responses = await Task.WhenAll(riderA.SendAsync(requestA), riderB.SendAsync(requestB));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        using var jsonA = JsonDocument.Parse(await responses[0].Content.ReadAsStringAsync());
        using var jsonB = JsonDocument.Parse(await responses[1].Content.ReadAsStringAsync());
        Assert.NotEqual(
            jsonA.RootElement.GetProperty("rideId").GetString(),
            jsonB.RootElement.GetProperty("rideId").GetString());
        Assert.NotNull(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("independent-rider-a"));
        Assert.NotNull(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("independent-rider-b"));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task CancellingSelectedBookingReleasesRiderReservationAndAllowsAnotherDriver()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "rider-release-booking");
        using var driverA = factory.CreateClient("Driver", "rider-release-driver-a");
        using var driverB = factory.CreateClient("Driver", "rider-release-driver-b");
        SeedBookableDriver(factory, "rider-release-driver-a", "rider-release-union-a");
        SeedBookableDriver(factory, "rider-release-driver-b", "rider-release-union-b");

        using var firstRequest = CreateSelectedBookingRequest(
            "rider-release-key-a", "rider-release-driver-a", "rider-release-union-a", "KPHB", "HITEC City", []);
        using var firstResponse = await rider.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var firstRideId = firstJson.RootElement.GetProperty("rideId").GetString()!;

        Assert.Equal(firstRideId,
            (await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
                .GetActiveAsync("rider-release-booking"))?.RideId);
        Assert.Equal(HttpStatusCode.OK, (await rider.PostAsync($"/api/v1/rides/{firstRideId}/cancel", null)).StatusCode);
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("rider-release-booking"));

        using var secondRequest = CreateSelectedBookingRequest(
            "rider-release-key-b", "rider-release-driver-b", "rider-release-union-b", "KPHB", "Madhapur", []);
        using var secondResponse = await rider.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.NotEqual(firstRideId, secondJson.RootElement.GetProperty("rideId").GetString());
        Assert.Equal(secondJson.RootElement.GetProperty("rideId").GetString(),
            (await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
                .GetActiveAsync("rider-release-booking"))?.RideId);
    }

    [Fact]
    public async Task AcceptedSelectedBookingPromotesRiderReservationAndBlocksAnotherDriver()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "rider-permanent-booking");
        using var driverA = factory.CreateClient("Driver", "rider-permanent-driver-a");
        using var driverB = factory.CreateClient("Driver", "rider-permanent-driver-b");
        SeedBookableDriver(factory, "rider-permanent-driver-a", "rider-permanent-union-a");
        SeedBookableDriver(factory, "rider-permanent-driver-b", "rider-permanent-union-b");

        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driverA, "rider-permanent-driver-a", "rider-permanent-union-a",
            "rider-permanent-key-a", 420m);
        Assert.Equal(HttpStatusCode.OK, (await driverA.PostAsync($"/api/v1/rides/{rideId}/accept", null)).StatusCode);

        var reservation = await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("rider-permanent-booking");
        Assert.Equal(rideId, reservation?.RideId);
        Assert.Null(reservation?.ExpiresAtUtc);

        using var replayRequest = CreateSelectedBookingRequest(
            "rider-permanent-key-a", "rider-permanent-driver-a", "rider-permanent-union-a", "KPHB", "HITEC City", []);
        using var replayResponse = await rider.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        using var replayJson = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync());
        Assert.Equal(rideId, replayJson.RootElement.GetProperty("rideId").GetString());

        using var secondRequest = CreateSelectedBookingRequest(
            "rider-permanent-key-b", "rider-permanent-driver-b", "rider-permanent-union-b", "KPHB", "Madhapur", []);
        using var secondResponse = await rider.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(rideId, (await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("rider-permanent-booking"))?.RideId);
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Matching")]
    [InlineData("Offered")]
    [InlineData("Negotiating")]
    [InlineData("AgreementConfirmed")]
    [InlineData("Accepted")]
    [InlineData("Arrived")]
    [InlineData("InProgress")]
    public async Task DriverAcceptanceRejectsPreExistingLegacyActiveRideForSameRider(string legacyStatus)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "rider-legacy-active-accept");
        using var driver = factory.CreateClient("Driver", "rider-legacy-active-driver");
        SeedBookableDriver(factory, "rider-legacy-active-driver", "rider-legacy-active-union");

        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, "rider-legacy-active-driver", "rider-legacy-active-union",
            "rider-legacy-active-key", 410m);
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides["LEGACY-ACTIVE-DURING-NEGOTIATION"] = new DemoRide(
            "LEGACY-ACTIVE-DURING-NEGOTIATION", "KPHB", "HITEC City", 1, legacyStatus,
            DateTimeOffset.UtcNow.AddHours(-1), null, "rider-legacy-active-accept", "legacy-driver",
            null, null, null, null, DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow.AddMinutes(15));

        using var accept = await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        Assert.Equal("AgreementConfirmed", rides[rideId].Status);
        var riderReservation = await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("rider-legacy-active-accept");
        Assert.Equal(rideId, riderReservation?.RideId);
        Assert.NotNull(riderReservation?.ExpiresAtUtc);
        var driverReservation = await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync("rider-legacy-active-driver");
        Assert.NotNull(driverReservation?.ExpiresAtUtc);
    }

    [Fact]
    public async Task CompletedSelectedRideReleasesRiderReservation()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "rider-completed-booking");
        using var driver = factory.CreateClient("Driver", "rider-completed-driver");
        SeedBookableDriver(factory, "rider-completed-driver", "rider-completed-union");

        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, "rider-completed-driver", "rider-completed-union",
            "rider-completed-key", 390m);

        Assert.Equal("Completed", factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId].Status);
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("rider-completed-booking"));
    }

    [Fact]
    public async Task ExpiredSelectedNegotiationReleasesRiderReservationBeforeNextBooking()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "rider-expiry-booking");
        using var driverA = factory.CreateClient("Driver", "rider-expiry-driver-a");
        using var driverB = factory.CreateClient("Driver", "rider-expiry-driver-b");
        SeedBookableDriver(factory, "rider-expiry-driver-a", "rider-expiry-union-a");
        SeedBookableDriver(factory, "rider-expiry-driver-b", "rider-expiry-union-b");

        using var firstRequest = CreateSelectedBookingRequest(
            "rider-expiry-key-a", "rider-expiry-driver-a", "rider-expiry-union-a", "KPHB", "HITEC City", []);
        using var firstResponse = await rider.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var firstRideId = firstJson.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[firstRideId] = rides[firstRideId] with
        {
            NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        using var secondRequest = CreateSelectedBookingRequest(
            "rider-expiry-key-b", "rider-expiry-driver-b", "rider-expiry-union-b", "KPHB", "Madhapur", []);
        using var secondResponse = await rider.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal("Expired", rides[firstRideId].Status);
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.Equal(secondJson.RootElement.GetProperty("rideId").GetString(),
            (await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
                .GetActiveAsync("rider-expiry-booking"))?.RideId);
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


    [Theory]
    [InlineData("91+9876543210")]
    [InlineData("++919876543210")]
    [InlineData("+91+9876543210")]
    [InlineData("+91987654321A")]
    [InlineData("+91 9876543210")]
    [InlineData("+919876543")]
    [InlineData("1234567890123456")]
    [InlineData("+1234567890123456")]
    [InlineData("+٩١٩٨٧٦٥٤٣٢١٠")]
    public async Task DriverOnboardingRejectsMalformedPhoneNumbersWithoutCreatingProfile(string phoneNumber)
    {
        using var factory = new TurnOvaApiFactory();
        const string driverId = "driver-onboarding-malformed-phone";
        using var driver = factory.CreateClient("Driver", driverId);
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles.TryRemove(driverId, out _);

        using var response = await driver.PostAsJsonAsync(
            "/api/v1/driver/onboarding", ValidOnboardingRequest(phoneNumber: phoneNumber));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(profiles.ContainsKey(driverId));
    }


    [Theory]
    [InlineData("1234567890")]
    [InlineData("123456789012345")]
    [InlineData("+1234567890")]
    [InlineData("+123456789012345")]
    public async Task DriverOnboardingAcceptsPhoneNumbersAtBothE164LengthBoundaries(string phoneNumber)
    {
        using var factory = new TurnOvaApiFactory();
        const string driverId = "driver-onboarding-phone-boundary";
        using var driver = factory.CreateClient("Driver", driverId);
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles.TryRemove(driverId, out _);

        using var response = await driver.PostAsJsonAsync(
            "/api/v1/driver/onboarding", ValidOnboardingRequest(phoneNumber: phoneNumber));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var profile = profiles[driverId];
        Assert.Equal(phoneNumber, profile.PhoneNumber);
    }


    [Fact]
    public async Task DriverOnboardingRejectsMalformedPhoneNumberWithoutReplacingExistingProfile()
    {
        using var factory = new TurnOvaApiFactory();
        const string driverId = "driver-onboarding-phone-preserve";
        using var driver = factory.CreateClient("Driver", driverId);
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles.TryRemove(driverId, out _);

        Assert.Equal(HttpStatusCode.Accepted,
            (await driver.PostAsJsonAsync("/api/v1/driver/onboarding", ValidOnboardingRequest())).StatusCode);
        var before = profiles[driverId];

        using var response = await driver.PostAsJsonAsync(
            "/api/v1/driver/onboarding",
            ValidOnboardingRequest(phoneNumber: "+91987654321A"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, profiles[driverId]);
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
    public async Task OperationsRejectionAfterUnionManagerRejectionCreatesItsOwnReviewAndAudit()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "union-then-operations-review-driver";
        const string unionId = "union-then-operations-review-union";
        using var driver = factory.CreateClient("Driver", driverId);
        using var ops = factory.CreateClient("Ops", "ops-reviewer-after-union");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var manager = factory.CreateUnionManagerClient(unionId, "union-manager-rejector");

        SeedBookableDriver(factory, driverId, unionId);
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Pending",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = null,
            ProfilePhotoReviewReason = null,
            ProfilePhotoOperationsReviewActorId = null
        };

        const string reason = "Image is unclear";
        using var unionRejection = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason });
        Assert.Equal(HttpStatusCode.OK, unionRejection.StatusCode);
        var unionReviewedAt = profiles[driverId].ProfilePhotoReviewedAtUtc;
        Assert.NotNull(unionReviewedAt);
        Assert.Null(profiles[driverId].ProfilePhotoOperationsReviewActorId);

        using var operationsRejection = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason });
        Assert.Equal(HttpStatusCode.OK, operationsRejection.StatusCode);

        var operationsReviewedAt = profiles[driverId].ProfilePhotoReviewedAtUtc;
        Assert.NotNull(operationsReviewedAt);
        Assert.True(operationsReviewedAt.Value > unionReviewedAt.Value);
        Assert.Equal("ops-reviewer-after-union", profiles[driverId].ProfilePhotoOperationsReviewActorId);

        var auditEvents = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>()
            .Values.Where(item => item.DriverId == driverId).ToArray();
        Assert.Contains(auditEvents, item =>
            item.EventType == "SelfieRejectedByUnion" &&
            item.Actor == "union-manager-rejector" &&
            item.OccurredAtUtc == unionReviewedAt);
        Assert.Contains(auditEvents, item =>
            item.EventType == "SelfieRejectedByOperations" &&
            item.Actor == "ops-reviewer-after-union" &&
            item.OccurredAtUtc == operationsReviewedAt);

        var firstOperationsEventId = auditEvents.Single(item =>
            item.EventType == "SelfieRejectedByOperations").EventId;
        using var unionApproval = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, unionApproval.StatusCode);
        Assert.Null(profiles[driverId].ProfilePhotoOperationsReviewActorId);

        await Task.Delay(10);
        using var secondOperationsRejection = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason });
        Assert.Equal(HttpStatusCode.OK, secondOperationsRejection.StatusCode);

        var operationsEvents = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>()
            .Values.Where(item => item.DriverId == driverId && item.EventType == "SelfieRejectedByOperations")
            .OrderBy(item => item.OccurredAtUtc).ToArray();
        Assert.Equal(2, operationsEvents.Length);
        Assert.Equal(firstOperationsEventId, operationsEvents[0].EventId);
        Assert.NotEqual(operationsEvents[0].EventId, operationsEvents[1].EventId);
        Assert.True(operationsEvents[1].OccurredAtUtc > operationsEvents[0].OccurredAtUtc);
    }

    [Fact]
    public async Task OperationsSelfieReviewRespectsAnExistingDriverReservationLease()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "ops-selfie-lease-driver";
        const string unionId = "ops-selfie-lease-union";
        using var driver = factory.CreateClient("Driver", driverId);
        using var ops = factory.CreateClient("Ops", "ops-selfie-lease-operator");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        SeedBookableDriver(factory, driverId, unionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.True(await reservations.TryAcquireTemporaryAsync(
            driverId, "ops-selfie-review-existing-lease", DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None));

        using var blockedReview = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = "Image is unclear" });
        Assert.Equal(HttpStatusCode.Conflict, blockedReview.StatusCode);
        Assert.Equal("Approved", profiles[driverId].ProfilePhotoApprovalStatus);
        Assert.Null(profiles[driverId].ProfilePhotoOperationsReviewActorId);

        Assert.True(await reservations.TryReleaseAsync(
            driverId, "ops-selfie-review-existing-lease", CancellationToken.None));
        using var review = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = "Image is unclear" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Equal("Rejected", profiles[driverId].ProfilePhotoApprovalStatus);
        Assert.Equal("ops-selfie-lease-operator", profiles[driverId].ProfilePhotoOperationsReviewActorId);
    }

    [Fact]
    public async Task UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var driver = factory.CreateClient("Driver", "union-review-driver");
        using var rider = factory.CreateClient("Rider", "union-review-rider");
        using var ops = factory.CreateClient("Ops", "union-review-ops");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");

        const string unionId = "union-review-union";
        const string driverId = "union-review-driver";
        SeedBookableDriver(factory, driverId, unionId);
        var unions = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>();
        unions[unionId] = unions[unionId] with { Name = "Union Review Union" };
        var assignments = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>();
        assignments[driverId] = assignments[driverId] with { UnionName = "Union Review Union" };

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Pending",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = null,
            ProfilePhotoReviewReason = null
        };

        using var manager = factory.CreateUnionManagerClient(unionId, "union-manager-review");
        using var retryManager = factory.CreateUnionManagerClient(unionId, "union-manager-review-retry");
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
        using var repeatedApproval = await retryManager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review", new { status = "Approved", reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, repeatedApproval.StatusCode);
        Assert.Equal("Approved", profiles[driverId].ProfilePhotoApprovalStatus);
        Assert.Equal(unionId, profiles[driverId].ProfilePhotoApprovedByUnionId);
        var unionAudit = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
        var approvalAudit = Assert.Single(unionAudit.Values.Where(item =>
            item.DriverId == driverId && item.EventType == "SelfieApprovedByUnion"));
        Assert.Equal("union-manager-review", approvalAudit.Actor);

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

        var auditBeforeInvalidRejections = await ops.GetFromJsonAsync<List<JsonElement>>(
            $"/api/v1/ops/drivers/{driverId}/union-history");
        foreach (var invalidReason in new string?[] { null, "   ", new string('x', 301) })
        {
            using var invalidRejection = await ops.PutAsJsonAsync(
                $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
                new { status = "Rejected", reason = invalidReason });
            Assert.Equal(HttpStatusCode.BadRequest, invalidRejection.StatusCode);
        }
        Assert.Equal("Approved", profiles[driverId].ProfilePhotoApprovalStatus);
        var auditAfterInvalidRejections = await ops.GetFromJsonAsync<List<JsonElement>>(
            $"/api/v1/ops/drivers/{driverId}/union-history");
        Assert.Equal(auditBeforeInvalidRejections!.Count, auditAfterInvalidRejections!.Count);

        const string expectedRejectionReason = "Vehicle image requires a clearer review photo.";
        using var operationsRejection = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = "  Vehicle image requires a clearer review photo.  " });
        Assert.Equal(HttpStatusCode.OK, operationsRejection.StatusCode);
        using var rejectionResponseJson = JsonDocument.Parse(await operationsRejection.Content.ReadAsStringAsync());
        Assert.Equal(expectedRejectionReason, rejectionResponseJson.RootElement.GetProperty("reason").GetString());
        Assert.Equal("Rejected", profiles[driverId].ProfilePhotoApprovalStatus);
        Assert.Equal(expectedRejectionReason, profiles[driverId].ProfilePhotoReviewReason);
        Assert.Equal("union-review-ops", profiles[driverId].ProfilePhotoOperationsReviewActorId);
        Assert.Equal(unionId, profiles[driverId].ProfilePhotoOperationsReviewUnionId);
        Assert.Equal("Union Review Union", profiles[driverId].ProfilePhotoOperationsReviewUnionName);

        using var driverProfileResponse = await driver.GetAsync("/api/v1/driver/onboarding/me");
        Assert.Equal(HttpStatusCode.OK, driverProfileResponse.StatusCode);
        using var driverProfileJson = JsonDocument.Parse(await driverProfileResponse.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null,
            driverProfileJson.RootElement.GetProperty("profilePhotoOperationsReviewActorId").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            driverProfileJson.RootElement.GetProperty("profilePhotoOperationsReviewUnionId").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            driverProfileJson.RootElement.GetProperty("profilePhotoOperationsReviewUnionName").ValueKind);

        using var discoveryAfterRejection = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        using var rejectedJson = JsonDocument.Parse(await discoveryAfterRejection.Content.ReadAsStringAsync());
        var rejectedDriver = rejectedJson.RootElement.GetProperty("unions").EnumerateArray()
            .Single(group => group.GetProperty("unionId").GetString() == unionId)
            .GetProperty("drivers").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, rejectedDriver.GetProperty("profilePhotoBase64").ValueKind);

        using var rejectedAuditResponse = await ops.GetAsync($"/api/v1/ops/drivers/{driverId}/union-history");
        using var rejectedAuditJson = JsonDocument.Parse(await rejectedAuditResponse.Content.ReadAsStringAsync());
        var rejectionAuditEvent = Assert.Single(rejectedAuditJson.RootElement.EnumerateArray(),
            item => item.GetProperty("eventType").GetString() == "SelfieRejectedByOperations");
        Assert.Equal(expectedRejectionReason, rejectionAuditEvent.GetProperty("reason").GetString());
        Assert.Equal("union-review-ops", rejectionAuditEvent.GetProperty("actor").GetString());

        var originalEventId = rejectionAuditEvent.GetProperty("eventId").GetString()!;
        var originalReviewedAt = profiles[driverId].ProfilePhotoReviewedAtUtc;
        Assert.NotNull(originalReviewedAt);
        var auditStore = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
        Assert.True(auditStore.TryRemove($"{driverId}|{originalEventId}", out _));

        // The profile commit succeeded but the audit write is missing. An identical
        // retry must restore the deterministic event without changing the review time.
        using var retryingOps = factory.CreateClient("Ops", "union-review-ops-retry");
        retryingOps.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var recoveredRejection = await retryingOps.PutAsJsonAsync(
            $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = "  Vehicle image requires a clearer review photo.  " });
        Assert.Equal(HttpStatusCode.OK, recoveredRejection.StatusCode);
        Assert.Equal(originalReviewedAt, profiles[driverId].ProfilePhotoReviewedAtUtc);

        using var recoveredAuditResponse = await ops.GetAsync($"/api/v1/ops/drivers/{driverId}/union-history");
        using var recoveredAuditJson = JsonDocument.Parse(await recoveredAuditResponse.Content.ReadAsStringAsync());
        var recoveredAuditEvent = Assert.Single(recoveredAuditJson.RootElement.EnumerateArray(),
            item => item.GetProperty("eventType").GetString() == "SelfieRejectedByOperations");
        Assert.Equal(originalEventId, recoveredAuditEvent.GetProperty("eventId").GetString());
        Assert.Equal(originalReviewedAt.Value, recoveredAuditEvent.GetProperty("occurredAtUtc").GetDateTimeOffset());
        Assert.Equal("union-review-ops", recoveredAuditEvent.GetProperty("actor").GetString());

        // A further identical retry must verify the canonical event rather than
        // append a duplicate or move the authoritative review timestamp.
        using var duplicateRejection = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = expectedRejectionReason });
        Assert.Equal(HttpStatusCode.OK, duplicateRejection.StatusCode);
        Assert.Equal(originalReviewedAt, profiles[driverId].ProfilePhotoReviewedAtUtc);
        var finalAudit = await ops.GetFromJsonAsync<List<JsonElement>>(
            $"/api/v1/ops/drivers/{driverId}/union-history");
        Assert.Single(finalAudit!, item =>
            item.GetProperty("eventType").GetString() == "SelfieRejectedByOperations" &&
            item.GetProperty("eventId").GetString() == originalEventId);
    }

    [Fact]
    public async Task UnionManagerSelfieAuditRetryReconstructsMissingEventWithOriginalActor()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string unionId = "union-review-recovery-union";
        const string driverId = "union-review-recovery-driver";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Pending",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = null,
            ProfilePhotoReviewReason = null
        };

        using var originalManager = factory.CreateUnionManagerClient(unionId, "union-manager-original-actor");
        using var retryManager = factory.CreateUnionManagerClient(unionId, "union-manager-retry-actor");
        var audit = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
        const string reason = "Image is unclear";

        using var firstReview = await originalManager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason });
        Assert.Equal(HttpStatusCode.OK, firstReview.StatusCode);

        var reviewedProfile = profiles[driverId];
        Assert.Equal("union-manager-original-actor", reviewedProfile.ProfilePhotoUnionReviewActorId);
        Assert.Equal(unionId, reviewedProfile.ProfilePhotoUnionReviewUnionId);
        Assert.NotNull(reviewedProfile.ProfilePhotoReviewedAtUtc);
        var originalEvent = Assert.Single(audit.Values, item =>
            item.DriverId == driverId && item.EventType == "SelfieRejectedByUnion");
        Assert.Equal("union-manager-original-actor", originalEvent.Actor);
        Assert.True(audit.TryRemove($"{driverId}|{originalEvent.EventId}", out _));

        using var retryReview = await retryManager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason });
        Assert.Equal(HttpStatusCode.OK, retryReview.StatusCode);

        var recoveredEvent = Assert.Single(audit.Values, item =>
            item.DriverId == driverId && item.EventType == "SelfieRejectedByUnion");
        Assert.Equal(originalEvent.EventId, recoveredEvent.EventId);
        Assert.Equal("union-manager-original-actor", recoveredEvent.Actor);
        Assert.Equal(reviewedProfile.ProfilePhotoReviewedAtUtc, recoveredEvent.OccurredAtUtc);
        Assert.Equal(reviewedProfile.ProfilePhotoReviewedAtUtc, profiles[driverId].ProfilePhotoReviewedAtUtc);

        var driverProfile = await driver.GetFromJsonAsync<DriverOnboardingProfile>("/api/v1/driver/onboarding/me");
        Assert.NotNull(driverProfile);
        Assert.Null(driverProfile.ProfilePhotoUnionReviewActorId);
        Assert.Null(driverProfile.ProfilePhotoUnionReviewUnionId);
        Assert.Null(driverProfile.ProfilePhotoUnionReviewUnionName);
    }

    [Fact]
    public async Task UnionManagerReviewFailsClosedForLegacyReviewedProfileWithoutActorSnapshot()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string unionId = "union-review-legacy-union";
        const string driverId = "union-review-legacy-driver";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);
        var reviewedAt = DateTimeOffset.Parse("2026-11-12T10:00:00Z");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Rejected",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = reviewedAt,
            ProfilePhotoReviewReason = "Image is unclear"
        };

        using var manager = factory.CreateUnionManagerClient(unionId, "union-manager-legacy-retry");
        using var retry = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = "Image is unclear" });

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(reviewedAt, profiles[driverId].ProfilePhotoReviewedAtUtc);
        Assert.Null(profiles[driverId].ProfilePhotoUnionReviewActorId);
        Assert.DoesNotContain(
            factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>().Values,
            item => item.DriverId == driverId);
    }

    [Fact]
    public async Task UnionTransferRepairsMissingUnionManagerSelfieAuditBeforeClearingReviewSnapshot()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "union-review-transfer-recovery-driver";
        const string oldUnionId = "union-review-transfer-recovery-old";
        const string newUnionId = "union-review-transfer-recovery-new";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, oldUnionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Pending",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = null,
            ProfilePhotoReviewReason = null
        };
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[newUnionId] =
            new DriverUnion(newUnionId, "New Recovery Union", "UNION-REVIEW-RECOVERY-NEW",
                "Hyderabad", null, null, ["hyderabad-central"], ["cab"], "Active", now, now);

        using var manager = factory.CreateUnionManagerClient(oldUnionId, "union-manager-transfer-original");
        using var ops = factory.CreateClient("Ops", "ops-transfer-recovery");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        var audit = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
        const string reason = "Image is unclear";

        using var review = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var originalEvent = Assert.Single(audit.Values, item =>
            item.DriverId == driverId && item.EventType == "SelfieRejectedByUnion");
        Assert.Equal("union-manager-transfer-original", originalEvent.Actor);
        Assert.True(audit.TryRemove($"{driverId}|{originalEvent.EventId}", out _));

        using var transfer = await ops.PostAsJsonAsync(
            $"/api/v1/ops/drivers/{driverId}/union-assignment",
            new { unionId = newUnionId, reason = "Approved membership transfer" });
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

        var recoveredEvent = Assert.Single(audit.Values, item =>
            item.DriverId == driverId && item.EventType == "SelfieRejectedByUnion");
        Assert.Equal(originalEvent.EventId, recoveredEvent.EventId);
        Assert.Equal("union-manager-transfer-original", recoveredEvent.Actor);
        Assert.Null(profiles[driverId].ProfilePhotoUnionReviewActorId);
        Assert.Null(profiles[driverId].ProfilePhotoUnionReviewUnionId);
        Assert.Null(profiles[driverId].ProfilePhotoUnionReviewUnionName);
    }

    [Fact]
    public async Task UnionTransferRetryAndHistoryReadRepairMissingAssignmentAuditWithoutExposingSnapshotToDriver()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "assignment-audit-recovery-driver";
        const string oldUnionId = "assignment-audit-recovery-old";
        const string newUnionId = "assignment-audit-recovery-new";
        using var driver = factory.CreateClient("Driver", driverId);
        using var ops = factory.CreateClient("Ops", "assignment-audit-ops");
        ops.DefaultRequestHeaders.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        SeedBookableDriver(factory, driverId, oldUnionId);
        SeedTransferTargetUnion(factory, newUnionId);

        var assignments = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>();
        var audit = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
        const string reason = "Approved membership transfer";

        using var firstTransfer = await ops.PostAsJsonAsync(
            $"/api/v1/ops/drivers/{driverId}/union-assignment",
            new { unionId = newUnionId, reason });
        Assert.Equal(HttpStatusCode.OK, firstTransfer.StatusCode);

        var originalAssignment = assignments[driverId];
        var expectedEvent = DriverUnionAssignmentAuditSnapshot.CreateExpectedEvent(originalAssignment);
        Assert.NotNull(expectedEvent);
        Assert.Equal(oldUnionId, originalAssignment.AuditPreviousUnionId);
        Assert.Equal("assignment-audit-ops", originalAssignment.AuditActor);
        var originalEvent = Assert.Single(audit.Values, item => item.EventId == originalAssignment.AuditEventId);
        Assert.Equal(expectedEvent, originalEvent);

        Assert.True(audit.TryRemove($"{driverId}|{originalEvent.EventId}", out _));
        using var retryTransfer = await ops.PostAsJsonAsync(
            $"/api/v1/ops/drivers/{driverId}/union-assignment",
            new { unionId = newUnionId, reason });
        Assert.Equal(HttpStatusCode.OK, retryTransfer.StatusCode);
        var retryAssignment = await retryTransfer.Content.ReadFromJsonAsync<DriverUnionAssignment>();
        Assert.NotNull(retryAssignment);
        Assert.Equal(originalAssignment.AuditEventId, retryAssignment.AuditEventId);
        Assert.Equal(originalAssignment.AuditOccurredAtUtc, retryAssignment.AuditOccurredAtUtc);

        var retryEvent = Assert.Single(audit.Values, item => item.EventId == originalAssignment.AuditEventId);
        Assert.Equal(originalEvent, retryEvent);

        Assert.True(audit.TryRemove($"{driverId}|{originalEvent.EventId}", out _));
        var history = await ops.GetFromJsonAsync<DriverUnionAuditEvent[]>(
            $"/api/v1/ops/drivers/{driverId}/union-history");
        Assert.NotNull(history);
        var historyEvent = Assert.Single(history, item => item.EventId == originalAssignment.AuditEventId);
        Assert.Equal(originalEvent, historyEvent);

        var driverAssignment = await driver.GetFromJsonAsync<DriverUnionAssignment>("/api/v1/driver/union");
        Assert.NotNull(driverAssignment);
        Assert.Null(driverAssignment.AuditEventId);
        Assert.Null(driverAssignment.AuditEventType);
        Assert.Null(driverAssignment.AuditActor);
        Assert.Null(driverAssignment.AuditPreviousUnionId);
        Assert.Null(driverAssignment.AuditPreviousUnionName);
        Assert.Null(driverAssignment.AuditOccurredAtUtc);
    }

    [Fact]
    public async Task UnionManagerSelfieReviewReconcilesExpiredOrphanReservationBeforeAcquiringReviewLease()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string unionId = "union-review-orphan-union";
        const string driverId = "union-review-orphan-driver";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Pending",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = null,
            ProfilePhotoReviewReason = null
        };

        using var manager = factory.CreateUnionManagerClient(unionId, "union-manager-orphan-review");
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.True(await reservations.TryAcquireTemporaryAsync(
            driverId, "union-review-expired-orphan", DateTimeOffset.UtcNow.AddMilliseconds(100), CancellationToken.None));

        // The in-memory store deliberately keeps expired leases visible until the API
        // reconciles the linked ride (or confirms that this lease is an expired orphan).
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var expiredReservation = await reservations.GetActiveAsync(driverId, CancellationToken.None);
        Assert.NotNull(expiredReservation);
        Assert.True(expiredReservation!.ExpiresAtUtc is { } expiredAtUtc && expiredAtUtc <= DateTimeOffset.UtcNow);

        using var review = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Approved", reason = (string?)null });

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Equal("Approved", profiles[driverId].ProfilePhotoApprovalStatus);
        Assert.Null(await reservations.GetActiveAsync(driverId, CancellationToken.None));
    }

    [Fact]
    public async Task UnionManagerSelfieReviewKeepsExpiredReservationForActiveNegotiation()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string unionId = "union-review-active-union";
        const string driverId = "union-review-active-driver";
        const string rideId = "union-review-active-ride";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            ProfilePhotoApprovalStatus = "Pending",
            ProfilePhotoApprovedByUnionId = null,
            ProfilePhotoReviewedAtUtc = null,
            ProfilePhotoReviewReason = null
        };

        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId] =
            new DemoRide(rideId, "KPHB", "HITEC City", 1, "Negotiating", now, null,
                "union-review-active-rider", driverId, 17.493, 78.399, null, null, null, null,
                false, "cab", "hyderabad-central", UnionId: unionId,
                NegotiationExpiresAtUtc: now.AddMinutes(10));

        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.True(await reservations.TryAcquireTemporaryAsync(
            driverId, rideId, DateTimeOffset.UtcNow.AddMilliseconds(100), CancellationToken.None));
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        using var manager = factory.CreateUnionManagerClient(unionId, "union-manager-active-review");
        using var review = await manager.PutAsJsonAsync(
            $"/api/v1/union/drivers/{driverId}/selfie-review",
            new { status = "Approved", reason = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, review.StatusCode);
        Assert.Equal(rideId, (await reservations.GetActiveAsync(driverId, CancellationToken.None))?.RideId);
        Assert.Equal("Pending", profiles[driverId].ProfilePhotoApprovalStatus);
    }

    [Fact]
    public async Task UnionTransferResetsSelfieApprovalUntilTheNewUnionApprovesIt()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var driver = factory.CreateClient("Driver", "transfer-selfie-driver");
        using var rider = factory.CreateClient("Rider", "transfer-selfie-rider");
        using var ops = factory.CreateClient("Ops", "transfer-selfie-ops@example.com");
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

        using var operationsRejection = await ops.PutAsJsonAsync(
            $"/api/v1/ops/unions/{oldUnionId}/drivers/{driverId}/selfie-review",
            new { status = "Rejected", reason = "Operations review before transfer" });
        Assert.Equal(HttpStatusCode.OK, operationsRejection.StatusCode);
        var pendingOperationsReview = profiles[driverId];
        var auditStore = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
        var pendingAudit = auditStore.Values.Single(item =>
            item.DriverId == driverId && item.EventType == "SelfieRejectedByOperations");
        Assert.Equal("Operations", pendingAudit.Actor);
        Assert.Equal(oldUnionId, pendingOperationsReview.ProfilePhotoOperationsReviewUnionId);
        Assert.True(auditStore.TryRemove(driverId + "|" + pendingAudit.EventId, out _));

        using var transfer = await ops.PostAsJsonAsync(
            $"/api/v1/ops/drivers/{driverId}/union-assignment",
            new { unionId = newUnionId, reason = "Driver moved to the new Cab union." });
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);
        var transferAudit = Assert.Single(auditStore.Values.Where(item =>
            item.DriverId == driverId && item.EventType == "Transferred"));
        Assert.Equal("Operations", transferAudit.Actor);

        var transferredProfile = profiles[driverId];
        Assert.Equal("Pending", transferredProfile.ProfilePhotoApprovalStatus);
        Assert.Null(transferredProfile.ProfilePhotoApprovedByUnionId);
        Assert.Null(transferredProfile.ProfilePhotoReviewedAtUtc);
        Assert.Null(transferredProfile.ProfilePhotoReviewReason);
        Assert.Null(transferredProfile.ProfilePhotoOperationsReviewActorId);
        Assert.Null(transferredProfile.ProfilePhotoOperationsReviewUnionId);
        Assert.Null(transferredProfile.ProfilePhotoOperationsReviewUnionName);
        var recoveredTransferAudit = Assert.Single(auditStore.Values, item =>
            item.DriverId == driverId && item.EventId == pendingAudit.EventId);
        Assert.Equal(pendingAudit.Actor, recoveredTransferAudit.Actor);
        Assert.Equal(pendingAudit.OccurredAtUtc, recoveredTransferAudit.OccurredAtUtc);

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

    private static object ValidOnboardingRequest(
        string serviceClass = "cab",
        string phoneNumber = "+919876543210") => new
    {
        profilePhotoBase64 = "/9j/2Q==",
        zoneId = "hyderabad-central",
        fullName = "Test Driver",
        phoneNumber,
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
    public async Task PayslipEndpointReturns422ForUnsupportedScriptWithoutLeakingDriverName()
    {
        using var factory = new TurnOvaApiFactory();
        const string driverId = "driver-payslip-unsupported-script";
        const string unionId = "union-payslip-unsupported-script";
        using var driver = factory.CreateClient("Driver", driverId);

        SeedBookableDriver(factory, driverId, unionId);
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with { FullName = "Driver محمد" };

        var assignmentEvent = new DriverUnionAuditEvent(
            "assignment-unsupported-script", driverId, null, null,
            unionId, "Selected Booking Union", "Assigned", "Initial assignment", "Operations",
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>()
            [$"{driverId}|{assignmentEvent.EventId}"] = assignmentEvent;

        using var response = await driver.GetAsync("/api/v1/driver/payslips/2026/10.pdf");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not supported", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("محمد", body, StringComparison.Ordinal);
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

        var pdfBytes = await response.Content.ReadAsByteArrayAsync();
        var pdfHeader = Encoding.ASCII.GetString(pdfBytes);
        Assert.StartsWith("%PDF-1.4", pdfHeader);
        Assert.EndsWith("%%EOF", pdfHeader.TrimEnd());
        Assert.True(pdfBytes.Length > 5_000);
        using (var pdfStream = new MemoryStream(pdfBytes))
        using (var document = PdfDocument.Open(pdfStream))
        {
            var extracted = string.Join("\\n", document.GetPages().Select(page => page.Text));
            Assert.Contains("Central Drivers Union", extracted, StringComparison.Ordinal);
            Assert.Contains("Total days worked: 2", extracted, StringComparison.Ordinal);
            Assert.Contains("Rides accepted: 2", extracted, StringComparison.Ordinal);
            Assert.Contains("Rides completed: 2", extracted, StringComparison.Ordinal);
            Assert.Contains("Rides rejected: 1", extracted, StringComparison.Ordinal);
            Assert.DoesNotContain("ride-1", extracted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ride-2", extracted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Customer:", extracted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Fare:", extracted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Net payable:", extracted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("₹", extracted, StringComparison.Ordinal);
            Assert.DoesNotContain("INR", extracted, StringComparison.Ordinal);
        }
        var snapshots = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverMonthlyPayslip>>();
        var snapshot = Assert.Single(snapshots.Values, item =>
            item.DriverId == "driver-payslip-pdf" && item.Year == 2026 && item.Month == 10);
        Assert.Equal(2, snapshot.DaysWorked);
        Assert.Equal(2, snapshot.RidesAccepted);
        Assert.Equal(2, snapshot.RidesCompleted);
        Assert.Equal(1, snapshot.RidesRejected);

        ledger["driver-payslip-pdf|ride-4:Completed"] = new DriverWorkActivity(
            "ride-4:Completed", "driver-payslip-pdf", "ride-4", "Completed",
            new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero));
        using var repeated = await driver.GetAsync("/api/v1/driver/payslips/2026/10.pdf");
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(
            ExtractPayslipPdfText(pdfBytes),
            ExtractPayslipPdfText(await repeated.Content.ReadAsByteArrayAsync()));

        using var otherDriverResponse = await otherDriver.GetAsync(
            "/api/v1/driver/payslips/2026/10.pdf?driverId=driver-payslip-pdf");
        Assert.Equal(HttpStatusCode.Conflict, otherDriverResponse.StatusCode);
        Assert.NotEqual("application/pdf", otherDriverResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PayslipSnapshotRepairsMissingSelectedRideActivitiesFromAuditBeforeSnapshot()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payslip-audit-recovery-driver";
        const string unionId = "payslip-audit-recovery-union";
        using var rider = factory.CreateClient("Rider", "payslip-audit-recovery-rider");
        using var driver = factory.CreateClient("Driver", driverId);

        var completedRideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, unionId, "payslip-audit-completed", 500m);

        // A selected Driver can decline while the ride is still Negotiating,
        // before either party has proposed an agreement version.
        SeedBookableDriver(factory, driverId, unionId);
        using var rejectedBookingRequest = CreateSelectedBookingRequest(
            "payslip-audit-rejected-before-agreement", driverId, unionId, "KPHB", "HITEC City", []);
        using var rejectedBooking = await rider.SendAsync(rejectedBookingRequest);
        Assert.Equal(HttpStatusCode.Created, rejectedBooking.StatusCode);
        using var rejectedBookingJson = JsonDocument.Parse(await rejectedBooking.Content.ReadAsStringAsync());
        var rejectedRideId = rejectedBookingJson.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        Assert.Empty(rides[rejectedRideId].AgreementVersions ?? []);

        using var rejected = await driver.PostAsync($"/api/v1/rides/{rejectedRideId}/reject", null);
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        var acceptedAt = new DateTimeOffset(2026, 10, 3, 4, 0, 0, TimeSpan.Zero);
        var completedAt = new DateTimeOffset(2026, 10, 3, 5, 0, 0, TimeSpan.Zero);
        var completedRide = rides[completedRideId];
        Assert.NotNull(completedRide.AuditEvents);
        rides[completedRideId] = completedRide with
        {
            AuditEvents = completedRide.AuditEvents!.Select(item =>
                item.EventType == "RideStatusChanged" ? item.NewStatus switch
                {
                    "Accepted" => item with { OccurredAtUtc = acceptedAt },
                    "Completed" => item with { OccurredAtUtc = completedAt },
                    _ => item
                } : item).ToArray()
        };

        var rejectedAt = new DateTimeOffset(2026, 10, 4, 4, 0, 0, TimeSpan.Zero);
        var rejectedRide = rides[rejectedRideId];
        Assert.NotNull(rejectedRide.AuditEvents);
        rides[rejectedRideId] = rejectedRide with
        {
            AuditEvents = rejectedRide.AuditEvents!.Select(item =>
                item.EventType == "RideStatusChanged" &&
                item.NewStatus == "Cancelled" && item.ActorRole == "Driver"
                    ? item with { OccurredAtUtc = rejectedAt }
                    : item).ToArray()
        };

        var ledger = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverWorkActivity>>();
        Assert.True(ledger.TryRemove($"{driverId}|{completedRideId}:Accepted", out _));
        Assert.True(ledger.TryRemove($"{driverId}|{completedRideId}:Completed", out _));
        Assert.True(ledger.TryRemove($"{driverId}|{rejectedRideId}:Rejected", out _));

        var union = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[unionId];
        var assignment = new DriverUnionAuditEvent(
            "payslip-audit-assignment", driverId, null, null,
            unionId, union.Name, "Assigned", "Initial assignment", "Operations",
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>()
            [$"{driverId}|{assignment.EventId}"] = assignment;

        using var response = await driver.GetAsync("/api/v1/driver/payslips/2026/10.pdf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pdfBytes = await response.Content.ReadAsByteArrayAsync();
        var pdfHeader = Encoding.ASCII.GetString(pdfBytes);
        Assert.StartsWith("%PDF-1.4", pdfHeader);
        Assert.EndsWith("%%EOF", pdfHeader.TrimEnd());
        Assert.True(pdfBytes.Length > 5_000);
        var snapshots = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverMonthlyPayslip>>();
        var snapshot = Assert.Single(snapshots.Values, item =>
            item.DriverId == driverId && item.Year == 2026 && item.Month == 10);
        Assert.Equal(1, snapshot.DaysWorked);
        Assert.Equal(1, snapshot.RidesAccepted);
        Assert.Equal(1, snapshot.RidesCompleted);
        Assert.Equal(1, snapshot.RidesRejected);
        Assert.Single(ledger.Values, item => item.RideId == completedRideId && item.ActivityType == "Accepted");
        Assert.Single(ledger.Values, item => item.RideId == completedRideId && item.ActivityType == "Completed");
        Assert.Single(ledger.Values, item => item.RideId == rejectedRideId && item.ActivityType == "Rejected");

        using var repeated = await driver.GetAsync("/api/v1/driver/payslips/2026/10.pdf");
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(
            ExtractPayslipPdfText(pdfBytes),
            ExtractPayslipPdfText(await repeated.Content.ReadAsByteArrayAsync()));
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
        var pdfHeader = Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", pdfHeader);
        Assert.EndsWith("%%EOF", pdfHeader.TrimEnd());
        Assert.True(pdf.Length > 5_000);
        Assert.Equal(12, payslip.DaysWorked);
        Assert.Equal(18, payslip.RidesAccepted);
        Assert.Equal(15, payslip.RidesCompleted);
        Assert.Equal(2, payslip.RidesRejected);
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
    public async Task RiderDiscoveryFailsClosedWhenLocationFreshnessPolicyIsNotConfigured()
    {
        using var factory = new TurnOvaApiFactory(nearbyLocationMaxAgeSeconds: null);
        using var rider = factory.CreateClient("Rider", "location-freshness-unconfigured-rider");

        using var response = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RiderDiscoveryFailsClosedWhenLocationFreshnessPolicyIsNonPositive(int maxAgeSeconds)
    {
        using var factory = new TurnOvaApiFactory(nearbyLocationMaxAgeSeconds: maxAgeSeconds);
        using var rider = factory.CreateClient("Rider", $"location-freshness-invalid-{maxAgeSeconds}");

        using var response = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SelectedBookingFailsClosedWhenLocationFreshnessPolicyIsMissingOrNonPositive(
        int? maxAgeSeconds)
    {
        const string riderId = "location-freshness-booking-rider";
        const string driverId = "location-freshness-booking-driver";
        const string unionId = "location-freshness-booking-union";
        using var factory = new TurnOvaApiFactory(nearbyLocationMaxAgeSeconds: maxAgeSeconds);
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var request = CreateSelectedBookingRequest(
            "location-freshness-booking-key-" + (maxAgeSeconds?.ToString() ?? "missing"),
            driverId, unionId, "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("The approved maximum Driver location age is not configured.",
            await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>());
        Assert.Empty(factory.Services.GetRequiredService<ConcurrentDictionary<string, RideIdempotencyRecord>>());
        Assert.Null(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync(driverId));
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync(riderId));
        Assert.Equal("Online", factory.Services
            .GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()[driverId].AvailabilityStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task SelectedBookingFailsClosedWhenNearbyRadiusIsMissingOrInvalidBeforeAnyReservationWrites(
        double? radiusKm)
    {
        const string riderId = "selected-booking-radius-policy-rider";
        const string driverId = "selected-booking-radius-policy-driver";
        const string unionId = "selected-booking-radius-policy-union";
        using var factory = new TurnOvaApiFactory(
            legacyFifoRiderRequestsEnabled: false,
            nearbyRadiusKm: radiusKm);
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var request = CreateSelectedBookingRequest(
            "selected-booking-radius-policy-" + (radiusKm?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing"),
            driverId, unionId, "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("The approved Rider discovery radius is not configured.",
            await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>());
        Assert.Empty(factory.Services.GetRequiredService<ConcurrentDictionary<string, RideIdempotencyRecord>>());
        Assert.Null(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync(driverId));
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync(riderId));
        Assert.Equal("Online", factory.Services
            .GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>()[driverId].AvailabilityStatus);
    }

    [Fact]
    public async Task RiderDiscoveryExcludesLocationWithStaleCaptureEvenWhenServerReceiptIsFresh()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "rider-stale-capture-discovery");
        const string driverId = "driver-stale-capture-discovery";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, "union-stale-capture-discovery");

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var receivedAt = DateTimeOffset.UtcNow;
        profiles[driverId] = profiles[driverId] with
        {
            LocationUpdatedAtUtc = receivedAt,
            LocationCapturedAtUtc = receivedAt.AddMinutes(-3)
        };

        using var response = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("unions").EnumerateArray());
    }

    [Fact]
    public async Task SelectedBookingRejectsStaleCaptureEvenWhenServerReceiptIsFresh()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "rider-stale-capture-booking");
        const string driverId = "driver-stale-capture-booking";
        const string unionId = "union-stale-capture-booking";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var receivedAt = DateTimeOffset.UtcNow;
        profiles[driverId] = profiles[driverId] with
        {
            LocationUpdatedAtUtc = receivedAt,
            LocationCapturedAtUtc = receivedAt.AddMinutes(-3)
        };

        using var request = CreateSelectedBookingRequest(
            "stale-capture-booking-key", driverId, unionId, "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(await factory.Services.GetRequiredService<IDriverRideReservationStore>().GetActiveAsync(driverId));
        Assert.Empty(factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>());
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
    public async Task RiderHistoryDoesNotFollowMismatchedRideIdIntoAnotherRidersAggregate()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string riderId = "history-owner-rider";
        var victim = CreateHistoricalRide(
            "victim-history-ride", "different-rider", "victim-history-driver",
            "InProgress", "cab", "victim-history-union");
        var mismatchedHistory = victim with { RiderId = riderId };
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[victim.RideId] = victim;
        rides["rider-history-alias"] = mismatchedHistory;
        using var rider = factory.CreateClient("Rider", riderId);

        using var response = await rider.GetAsync("/api/v1/rides/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task DriverHistoryDoesNotFollowMismatchedRideIdIntoAnotherDriversAggregate()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "history-owner-driver";
        var victim = CreateHistoricalRide(
            "victim-history-ride", "victim-history-rider", "different-driver",
            "InProgress", "cab", "victim-history-union");
        var mismatchedHistory = victim with { DriverId = driverId };
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[victim.RideId] = victim;
        rides["driver-history-alias"] = mismatchedHistory;
        using var driver = factory.CreateClient("Driver", driverId);

        using var response = await driver.GetAsync("/api/v1/driver/rides/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Negotiating")]
    public async Task RideReadRejectsMismatchedStoredAggregateWithoutReleasingReservation(string status)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string riderId = "mismatched-aggregate-rider";
        const string driverId = "mismatched-aggregate-driver";
        const string requestedRideId = "requested-ride-alias";
        using var rider = factory.CreateClient("Rider", riderId);

        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.True(await reservations.TryAcquireAsync(driverId, requestedRideId));

        var mismatched = CreateHistoricalRide(
            "different-stored-ride", riderId, driverId, status, "cab", "mismatched-aggregate-union",
            DateTimeOffset.UtcNow.AddMinutes(5));
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[requestedRideId] = mismatched;

        using var response = await rider.GetAsync($"/api/v1/rides/{requestedRideId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(mismatched, rides[requestedRideId]);
        Assert.Equal(requestedRideId, (await reservations.GetActiveAsync(driverId))?.RideId);
    }

    [Fact]
    public async Task RiderDiscoveryFiltersHistoricalActiveDriversAndKeepsTerminalOnlyAndEligibleDrivers()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "legacy-discovery-rider");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var unions = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>();
        var assignments = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>();
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var now = DateTimeOffset.UtcNow;
        const string unionId = "legacy-discovery-union";

        unions[unionId] = new DriverUnion(
            unionId, "Legacy Discovery Union", "LEGACY-DISCOVERY", "Hyderabad",
            null, null, ["hyderabad-central"], ["cab"], "Active", now, now);

        void AddDriver(string driverId)
        {
            using var seededClient = factory.CreateClient("Driver", driverId);
            profiles[driverId] = profiles[driverId] with
            {
                FullName = driverId,
                Status = "Approved",
                ServiceClass = "cab",
                PassengerCapacity = 4,
                Documents =
                [
                    new DriverDocumentReference("DrivingLicence", $"test/{driverId}/licence", "Approved"),
                    new DriverDocumentReference("VehicleRegistration", $"test/{driverId}/registration", "Approved")
                ],
                AvailabilityStatus = "Online",
                CurrentLatitude = 17.493,
                CurrentLongitude = 78.399,
                LocationUpdatedAtUtc = now,
                ProfilePhotoBase64 = "/9j/",
                ProfilePhotoContentType = "image/jpeg",
                ProfilePhotoApprovalStatus = "Approved",
                ProfilePhotoApprovedByUnionId = unionId
            };
            assignments[driverId] = new DriverUnionAssignment(
                driverId, unionId, "Legacy Discovery Union", "Active", now, now, "Historical active-ride regression fixture");
        }

        AddDriver("legacy-discovery-active");
        AddDriver("legacy-discovery-terminal");
        AddDriver("legacy-discovery-eligible");

        var active = CreateHistoricalRide(
            "legacy-discovery-active-ride", "legacy-active-rider", "legacy-discovery-active",
            "Negotiating", "cab", unionId, now.AddMinutes(5));
        var terminal = CreateHistoricalRide(
            "legacy-discovery-terminal-ride", "legacy-terminal-rider", "legacy-discovery-terminal",
            "Completed", "cab", unionId);
        rides[active.RideId] = active;
        rides[terminal.RideId] = terminal;

        using var response = await rider.GetAsync(
            "/api/v1/rider/discovery?latitude=17.493&longitude=78.399&passengers=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var group = Assert.Single(json.RootElement.GetProperty("unions").EnumerateArray());
        var driverIds = group.GetProperty("drivers").EnumerateArray()
            .Select(driver => driver.GetProperty("driverId").GetString()!)
            .ToArray();

        Assert.Equal(2, driverIds.Length);
        Assert.Contains("legacy-discovery-terminal", driverIds);
        Assert.Contains("legacy-discovery-eligible", driverIds);
        Assert.DoesNotContain("legacy-discovery-active", driverIds);
        Assert.Equal(active, rides[active.RideId]);
        Assert.Equal(terminal, rides[terminal.RideId]);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RiderDiscoveryFailsClosedWhenPickupMatchesOverlappingConfiguredZones(
        bool reverseZoneOrder, bool sharedBoundary)
    {
        var zones = CreateAmbiguousDispatchZones(reverseZoneOrder, sharedBoundary);
        var pickupLatitude = sharedBoundary ? 17.6 : 17.493;
        var pickupLongitude = sharedBoundary ? 78.4 : 78.399;
        using var factory = new TurnOvaApiFactory(dispatchZones: zones);
        using var rider = factory.CreateClient("Rider", "rider-ambiguous-zone");

        using var response = await rider.GetAsync(
            $"/api/v1/rider/discovery?latitude={pickupLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={pickupLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&passengers=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not available in this pickup zone", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SelectedBookingFailsClosedWhenPickupMatchesOverlappingConfiguredZonesBeforeReservation(
        bool reverseZoneOrder, bool sharedBoundary)
    {
        var zones = CreateAmbiguousDispatchZones(reverseZoneOrder, sharedBoundary);
        var pickupLatitude = sharedBoundary ? 17.6 : 17.493;
        var pickupLongitude = sharedBoundary ? 78.4 : 78.399;
        using var factory = new TurnOvaApiFactory(dispatchZones: zones);
        using var rider = factory.CreateClient("Rider", "rider-ambiguous-booking-zone");
        using var request = CreateSelectedBookingRequest(
            "ambiguous-booking-key", "ambiguous-booking-driver", "ambiguous-booking-union",
            "KPHB", "HITEC City", [], pickupLatitude, pickupLongitude);

        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>());
        Assert.Empty(factory.Services.GetRequiredService<ConcurrentDictionary<string, RideIdempotencyRecord>>());
        Assert.Null(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync("ambiguous-booking-driver"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DriverLocationTakesDriverOfflineWhenPickupMatchesOverlappingConfiguredZones(
        bool reverseZoneOrder, bool sharedBoundary)
    {
        var zones = CreateAmbiguousDispatchZones(reverseZoneOrder, sharedBoundary);
        var pickupLatitude = sharedBoundary ? 17.6 : 17.493;
        var pickupLongitude = sharedBoundary ? 78.4 : 78.399;
        using var factory = new TurnOvaApiFactory(dispatchZones: zones);
        using var driver = factory.CreateClient("Driver", "driver-ambiguous-zone");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles["driver-ambiguous-zone"] = profiles["driver-ambiguous-zone"] with
        {
            AvailabilityStatus = "Online",
            CurrentLatitude = pickupLatitude,
            CurrentLongitude = pickupLongitude,
            LocationUpdatedAtUtc = DateTimeOffset.UtcNow
        };

        using var response = await driver.PostAsJsonAsync(
            "/api/v1/driver/location", new { latitude = pickupLatitude, longitude = pickupLongitude });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("left your assigned service zone",
            (await response.Content.ReadAsStringAsync()).ToLowerInvariant());
        var updated = profiles["driver-ambiguous-zone"];
        Assert.Equal("Offline", updated.AvailabilityStatus);
        Assert.Null(updated.CurrentLatitude);
        Assert.Null(updated.CurrentLongitude);
        Assert.Null(updated.LocationUpdatedAtUtc);
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

        var zoneExitCapture = DateTimeOffset.UtcNow.AddSeconds(1);
        using var location = await driver.PostAsJsonAsync("/api/v1/driver/location",
            new { latitude = 0d, longitude = 0d, capturedAtUtc = zoneExitCapture });
        Assert.Equal(HttpStatusCode.Conflict, location.StatusCode);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var profile = profiles["driver-location-zone-exit"];
        Assert.Equal("Offline", profile.AvailabilityStatus);
        Assert.Null(profile.CurrentLatitude);
        Assert.Null(profile.CurrentLongitude);
        Assert.Null(profile.LocationUpdatedAtUtc);
        Assert.Equal(zoneExitCapture, profile.LocationCapturedAtUtc);

        using var staleOnline = await driver.PostAsJsonAsync("/api/v1/driver/availability", new
        {
            isOnline = true,
            latitude = 17.493,
            longitude = 78.399,
            capturedAtUtc = zoneExitCapture.AddSeconds(-1)
        });
        Assert.Equal(HttpStatusCode.Conflict, staleOnline.StatusCode);
        Assert.Equal("Offline", profiles["driver-location-zone-exit"].AvailabilityStatus);

        var queue = await driver.GetFromJsonAsync<List<QueueEntryTest>>(
            "/api/v1/zones/hyderabad-central/queues/cab");
        Assert.NotNull(queue);
        Assert.DoesNotContain(queue, entry => entry.DriverId == "driver-location-zone-exit");
    }

    [Fact]
    public async Task DriverLocationRejectsOutOfOrderFixWithoutRefreshingOrTakingDriverOffline()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-location-out-of-order");
        SeedActiveCabUnion(factory, "driver-location-out-of-order");

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        using var online = await driver.PostAsJsonAsync("/api/v1/driver/availability", new
        {
            isOnline = true,
            latitude = 17.493,
            longitude = 78.399
        });
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);
        var latestCapture = profiles["driver-location-out-of-order"].LocationCapturedAtUtc!.Value;
        var current = profiles["driver-location-out-of-order"];

        foreach (var coordinates in new[] { (Latitude: 17.5d, Longitude: 78.4d), (Latitude: 0d, Longitude: 0d) })
        {
            using var stale = await driver.PostAsJsonAsync("/api/v1/driver/location", new
            {
                latitude = coordinates.Latitude,
                longitude = coordinates.Longitude,
                capturedAtUtc = latestCapture.AddSeconds(-1)
            });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        var after = profiles["driver-location-out-of-order"];
        Assert.Equal("Online", after.AvailabilityStatus);
        Assert.Equal(current.CurrentLatitude, after.CurrentLatitude);
        Assert.Equal(current.CurrentLongitude, after.CurrentLongitude);
        Assert.Equal(current.LocationUpdatedAtUtc, after.LocationUpdatedAtUtc);
        Assert.Equal(latestCapture, after.LocationCapturedAtUtc);
        var queue = await driver.GetFromJsonAsync<List<QueueEntryTest>>(
            "/api/v1/zones/hyderabad-central/queues/cab");
        Assert.Contains(queue!, entry => entry.DriverId == "driver-location-out-of-order");
    }

    [Fact]
    public async Task DriverCannotReusePreOfflineGpsFixWhenGoingOnlineAgain()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "driver-online-stale-fix");
        SeedActiveCabUnion(factory, "driver-online-stale-fix");

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var acceptedCapture = DateTimeOffset.UtcNow;
        using var firstOnline = await driver.PostAsJsonAsync("/api/v1/driver/availability", new
        {
            isOnline = true,
            latitude = 17.493,
            longitude = 78.399,
            capturedAtUtc = acceptedCapture
        });
        Assert.Equal(HttpStatusCode.OK, firstOnline.StatusCode);

        using var offline = await driver.PostAsJsonAsync("/api/v1/driver/availability", new { isOnline = false });
        Assert.Equal(HttpStatusCode.OK, offline.StatusCode);
        Assert.Equal(acceptedCapture, profiles["driver-online-stale-fix"].LocationCapturedAtUtc);

        using var staleOnline = await driver.PostAsJsonAsync("/api/v1/driver/availability", new
        {
            isOnline = true,
            latitude = 17.493,
            longitude = 78.399,
            capturedAtUtc = acceptedCapture.AddSeconds(-1)
        });

        Assert.Equal(HttpStatusCode.Conflict, staleOnline.StatusCode);
        Assert.Equal("Offline", profiles["driver-online-stale-fix"].AvailabilityStatus);
        Assert.Null(profiles["driver-online-stale-fix"].CurrentLatitude);
        var queue = await driver.GetFromJsonAsync<List<QueueEntryTest>>(
            "/api/v1/zones/hyderabad-central/queues/cab");
        Assert.DoesNotContain(queue!, entry => entry.DriverId == "driver-online-stale-fix");
    }

    [Fact]
    public async Task DriverCannotGoOnlineWithGpsCaptureTimestampMoreThanOneMinuteInFuture()
    {
        using var factory = new TurnOvaApiFactory();
        const string driverId = "driver-online-future-capture";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedActiveCabUnion(factory, driverId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var futureCapture = DateTimeOffset.UtcNow.AddMinutes(2);
        using var response = await driver.PostAsJsonAsync("/api/v1/driver/availability", new
        {
            isOnline = true,
            latitude = 17.493,
            longitude = 78.399,
            capturedAtUtc = futureCapture
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Offline", profiles[driverId].AvailabilityStatus);
        Assert.Null(profiles[driverId].CurrentLatitude);
        var queue = await driver.GetFromJsonAsync<List<QueueEntryTest>>(
            "/api/v1/zones/hyderabad-central/queues/cab");
        Assert.DoesNotContain(queue!, entry => entry.DriverId == driverId);
    }

    [Fact]
    public async Task DriverLocationRejectsGpsCaptureTimestampMoreThanOneMinuteInFutureWithoutChangingAcceptedFix()
    {
        using var factory = new TurnOvaApiFactory();
        const string driverId = "driver-location-future-capture";
        using var driver = factory.CreateClient("Driver", driverId);
        SeedActiveCabUnion(factory, driverId);

        using var online = await driver.PostAsJsonAsync("/api/v1/driver/availability", new
        {
            isOnline = true,
            latitude = 17.493,
            longitude = 78.399
        });
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var before = profiles[driverId];
        using var response = await driver.PostAsJsonAsync("/api/v1/driver/location", new
        {
            latitude = 17.494,
            longitude = 78.400,
            capturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(2)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = profiles[driverId];
        Assert.Equal("Online", after.AvailabilityStatus);
        Assert.Equal(before.CurrentLatitude, after.CurrentLatitude);
        Assert.Equal(before.CurrentLongitude, after.CurrentLongitude);
        Assert.Equal(before.LocationUpdatedAtUtc, after.LocationUpdatedAtUtc);
        Assert.Equal(before.LocationCapturedAtUtc, after.LocationCapturedAtUtc);
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
    public async Task IdempotentRetryDoesNotRestoreRiderReservationAfterConcurrentNegotiationExpiry()
    {
        const string riderId = "idempotent-expiry-rider";
        const string driverId = "idempotent-expiry-driver";
        const string unionId = "idempotent-expiry-union";
        const string bookingKey = "idempotent-expiry-key";
        var reservations = new ReservationMakePermanentFailureStore(new InMemoryDriverRideReservationStore());
        using var factory = new TurnOvaApiFactory(
            legacyFifoRiderRequestsEnabled: false,
            reservationStore: reservations);
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var originalRequest = CreateSelectedBookingRequest(
            bookingKey, driverId, unionId, "KPHB", "HITEC City", []);
        using var originalResponse = await rider.SendAsync(originalRequest);
        Assert.Equal(HttpStatusCode.Created, originalResponse.StatusCode);
        using var originalJson = JsonDocument.Parse(await originalResponse.Content.ReadAsStringAsync());
        var rideId = originalJson.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();

        Assert.True(await reservations.TryReleaseAsync(driverId, rideId));
        reservations.BeforeNextGetActive(() =>
        {
            rides[rideId] = rides[rideId] with
            {
                NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
            };
        });

        using var retryRequest = CreateSelectedBookingRequest(
            bookingKey, driverId, unionId, "KPHB", "HITEC City", []);
        using var retryResponse = await rider.SendAsync(retryRequest);

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using var retryJson = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync());
        Assert.Equal("Expired", retryJson.RootElement.GetProperty("status").GetString());
        Assert.Equal("Expired", rides[rideId].Status);
        Assert.Null(await reservations.GetActiveAsync(driverId));
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync(riderId));
        Assert.Contains(rides[rideId].AuditEvents!, item =>
            item.EventType == "RideStatusChanged" && item.NewStatus == "Expired");
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Matching")]
    [InlineData("Offered")]
    [InlineData("Negotiating")]
    [InlineData("AgreementConfirmed")]
    public async Task HistoricalActiveCabRideWithoutReservationBlocksSelectedBooking(string status)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        var suffix = status.ToLowerInvariant();
        var riderId = $"legacy-{suffix}-new-rider";
        var driverId = $"legacy-{suffix}-driver";
        var unionId = $"legacy-{suffix}-union";
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var historical = CreateHistoricalRide(
            $"legacy-{suffix}-ride", $"legacy-{suffix}-old-rider", driverId,
            status, "cab", unionId, DateTimeOffset.UtcNow.AddMinutes(5));
        rides[historical.RideId] = historical;

        using var request = CreateSelectedBookingRequest(
            $"legacy-{suffix}-new-booking", driverId, unionId, "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(historical, rides[historical.RideId]);
        Assert.Null(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync(driverId));
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync(riderId));
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Arrived")]
    [InlineData("InProgress")]
    public async Task HistoricalAcceptedArrivedOrInProgressRideWithoutReservationBlocksBookingAvailabilityAndUnionTransfer(
        string status)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        var riderId = $"legacy-{status.ToLowerInvariant()}-rider";
        var driverId = $"legacy-{status.ToLowerInvariant()}-driver";
        var unionId = $"legacy-{status.ToLowerInvariant()}-union";
        var targetUnionId = $"legacy-{status.ToLowerInvariant()}-target";
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        using var ops = factory.CreateClient("Rider", $"{driverId}-ops");
        SeedBookableDriver(factory, driverId, unionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            AvailabilityStatus = "Online",
            ProfilePhotoBase64 = Convert.ToBase64String([1, 2, 3]),
            ProfilePhotoContentType = "image/jpeg",
            ProfilePhotoApprovalStatus = "Approved",
            ProfilePhotoApprovedByUnionId = unionId
        };
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[targetUnionId] =
            new DriverUnion(targetUnionId, "Historical Transfer Target", "HISTORICAL-TARGET", "Hyderabad",
                null, null, ["hyderabad-central"], ["cab"], "Active", now, now);
        var historical = CreateHistoricalRide(
            $"legacy-{status.ToLowerInvariant()}-ride", $"{driverId}-old-rider", driverId,
            status, "cab", unionId);
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[historical.RideId] = historical;

        using var bookingRequest = CreateSelectedBookingRequest(
            $"{driverId}-new-booking", driverId, unionId, "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(bookingRequest);
        Assert.Equal(HttpStatusCode.Conflict, booking.StatusCode);

        using var availability = await driver.PostAsJsonAsync(
            "/api/v1/driver/availability", new { isOnline = false });
        Assert.Equal(HttpStatusCode.Conflict, availability.StatusCode);

        using var transfer = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/ops/drivers/{driverId}/union-assignment")
        {
            Content = JsonContent.Create(new { unionId = targetUnionId, reason = "Approved operational transfer" })
        };
        transfer.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var transferResponse = await ops.SendAsync(transfer);
        Assert.Equal(HttpStatusCode.Conflict, transferResponse.StatusCode);

        Assert.Equal(historical, rides[historical.RideId]);
        Assert.Equal("Online", profiles[driverId].AvailabilityStatus);
        Assert.Equal(unionId, factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>()
            [driverId].UnionId);
        Assert.Null(await factory.Services.GetRequiredService<IDriverRideReservationStore>().GetActiveAsync(driverId));
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>().GetActiveAsync(riderId));
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    [InlineData("Expired")]
    public async Task TerminalHistoricalRideWithoutReservationDoesNotBlockNewSelectedBookingAndPreservesHistory(
        string status)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string riderId = "terminal-history-new-rider";
        const string driverId = "terminal-history-driver";
        const string unionId = "terminal-history-union";
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var historical = CreateHistoricalRide(
            $"terminal-history-{status.ToLowerInvariant()}", "terminal-history-old-rider", driverId,
            status, "cab", unionId);
        rides[historical.RideId] = historical;

        using var request = CreateSelectedBookingRequest(
            $"terminal-history-{status.ToLowerInvariant()}-new", driverId, unionId, "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var newRideId = json.RootElement.GetProperty("rideId").GetString()!;

        Assert.NotEqual(historical.RideId, newRideId);
        Assert.Equal(historical, rides[historical.RideId]);
        Assert.Equal(newRideId, (await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync(driverId))?.RideId);
    }

    [Fact]
    public async Task HistoricalNonCabActiveRideWithoutReservationBlocksBookingAvailabilityAndUnionTransferAndPreservesHistory()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string riderId = "legacy-noncab-new-rider";
        const string driverId = "legacy-noncab-driver";
        const string unionId = "legacy-noncab-current-union";
        const string targetUnionId = "legacy-noncab-target-union";
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        using var ops = factory.CreateClient("Rider", "legacy-noncab-ops");
        SeedBookableDriver(factory, driverId, unionId);
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with
        {
            AvailabilityStatus = "Online",
            ProfilePhotoBase64 = Convert.ToBase64String([1, 2, 3]),
            ProfilePhotoContentType = "image/jpeg",
            ProfilePhotoApprovalStatus = "Approved",
            ProfilePhotoApprovedByUnionId = unionId
        };
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[targetUnionId] =
            new DriverUnion(targetUnionId, "Non-Cab Transfer Target", "NONCAB-TARGET", "Hyderabad",
                null, null, ["hyderabad-central"], ["cab"], "Active", now, now);
        var historical = CreateHistoricalRide(
            "legacy-noncab-active-ride", "legacy-noncab-old-rider", driverId,
            "InProgress", "auto", null);
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[historical.RideId] = historical;

        using var bookingRequest = CreateSelectedBookingRequest(
            "legacy-noncab-new-booking", driverId, unionId, "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(bookingRequest);
        Assert.Equal(HttpStatusCode.Conflict, booking.StatusCode);

        using var availability = await driver.PostAsJsonAsync(
            "/api/v1/driver/availability", new { isOnline = false });
        Assert.Equal(HttpStatusCode.Conflict, availability.StatusCode);

        using var transfer = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/ops/drivers/{driverId}/union-assignment")
        {
            Content = JsonContent.Create(new { unionId = targetUnionId, reason = "Approved operational transfer" })
        };
        transfer.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var transferResponse = await ops.SendAsync(transfer);
        Assert.Equal(HttpStatusCode.Conflict, transferResponse.StatusCode);

        Assert.Equal(historical, rides[historical.RideId]);
        Assert.Equal("auto", rides[historical.RideId].ServiceClass);
        Assert.Null(rides[historical.RideId].UnionId);
        Assert.Null(await factory.Services.GetRequiredService<IDriverRideReservationStore>().GetActiveAsync(driverId));
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>().GetActiveAsync(riderId));
    }

    [Fact]
    public async Task TerminalReservationCleanupStillBlocksWhenAnotherHistoricalActiveRideExists()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string ownerId = "terminal-reservation-owner";
        const string challengerId = "terminal-reservation-challenger";
        const string driverId = "terminal-reservation-driver";
        const string unionId = "terminal-reservation-union";
        const string targetUnionId = "terminal-reservation-target-union";
        using var owner = factory.CreateClient("Rider", ownerId);
        using var challenger = factory.CreateClient("Rider", challengerId);
        using var driver = factory.CreateClient("Driver", driverId);
        using var ops = factory.CreateClient("Rider", "terminal-reservation-ops");
        SeedBookableDriver(factory, driverId, unionId);
        SeedTransferTargetUnion(factory, targetUnionId);

        using var originalRequest = CreateSelectedBookingRequest(
            "terminal-reservation-original", driverId, unionId, "KPHB", "HITEC City", []);
        using var originalResponse = await owner.SendAsync(originalRequest);
        Assert.Equal(HttpStatusCode.Created, originalResponse.StatusCode);
        using var originalJson = JsonDocument.Parse(await originalResponse.Content.ReadAsStringAsync());
        var originalRideId = originalJson.RootElement.GetProperty("rideId").GetString()!;

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[originalRideId] = rides[originalRideId] with
        {
            Status = "Completed",
            NegotiationExpiresAtUtc = null
        };
        var terminalRide = rides[originalRideId];
        var secondActive = CreateHistoricalRide(
            "terminal-reservation-second-active", "terminal-reservation-second-rider", driverId,
            "InProgress", "cab", unionId);
        rides[secondActive.RideId] = secondActive;

        using var challengerRequest = CreateSelectedBookingRequest(
            "terminal-reservation-challenger-booking", driverId, unionId, "KPHB", "HITEC City", []);
        using var challengerResponse = await challenger.SendAsync(challengerRequest);

        Assert.Equal(HttpStatusCode.Conflict, challengerResponse.StatusCode);
        Assert.Equal(terminalRide, rides[originalRideId]);
        Assert.Equal(secondActive, rides[secondActive.RideId]);
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.Null(await reservations.GetActiveAsync(driverId));
        var riderReservations = factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>();
        Assert.Null(await riderReservations.GetActiveAsync(ownerId));
        Assert.Null(await riderReservations.GetActiveAsync(challengerId));

        using var availability = await driver.PostAsJsonAsync(
            "/api/v1/driver/availability", new { isOnline = false });
        Assert.Equal(HttpStatusCode.Conflict, availability.StatusCode);

        using var transfer = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/ops/drivers/{driverId}/union-assignment")
        {
            Content = JsonContent.Create(new { unionId = targetUnionId, reason = "Approved operational transfer" })
        };
        transfer.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var transferResponse = await ops.SendAsync(transfer);
        Assert.Equal(HttpStatusCode.Conflict, transferResponse.StatusCode);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var assignments = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>();
        Assert.Equal("Online", profiles[driverId].AvailabilityStatus);
        Assert.Equal(unionId, assignments[driverId].UnionId);
        Assert.Equal(secondActive, rides[secondActive.RideId]);
    }

    [Fact]
    public async Task ExpiredOrphanReservationCleanupStillBlocksWhenAnotherHistoricalActiveRideExists()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string riderId = "expired-orphan-challenger";
        const string driverId = "expired-orphan-driver";
        const string unionId = "expired-orphan-union";
        const string targetUnionId = "expired-orphan-target-union";
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        using var ops = factory.CreateClient("Rider", "expired-orphan-ops");
        SeedBookableDriver(factory, driverId, unionId);
        SeedTransferTargetUnion(factory, targetUnionId);
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.True(await reservations.TryAcquireTemporaryAsync(
            driverId, "missing-orphan-ride", DateTimeOffset.UtcNow.AddMilliseconds(150)));
        await Task.Delay(250);

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var secondActive = CreateHistoricalRide(
            "expired-orphan-second-active", "expired-orphan-old-rider", driverId,
            "InProgress", "cab", unionId);
        rides[secondActive.RideId] = secondActive;

        using var request = CreateSelectedBookingRequest(
            "expired-orphan-challenger-booking", driverId, unionId, "KPHB", "HITEC City", []);
        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(await reservations.GetActiveAsync(driverId));
        Assert.Equal(secondActive, rides[secondActive.RideId]);
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>().GetActiveAsync(riderId));

        using var availability = await driver.PostAsJsonAsync(
            "/api/v1/driver/availability", new { isOnline = false });
        Assert.Equal(HttpStatusCode.Conflict, availability.StatusCode);

        using var transfer = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/ops/drivers/{driverId}/union-assignment")
        {
            Content = JsonContent.Create(new { unionId = targetUnionId, reason = "Approved operational transfer" })
        };
        transfer.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var transferResponse = await ops.SendAsync(transfer);
        Assert.Equal(HttpStatusCode.Conflict, transferResponse.StatusCode);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var assignments = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>();
        Assert.Equal("Online", profiles[driverId].AvailabilityStatus);
        Assert.Equal(unionId, assignments[driverId].UnionId);
        Assert.Equal(secondActive, rides[secondActive.RideId]);
    }

    [Theory]
    [InlineData("Negotiating")]
    [InlineData("AgreementConfirmed")]
    public async Task SameIdempotencyRetryIsBlockedByAnotherHistoricalActiveRideWithoutReleasingItsReservation(
        string initialStatus)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string riderId = "same-idempotency-historical-rider";
        const string driverId = "same-idempotency-historical-driver";
        const string unionId = "same-idempotency-historical-union";
        const string bookingKey = "same-idempotency-historical-key";
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        string rideId;
        if (initialStatus == "AgreementConfirmed")
        {
            rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
                factory, rider, driver, driverId, unionId, bookingKey, 420m);
        }
        else
        {
            using var originalRequest = CreateSelectedBookingRequest(
                bookingKey, driverId, unionId, "KPHB", "HITEC City", []);
            using var originalResponse = await rider.SendAsync(originalRequest);
            Assert.Equal(HttpStatusCode.Created, originalResponse.StatusCode);
            using var originalJson = JsonDocument.Parse(await originalResponse.Content.ReadAsStringAsync());
            rideId = originalJson.RootElement.GetProperty("rideId").GetString()!;
        }

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var originalRide = rides[rideId];
        var secondActive = CreateHistoricalRide(
            $"same-idempotency-second-{initialStatus.ToLowerInvariant()}", "same-idempotency-second-rider",
            driverId, "Accepted", "cab", unionId);
        rides[secondActive.RideId] = secondActive;

        using var retryRequest = CreateSelectedBookingRequest(
            bookingKey, driverId, unionId, "KPHB", "HITEC City", []);
        using var retryResponse = await rider.SendAsync(retryRequest);

        Assert.Equal(HttpStatusCode.Conflict, retryResponse.StatusCode);
        Assert.Equal(originalRide, rides[rideId]);
        Assert.Equal(secondActive, rides[secondActive.RideId]);
        Assert.Equal(rideId, (await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync(driverId))?.RideId);
        Assert.Equal(rideId, (await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync(riderId))?.RideId);
    }

    [Fact]
    public async Task DriverCannotAcceptSelectedRideWhileAnotherHistoricalActiveRideExists()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string riderId = "historical-accept-rider";
        const string driverId = "historical-accept-driver";
        const string unionId = "historical-accept-union";
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, driverId, unionId, "historical-accept-booking", 420m);

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var selectedRide = rides[rideId];
        var secondActive = CreateHistoricalRide(
            "historical-accept-second-active", "historical-accept-old-rider", driverId,
            "InProgress", "cab", unionId);
        rides[secondActive.RideId] = secondActive;

        using var response = await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(selectedRide, rides[rideId]);
        Assert.Equal(secondActive, rides[secondActive.RideId]);
        var reservation = await factory.Services.GetRequiredService<IDriverRideReservationStore>().GetActiveAsync(driverId);
        Assert.Equal(rideId, reservation?.RideId);
        Assert.NotNull(reservation?.ExpiresAtUtc);
    }

    [Fact]
    public async Task ExpiredHistoricalNegotiationReleasesMatchingReservationBeforeAnotherActiveRideBlocks()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string ownerId = "expired-history-owner";
        const string challengerId = "expired-history-challenger";
        const string driverId = "expired-history-driver";
        const string unionId = "expired-history-union";
        using var owner = factory.CreateClient("Rider", ownerId);
        using var challenger = factory.CreateClient("Rider", challengerId);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var originalRequest = CreateSelectedBookingRequest(
            "expired-history-original", driverId, unionId, "KPHB", "HITEC City", []);
        using var originalResponse = await owner.SendAsync(originalRequest);
        Assert.Equal(HttpStatusCode.Created, originalResponse.StatusCode);
        using var originalJson = JsonDocument.Parse(await originalResponse.Content.ReadAsStringAsync());
        var originalRideId = originalJson.RootElement.GetProperty("rideId").GetString()!;

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[originalRideId] = rides[originalRideId] with
        {
            NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
        };
        var secondActive = CreateHistoricalRide(
            "expired-history-second-active", "expired-history-old-rider", driverId,
            "InProgress", "cab", unionId);
        rides[secondActive.RideId] = secondActive;

        using var challengerRequest = CreateSelectedBookingRequest(
            "expired-history-challenger-booking", driverId, unionId, "KPHB", "HITEC City", []);
        using var challengerResponse = await challenger.SendAsync(challengerRequest);

        Assert.Equal(HttpStatusCode.Conflict, challengerResponse.StatusCode);
        var expiredRide = rides[originalRideId];
        Assert.Equal("Expired", expiredRide.Status);
        Assert.Contains(expiredRide.AuditEvents!, item =>
            item.EventType == "RideStatusChanged" && item.NewStatus == "Expired");
        Assert.Equal(secondActive, rides[secondActive.RideId]);
        Assert.Null(await factory.Services.GetRequiredService<IDriverRideReservationStore>().GetActiveAsync(driverId));
        var riderReservations = factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>();
        Assert.Null(await riderReservations.GetActiveAsync(ownerId));
        Assert.Null(await riderReservations.GetActiveAsync(challengerId));
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
        var cancelledRide = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId];
        var cancellationEvent = Assert.Single(cancelledRide.AuditEvents!,
            item => item.EventType == "RideStatusChanged" && item.NewStatus == "Cancelled");
        Assert.Equal(owner, cancellationEvent.ActorId);
        Assert.Equal("Rider", cancellationEvent.ActorRole);
        Assert.Equal("Negotiating", cancellationEvent.PreviousStatus);
        Assert.Equal("Cancelled", cancellationEvent.NewStatus);
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
        Assert.Null(await factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>()
            .GetActiveAsync("decline-booking-rider"));
        using var rideResponse = await rider.GetAsync($"/api/v1/rides/{rideId}");
        using var rideJson = JsonDocument.Parse(await rideResponse.Content.ReadAsStringAsync());
        Assert.Equal("Cancelled", rideJson.RootElement.GetProperty("status").GetString());
        var cancelledRide = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId];
        var cancellationEvent = Assert.Single(cancelledRide.AuditEvents!,
            item => item.EventType == "RideStatusChanged" && item.NewStatus == "Cancelled");
        Assert.Equal("decline-booking-driver", cancellationEvent.ActorId);
        Assert.Equal("Driver", cancellationEvent.ActorRole);
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
    public async Task ExpiredSelectedNegotiationDoesNotAdvanceLegacyFifoOffer()
    {
        var advancer = new RecordingRideOfferAdvancer();
        using var factory = new TurnOvaApiFactory(
            legacyFifoRiderRequestsEnabled: false,
            rideOfferAdvancer: advancer);
        using var rider = factory.CreateClient("Rider", "selected-expiry-no-fifo-rider");
        using var driver = factory.CreateClient("Driver", "selected-expiry-no-fifo-driver");
        SeedBookableDriver(factory, "selected-expiry-no-fifo-driver", "selected-expiry-no-fifo-union");

        using var request = CreateSelectedBookingRequest(
            "selected-expiry-no-fifo-key",
            "selected-expiry-no-fifo-driver",
            "selected-expiry-no-fifo-union",
            "KPHB",
            "HITEC City",
            []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with
        {
            NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
        };

        using var expired = await rider.GetAsync($"/api/v1/rides/{rideId}");
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        using var expiredJson = JsonDocument.Parse(await expired.Content.ReadAsStringAsync());
        Assert.Equal("Expired", expiredJson.RootElement.GetProperty("status").GetString());

        Assert.Empty(advancer.AdvancedRideIds);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("selected-expiry-no-fifo-driver"));
    }

    [Fact]
    public async Task DriverDecliningSelectedBookingDoesNotAdvanceLegacyFifoOffer()
    {
        const string riderId = "selected-decline-no-fifo-rider";
        const string driverId = "selected-decline-no-fifo-driver";
        const string unionId = "selected-decline-no-fifo-union";
        var advancer = new RecordingRideOfferAdvancer();
        using var factory = new TurnOvaApiFactory(
            legacyFifoRiderRequestsEnabled: false,
            rideOfferAdvancer: advancer);
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var request = CreateSelectedBookingRequest(
            "selected-decline-no-fifo-key", driverId, unionId, "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var driverReservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        var riderReservations = factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>();
        Assert.True(await driverReservations.HasActiveAsync(driverId));
        Assert.NotNull(await riderReservations.GetActiveAsync(riderId));

        using var rejected = await driver.PostAsync($"/api/v1/rides/{rideId}/reject", null);
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);

        var cancelledRide = rides[rideId];
        Assert.Equal("Cancelled", cancelledRide.Status);
        var declineEvent = Assert.Single(cancelledRide.AuditEvents!,
            item => item.EventType == "RideStatusChanged" && item.NewStatus == "Cancelled");
        Assert.Equal(driverId, declineEvent.ActorId);
        Assert.Equal("Driver", declineEvent.ActorRole);
        Assert.Equal("Negotiating", declineEvent.PreviousStatus);

        Assert.Empty(advancer.AdvancedRideIds);
        Assert.False(await driverReservations.HasActiveAsync(driverId));
        Assert.Null(await riderReservations.GetActiveAsync(riderId));
    }

    [Fact]
    public async Task DriverRejectingExpiredSelectedNegotiationDoesNotAdvanceLegacyFifoOffer()
    {
        const string riderId = "selected-expired-reject-no-fifo-rider";
        const string driverId = "selected-expired-reject-no-fifo-driver";
        const string unionId = "selected-expired-reject-no-fifo-union";
        var advancer = new RecordingRideOfferAdvancer();
        using var factory = new TurnOvaApiFactory(
            legacyFifoRiderRequestsEnabled: false,
            rideOfferAdvancer: advancer);
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var request = CreateSelectedBookingRequest(
            "selected-expired-reject-no-fifo-key", driverId, unionId, "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var driverReservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        var riderReservations = factory.Services.GetRequiredService<IRiderActiveBookingReservationStore>();
        Assert.True(await driverReservations.HasActiveAsync(driverId));
        Assert.NotNull(await riderReservations.GetActiveAsync(riderId));

        rides[rideId] = rides[rideId] with
        {
            NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
        };

        using var rejected = await driver.PostAsync($"/api/v1/rides/{rideId}/reject", null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        var expiredRide = rides[rideId];
        Assert.Equal("Expired", expiredRide.Status);
        var expiryEvent = Assert.Single(expiredRide.AuditEvents!,
            item => item.EventType == "RideStatusChanged" && item.NewStatus == "Expired");
        Assert.Equal("system", expiryEvent.ActorId);
        Assert.Equal("System", expiryEvent.ActorRole);
        Assert.Equal("Negotiating", expiryEvent.PreviousStatus);

        Assert.Empty(advancer.AdvancedRideIds);
        Assert.False(await driverReservations.HasActiveAsync(driverId));
        Assert.Null(await riderReservations.GetActiveAsync(riderId));
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
        var expiryEvent = Assert.Single(rides[rideId].AuditEvents!,
            item => item.EventType == "RideStatusChanged" && item.NewStatus == "Expired");
        Assert.Equal("system", expiryEvent.ActorId);
        Assert.Equal("System", expiryEvent.ActorRole);
        Assert.Equal("Negotiating", expiryEvent.PreviousStatus);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("expired-booking-driver"));
    }

    [Fact]
    public async Task RiderAndDriverHistoryReconcileExpiredSelectedNegotiationsAndReleaseOnlyTheirReservations()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "history-expiry-driver";
        const string unionId = "history-expiry-union";
        using var rider = factory.CreateClient("Rider", "history-expiry-rider");
        using var secondRider = factory.CreateClient("Rider", "history-expiry-second-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var riderBookingRequest = CreateSelectedBookingRequest(
            "history-expiry-rider-booking", driverId, unionId, "KPHB", "HITEC City", []);
        using var riderBooking = await rider.SendAsync(riderBookingRequest);
        Assert.Equal(HttpStatusCode.Created, riderBooking.StatusCode);
        using var riderBookingJson = JsonDocument.Parse(await riderBooking.Content.ReadAsStringAsync());
        var riderRideId = riderBookingJson.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[riderRideId] = rides[riderRideId] with
        {
            NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
        };

        using var riderHistoryResponse = await rider.GetAsync("/api/v1/rides/mine");
        Assert.Equal(HttpStatusCode.OK, riderHistoryResponse.StatusCode);
        using var riderHistoryJson = JsonDocument.Parse(await riderHistoryResponse.Content.ReadAsStringAsync());
        var riderHistoryRide = riderHistoryJson.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("rideId").GetString() == riderRideId);
        Assert.Equal("Expired", riderHistoryRide.GetProperty("status").GetString());
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync(driverId));
        Assert.Single(rides[riderRideId].AuditEvents!,
            item => item.EventType == "RideStatusChanged" && item.NewStatus == "Expired");

        using var driverBookingRequest = CreateSelectedBookingRequest(
            "history-expiry-driver-booking", driverId, unionId, "KPHB", "Madhapur", []);
        using var driverBooking = await secondRider.SendAsync(driverBookingRequest);
        Assert.Equal(HttpStatusCode.Created, driverBooking.StatusCode);
        using var driverBookingJson = JsonDocument.Parse(await driverBooking.Content.ReadAsStringAsync());
        var driverRideId = driverBookingJson.RootElement.GetProperty("rideId").GetString()!;
        rides[driverRideId] = rides[driverRideId] with
        {
            NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
        };

        using var driverHistoryResponse = await driver.GetAsync("/api/v1/driver/rides/mine");
        Assert.Equal(HttpStatusCode.OK, driverHistoryResponse.StatusCode);
        using var driverHistoryJson = JsonDocument.Parse(await driverHistoryResponse.Content.ReadAsStringAsync());
        var driverHistoryRide = driverHistoryJson.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("rideId").GetString() == driverRideId);
        Assert.Equal("Expired", driverHistoryRide.GetProperty("status").GetString());
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync(driverId));
        Assert.Single(rides[driverRideId].AuditEvents!,
            item => item.EventType == "RideStatusChanged" && item.NewStatus == "Expired");
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

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var legacyAccepted = rides[rideId];
        Assert.NotNull(legacyAccepted.DriverLocationCapturedAtUtc);
        Assert.Equal(17.495, legacyAccepted.DriverLatitude);
        Assert.Equal(78.401, legacyAccepted.DriverLongitude);

        // A current client may send a GPS capture time slightly ahead of server time
        // within the same one-minute skew tolerance used by availability GPS.
        var newerCapture = DateTimeOffset.UtcNow.AddSeconds(10);
        using var newerLocation = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location",
            new { latitude = 17.496, longitude = 78.402, capturedAtUtc = newerCapture });
        Assert.Equal(HttpStatusCode.NoContent, newerLocation.StatusCode);
        var acceptedNewer = rides[rideId];
        Assert.Equal(newerCapture, acceptedNewer.DriverLocationCapturedAtUtc);

        using var outOfOrder = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location",
            new { latitude = 17.497, longitude = 78.403, capturedAtUtc = newerCapture.AddSeconds(-1) });
        Assert.Equal(HttpStatusCode.Conflict, outOfOrder.StatusCode);
        var afterOutOfOrder = rides[rideId];
        Assert.Equal(acceptedNewer.DriverLatitude, afterOutOfOrder.DriverLatitude);
        Assert.Equal(acceptedNewer.DriverLongitude, afterOutOfOrder.DriverLongitude);
        Assert.Equal(acceptedNewer.LocationUpdatedAt, afterOutOfOrder.LocationUpdatedAt);
        Assert.Equal(acceptedNewer.DriverLocationCapturedAtUtc, afterOutOfOrder.DriverLocationCapturedAtUtc);

        using var futureLocation = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location",
            new { latitude = 17.498, longitude = 78.404, capturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(2) });
        Assert.Equal(HttpStatusCode.BadRequest, futureLocation.StatusCode);
        var afterFuture = rides[rideId];
        Assert.Equal(acceptedNewer.DriverLatitude, afterFuture.DriverLatitude);
        Assert.Equal(acceptedNewer.DriverLongitude, afterFuture.DriverLongitude);
        Assert.Equal(acceptedNewer.LocationUpdatedAt, afterFuture.LocationUpdatedAt);
        Assert.Equal(acceptedNewer.DriverLocationCapturedAtUtc, afterFuture.DriverLocationCapturedAtUtc);

        using var riderRideResponse = await rider.GetAsync($"/api/v1/rides/{rideId}");
        Assert.Equal(HttpStatusCode.OK, riderRideResponse.StatusCode);
        using var riderRideJson = JsonDocument.Parse(await riderRideResponse.Content.ReadAsStringAsync());
        Assert.Equal(17.496, riderRideJson.RootElement.GetProperty("driverLatitude").GetDouble(), 6);
        Assert.Equal(78.402, riderRideJson.RootElement.GetProperty("driverLongitude").GetDouble(), 6);
        Assert.Equal(newerCapture,
            riderRideJson.RootElement.GetProperty("driverLocationCapturedAtUtc").GetDateTimeOffset());

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
    public async Task SelectedCabLifecycleRetriesAreIdempotentAndKeepTerminalState()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "trip-lifecycle-idempotent-driver";
        using var rider = factory.CreateClient("Rider", "trip-lifecycle-idempotent-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, driverId, "trip-lifecycle-idempotent-union",
            "trip-lifecycle-idempotent-booking", 500m);

        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/arrive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/arrive", null)).StatusCode);
        var auditEventsBeforeLocation = factory.Services
            .GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId].AuditEvents!.ToArray();
        using var locationUpdate = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location", new { latitude = 17.493, longitude = 78.399 });
        Assert.Equal(HttpStatusCode.NoContent, locationUpdate.StatusCode);
        Assert.Equal(auditEventsBeforeLocation,
            factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId].AuditEvents!.ToArray());

        using var started = await driver.PostAsync($"/api/v1/rides/{rideId}/start", null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.Equal("InProgress", (await ReadRideAsync(started)).Status);
        using var startRetry = await driver.PostAsync($"/api/v1/rides/{rideId}/start", null);
        Assert.Equal(HttpStatusCode.OK, startRetry.StatusCode);
        Assert.Equal("InProgress", (await ReadRideAsync(startRetry)).Status);

        using var completed = await driver.PostAsync($"/api/v1/rides/{rideId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("Completed", (await ReadRideAsync(completed)).Status);
        using var completionRetry = await driver.PostAsync($"/api/v1/rides/{rideId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completionRetry.StatusCode);
        var terminalRide = await ReadRideAsync(completionRetry);
        Assert.Equal("Completed", terminalRide.Status);
        Assert.False(terminalRide.LocationSharingActive);

        var workActivities = factory.Services
            .GetRequiredService<ConcurrentDictionary<string, DriverWorkActivity>>();
        Assert.Single(workActivities.Values, item =>
            item.RideId == rideId && item.ActivityType == "Completed");
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync(driverId));

        var riderHistory = await rider.GetFromJsonAsync<List<RiderRideHistoryItemTest>>("/api/v1/rides/mine");
        Assert.NotNull(riderHistory);
        Assert.Equal("Completed", Assert.Single(riderHistory, item => item.RideId == rideId).Status);

        using var locationAfterRetry = await driver.PostAsJsonAsync(
            $"/api/v1/rides/{rideId}/location", new { latitude = 17.496, longitude = 78.402 });
        Assert.Equal(HttpStatusCode.NotFound, locationAfterRetry.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/arrive", null)).StatusCode);

        var auditEvents = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()[rideId]
            .AuditEvents!;
        Assert.Equal(new[] { "Accepted", "Arrived", "InProgress", "Completed" },
            auditEvents.Where(item => item.EventType == "RideStatusChanged")
                .Select(item => item.NewStatus).ToArray());
        Assert.Equal(4, auditEvents.Count(item => item.EventType == "RideStatusChanged"));
    }

    [Fact]
    public async Task CompletedRideRetryRepairsWorkActivityAndReservationReleaseAfterInterruptedCompletion()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "completion-recovery-driver";
        using var rider = factory.CreateClient("Rider", "completion-recovery-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, driverId, "completion-recovery-union",
            "completion-recovery-booking", 500m);

        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/arrive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await driver.PostAsync($"/api/v1/rides/{rideId}/start", null)).StatusCode);

        // Simulate a process interruption after the Completed ride write but before
        // the separate work-activity and reservation-release writes.
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with { Status = "Completed", LocationSharingActive = false };
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.True(await reservations.HasActiveAsync(driverId));

        var activities = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverWorkActivity>>();
        Assert.DoesNotContain(activities.Values, item =>
            item.RideId == rideId && item.ActivityType == "Completed");

        using var repair = await driver.PostAsync($"/api/v1/rides/{rideId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, repair.StatusCode);
        Assert.Equal("Completed", (await ReadRideAsync(repair)).Status);
        Assert.False(await reservations.HasActiveAsync(driverId));
        Assert.Single(activities.Values, item =>
            item.RideId == rideId && item.ActivityType == "Completed");

        using var retry = await driver.PostAsync($"/api/v1/rides/{rideId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Single(activities.Values, item =>
            item.RideId == rideId && item.ActivityType == "Completed");
    }

    [Theory]
    [InlineData("selected-driver-whitespace")]
    [InlineData("selected-driver-too-long")]
    [InlineData("union-whitespace")]
    [InlineData("union-too-long")]
    [InlineData("pickup-whitespace")]
    [InlineData("pickup-too-long")]
    [InlineData("dropoff-whitespace")]
    [InlineData("dropoff-too-long")]
    [InlineData("passengers-below-range")]
    [InlineData("passengers-above-range")]
    [InlineData("latitude-out-of-range")]
    [InlineData("longitude-out-of-range")]
    [InlineData("idempotency-key-missing")]
    [InlineData("idempotency-key-too-long")]
    public async Task SelectedBookingRejectsInvalidRequestFieldsBeforeReservingDriver(string invalidCase)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        var driverId = "invalid-booking-driver-" + invalidCase;
        var unionId = "invalid-booking-union-" + invalidCase;
        using var rider = factory.CreateClient("Rider", "invalid-booking-rider-" + invalidCase);
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        var payload = new Dictionary<string, object?>
        {
            ["selectedDriverId"] = driverId,
            ["unionId"] = unionId,
            ["pickup"] = "KPHB",
            ["dropoff"] = "HITEC City",
            ["stops"] = Array.Empty<string>(),
            ["passengers"] = 1,
            ["pickupLatitude"] = 17.493,
            ["pickupLongitude"] = 78.399
        };
        var idempotencyKey = "invalid-booking-key-" + invalidCase;
        switch (invalidCase)
        {
            case "selected-driver-whitespace":
                payload["selectedDriverId"] = "   ";
                break;
            case "selected-driver-too-long":
                payload["selectedDriverId"] = new string('d', 129);
                break;
            case "union-whitespace":
                payload["unionId"] = "   ";
                break;
            case "union-too-long":
                payload["unionId"] = new string('u', 129);
                break;
            case "pickup-whitespace":
                payload["pickup"] = "   ";
                break;
            case "pickup-too-long":
                payload["pickup"] = new string('p', 251);
                break;
            case "dropoff-whitespace":
                payload["dropoff"] = "   ";
                break;
            case "dropoff-too-long":
                payload["dropoff"] = new string('d', 251);
                break;
            case "passengers-below-range":
                payload["passengers"] = 0;
                break;
            case "passengers-above-range":
                payload["passengers"] = 7;
                break;
            case "latitude-out-of-range":
                payload["pickupLatitude"] = 91d;
                break;
            case "longitude-out-of-range":
                payload["pickupLongitude"] = 181d;
                break;
            case "idempotency-key-missing":
                idempotencyKey = "";
                break;
            case "idempotency-key-too-long":
                idempotencyKey = new string('k', 129);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, "Unknown invalid booking case.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rider/bookings")
        {
            Content = JsonContent.Create(payload)
        };
        if (idempotencyKey.Length > 0)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync(driverId));
        Assert.Empty(factory.Services.GetRequiredService<ConcurrentDictionary<string, RideIdempotencyRecord>>());
    }

    [Fact]
    public async Task SelectedBookingAcceptsMaximumSupportedRouteAndPassengerBoundaries()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "booking-boundary-driver";
        const string unionId = "booking-boundary-union";
        using var rider = factory.CreateClient("Rider", "booking-boundary-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles[driverId] = profiles[driverId] with { PassengerCapacity = 6 };
        var stops = Enumerable.Range(0, 10)
            .Select(index => new string((char)('A' + index), 250))
            .ToArray();
        var payload = new Dictionary<string, object?>
        {
            ["selectedDriverId"] = driverId,
            ["unionId"] = unionId,
            ["pickup"] = new string('p', 250),
            ["dropoff"] = new string('d', 250),
            ["stops"] = stops,
            ["passengers"] = 6,
            ["pickupLatitude"] = 17.493,
            ["pickupLongitude"] = 78.399
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rider/bookings")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("Idempotency-Key", "booking-boundary-valid-key");

        using var response = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(250, json.RootElement.GetProperty("pickup").GetString()!.Length);
        Assert.Equal(250, json.RootElement.GetProperty("dropoff").GetString()!.Length);
        Assert.Equal(6, json.RootElement.GetProperty("passengers").GetInt32());
        var persistedStops = json.RootElement.GetProperty("stops").EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        Assert.Equal(10, persistedStops.Length);
        Assert.All(persistedStops, stop => Assert.Equal(250, stop.Length));
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync(driverId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x")]
    public async Task SelectedBookingRejectsBlankOrOverlongIntermediateStopsBeforeReservation(string invalidStop)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "invalid-stop-rider-" + invalidStop.Length);
        using var driver = factory.CreateClient("Driver", "invalid-stop-driver");
        SeedBookableDriver(factory, "invalid-stop-driver", "invalid-stop-union");

        var stop = invalidStop.Length == 1 ? new string('x', 251) : invalidStop;
        using var request = CreateSelectedBookingRequest(
            "invalid-stop-key-" + stop.Length, "invalid-stop-driver", "invalid-stop-union",
            "KPHB", "HITEC City", [stop]);
        using var response = await rider.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync("invalid-stop-driver"));
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
        var originalBookingCreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        idempotency[idempotencyIndex] = idempotency[idempotencyIndex] with
        {
            CreatedAtUtc = originalBookingCreatedAtUtc
        };
        var originalNegotiationExpiryUtc = originalBookingCreatedAtUtc.AddMinutes(15);
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.True(await reservations.TrySetExpiryAsync(
            "recovery-booking-driver", rideId, originalNegotiationExpiryUtc));

        using var retry = CreateSelectedBookingRequest("recovery-booking-key", "recovery-booking-driver",
            "recovery-booking-union", "KPHB", "HITEC City", ["JNTU"]);
        using var recovered = await rider.SendAsync(retry);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        using var recoveredJson = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync());
        Assert.Equal(rideId, recoveredJson.RootElement.GetProperty("rideId").GetString());
        Assert.Equal(1, recoveredJson.RootElement.GetProperty("stops").GetArrayLength());
        Assert.Equal(originalBookingCreatedAtUtc,
            recoveredJson.RootElement.GetProperty("requestedAt").GetDateTimeOffset());
        Assert.Equal(originalNegotiationExpiryUtc,
            recoveredJson.RootElement.GetProperty("negotiationExpiresAtUtc").GetDateTimeOffset());
        var rideCreatedEvent = Assert.Single(rides[rideId].AuditEvents!,
            item => item.EventType == "RideCreated");
        Assert.Equal(originalBookingCreatedAtUtc, rideCreatedEvent.OccurredAtUtc);
        var recoveredReservation = await reservations.GetActiveAsync("recovery-booking-driver");
        Assert.NotNull(recoveredReservation);
        Assert.Equal(rideId, recoveredReservation.RideId);
        Assert.Equal(originalNegotiationExpiryUtc, recoveredReservation.ExpiresAtUtc);
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

    [Theory]
    [InlineData("Expired")]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task DirectRideReadReleasesReservationForAlreadyTerminalSelectedRide(string terminalStatus)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "selected-booking-driver";
        const string unionId = "direct-terminal-read-union";
        using var rider = factory.CreateClient("Rider", "direct-terminal-read-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var request = CreateSelectedBookingRequest("direct-terminal-read-key", driverId, unionId,
            "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;

        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();
        Assert.True(await reservations.TryMakePermanentAsync(driverId, rideId));
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with
        {
            Status = terminalStatus,
            NegotiationExpiresAtUtc = null
        };

        using var read = await rider.GetAsync($"/api/v1/rides/{rideId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(terminalStatus, readJson.RootElement.GetProperty("status").GetString());
        Assert.False(await reservations.HasActiveAsync(driverId));
    }

    [Fact]
    public async Task RiderAndDriverHistoryReadsRepairAlreadyTerminalSelectedRideReservations()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "selected-booking-driver";
        const string unionId = "terminal-history-read-union";
        using var rider = factory.CreateClient("Rider", "terminal-history-read-rider");
        using var secondRider = factory.CreateClient("Rider", "terminal-history-read-second-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();

        using var riderRequest = CreateSelectedBookingRequest("terminal-history-rider-key", driverId, unionId,
            "KPHB", "HITEC City", []);
        using var riderBooking = await rider.SendAsync(riderRequest);
        Assert.Equal(HttpStatusCode.Created, riderBooking.StatusCode);
        using var riderBookingJson = JsonDocument.Parse(await riderBooking.Content.ReadAsStringAsync());
        var riderRideId = riderBookingJson.RootElement.GetProperty("rideId").GetString()!;
        rides[riderRideId] = rides[riderRideId] with
        {
            Status = "Cancelled",
            NegotiationExpiresAtUtc = null
        };

        using var riderHistory = await rider.GetAsync("/api/v1/rides/mine");
        Assert.Equal(HttpStatusCode.OK, riderHistory.StatusCode);
        using var riderHistoryJson = JsonDocument.Parse(await riderHistory.Content.ReadAsStringAsync());
        Assert.Contains(riderHistoryJson.RootElement.EnumerateArray(),
            item => item.GetProperty("rideId").GetString() == riderRideId &&
                item.GetProperty("status").GetString() == "Cancelled");
        Assert.False(await reservations.HasActiveAsync(driverId));

        using var driverRequest = CreateSelectedBookingRequest("terminal-history-driver-key", driverId, unionId,
            "KPHB", "Madhapur", []);
        using var driverBooking = await secondRider.SendAsync(driverRequest);
        Assert.Equal(HttpStatusCode.Created, driverBooking.StatusCode);
        using var driverBookingJson = JsonDocument.Parse(await driverBooking.Content.ReadAsStringAsync());
        var driverRideId = driverBookingJson.RootElement.GetProperty("rideId").GetString()!;
        rides[driverRideId] = rides[driverRideId] with
        {
            Status = "Completed",
            NegotiationExpiresAtUtc = null
        };

        using var driverHistory = await driver.GetAsync("/api/v1/driver/rides/mine");
        Assert.Equal(HttpStatusCode.OK, driverHistory.StatusCode);
        using var driverHistoryJson = JsonDocument.Parse(await driverHistory.Content.ReadAsStringAsync());
        Assert.Contains(driverHistoryJson.RootElement.EnumerateArray(),
            item => item.GetProperty("rideId").GetString() == driverRideId &&
                item.GetProperty("status").GetString() == "Completed");
        Assert.False(await reservations.HasActiveAsync(driverId));
    }

    [Fact]
    public async Task TerminalRideReadDoesNotReleaseNewerReservationForSameDriver()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "selected-booking-driver";
        const string unionId = "newer-reservation-read-union";
        using var rider = factory.CreateClient("Rider", "newer-reservation-read-rider");
        using var secondRider = factory.CreateClient("Rider", "newer-reservation-read-second-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();

        using var oldRequest = CreateSelectedBookingRequest("older-terminal-read-key", driverId, unionId,
            "KPHB", "HITEC City", []);
        using var oldBooking = await rider.SendAsync(oldRequest);
        Assert.Equal(HttpStatusCode.Created, oldBooking.StatusCode);
        using var oldBookingJson = JsonDocument.Parse(await oldBooking.Content.ReadAsStringAsync());
        var oldRideId = oldBookingJson.RootElement.GetProperty("rideId").GetString()!;
        Assert.True(await reservations.TryReleaseAsync(driverId, oldRideId));
        // The old aggregate must be terminal before the new booking: the new historical-ride
        // guard correctly blocks a reservation-less active Negotiating ride.
        rides[oldRideId] = rides[oldRideId] with
        {
            Status = "Completed",
            NegotiationExpiresAtUtc = null
        };

        using var newerRequest = CreateSelectedBookingRequest("newer-active-read-key", driverId, unionId,
            "KPHB", "Madhapur", []);
        using var newerBooking = await secondRider.SendAsync(newerRequest);
        Assert.Equal(HttpStatusCode.Created, newerBooking.StatusCode);
        using var newerBookingJson = JsonDocument.Parse(await newerBooking.Content.ReadAsStringAsync());
        var newerRideId = newerBookingJson.RootElement.GetProperty("rideId").GetString()!;
        Assert.NotEqual(oldRideId, newerRideId);

        using var oldRideRead = await rider.GetAsync($"/api/v1/rides/{oldRideId}");
        Assert.Equal(HttpStatusCode.OK, oldRideRead.StatusCode);
        var activeReservation = await reservations.GetActiveAsync(driverId);
        Assert.NotNull(activeReservation);
        Assert.Equal(newerRideId, activeReservation.RideId);
    }

    [Fact]
    public async Task ConcurrentExpiredRideReadsKeepTerminalReservationCleanupIdempotent()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "selected-booking-driver";
        const string unionId = "concurrent-terminal-read-union";
        using var rider = factory.CreateClient("Rider", "concurrent-terminal-read-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var request = CreateSelectedBookingRequest("concurrent-terminal-read-key", driverId, unionId,
            "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with
        {
            NegotiationExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
        };

        var responses = await Task.WhenAll(
            rider.GetAsync($"/api/v1/rides/{rideId}"),
            rider.GetAsync($"/api/v1/rides/{rideId}"));
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("Expired", json.RootElement.GetProperty("status").GetString());
            }
        }

        Assert.Single(rides[rideId].AuditEvents!,
            item => item.EventType == "RideStatusChanged" && item.NewStatus == "Expired");
        Assert.False(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync(driverId));
    }

    [Fact]
    public async Task UnrelatedRiderCannotTriggerTerminalReservationRepairForAnotherRidersRide()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "selected-booking-driver";
        const string unionId = "terminal-read-owner-union";
        using var owner = factory.CreateClient("Rider", "terminal-read-owner");
        using var unrelatedRider = factory.CreateClient("Rider", "terminal-read-unrelated");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var request = CreateSelectedBookingRequest("terminal-read-owner-key", driverId, unionId,
            "KPHB", "HITEC City", []);
        using var booking = await owner.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with { Status = "Completed", NegotiationExpiresAtUtc = null };
        var reservations = factory.Services.GetRequiredService<IDriverRideReservationStore>();

        using var unauthorizedRead = await unrelatedRider.GetAsync($"/api/v1/rides/{rideId}");
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedRead.StatusCode);
        Assert.True(await reservations.HasActiveAsync(driverId));

        using var ownerRead = await owner.GetAsync($"/api/v1/rides/{rideId}");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        Assert.False(await reservations.HasActiveAsync(driverId));
    }

    [Fact]
    public async Task TerminalNonCabHistoricalRideReadDoesNotReleaseItsReservation()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "selected-booking-driver";
        const string unionId = "terminal-legacy-noncab-union";
        using var rider = factory.CreateClient("Rider", "terminal-legacy-noncab-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, unionId);

        using var request = CreateSelectedBookingRequest("terminal-legacy-noncab-key", driverId, unionId,
            "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with
        {
            ServiceClass = "auto",
            Status = "Completed",
            NegotiationExpiresAtUtc = null
        };

        using var read = await rider.GetAsync($"/api/v1/rides/{rideId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .HasActiveAsync(driverId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TerminalCabHistoricalRideWithoutUnionDoesNotReleaseItsReservation(string? unionId)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "selected-booking-driver";
        const string legacyUnionId = "terminal-legacy-nonunion-cab-union";
        using var rider = factory.CreateClient("Rider", "terminal-legacy-nonunion-cab-rider");
        using var driver = factory.CreateClient("Driver", driverId);
        SeedBookableDriver(factory, driverId, legacyUnionId);

        using var request = CreateSelectedBookingRequest(
            "terminal-legacy-nonunion-cab-key-" + (unionId?.Length ?? -1), driverId, legacyUnionId,
            "KPHB", "HITEC City", []);
        using var booking = await rider.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        using var bookingJson = JsonDocument.Parse(await booking.Content.ReadAsStringAsync());
        var rideId = bookingJson.RootElement.GetProperty("rideId").GetString()!;
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        rides[rideId] = rides[rideId] with
        {
            UnionId = unionId,
            Status = "Completed",
            NegotiationExpiresAtUtc = null
        };

        using var read = await rider.GetAsync($"/api/v1/rides/{rideId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var activeReservation = await factory.Services.GetRequiredService<IDriverRideReservationStore>()
            .GetActiveAsync(driverId);
        Assert.NotNull(activeReservation);
        Assert.Equal(rideId, activeReservation.RideId);
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

    private static DemoRide CreateHistoricalRide(
        string rideId,
        string riderId,
        string driverId,
        string status,
        string serviceClass = "cab",
        string? unionId = null,
        DateTimeOffset? negotiationExpiresAtUtc = null)
    {
        var now = DateTimeOffset.UtcNow;
        var acceptedAt = status is "Accepted" or "Arrived" or "InProgress"
            ? now.AddMinutes(-5)
            : (DateTimeOffset?)null;
        return new DemoRide(
            rideId, "KPHB", "HITEC City", 1, status, now.AddMinutes(-10), null,
            riderId, driverId, 17.493, 78.399, null, null, acceptedAt, now,
            false, serviceClass, "hyderabad-central",
            UnionId: unionId,
            NegotiationExpiresAtUtc: negotiationExpiresAtUtc);
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

    private static void SeedTransferTargetUnion(TurnOvaApiFactory factory, string unionId)
    {
        var now = DateTimeOffset.UtcNow;
        factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnion>>()[unionId] =
            new DriverUnion(unionId, "Historical Transfer Target", "HIST-" + unionId, "Hyderabad",
                null, null, ["hyderabad-central"], ["cab"], "Active", now, now);
    }

    private static DispatchZoneConfig[] CreateAmbiguousDispatchZones(bool reverseZoneOrder, bool sharedBoundary)
    {
        var central = new DispatchZoneConfig("hyderabad-central", 17.2, 17.6, 78.2, 78.6);
        var overlapping = sharedBoundary
            ? new DispatchZoneConfig("hyderabad-east", 17.6, 17.8, 78.2, 78.6)
            : new DispatchZoneConfig("hyderabad-east", 17.4, 17.8, 78.3, 78.7);
        return reverseZoneOrder ? new[] { overlapping, central } : new[] { central, overlapping };
    }

    private static HttpRequestMessage CreateSelectedBookingRequest(
        string key, string driverId, string unionId, string pickup, string dropoff, string[] stops,
        double pickupLatitude = 17.493, double pickupLongitude = 78.399)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rider/bookings")
        {
            Content = JsonContent.Create(new
            {
                selectedDriverId = driverId, unionId, pickup, dropoff, stops,
                passengers = 1, pickupLatitude, pickupLongitude
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
        Assert.Equal(JsonValueKind.Null, versions[0].GetProperty("riderConfirmedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, versions[0].GetProperty("driverConfirmedAtUtc").ValueKind);
        foreach (var confirmedVersion in versions.Skip(1))
        {
            Assert.Equal(JsonValueKind.String, confirmedVersion.GetProperty("riderConfirmedAtUtc").ValueKind);
            Assert.Equal(JsonValueKind.String, confirmedVersion.GetProperty("driverConfirmedAtUtc").ValueKind);
            var riderConfirmedAt = DateTimeOffset.Parse(confirmedVersion.GetProperty("riderConfirmedAtUtc").GetString()!);
            var driverConfirmedAt = DateTimeOffset.Parse(confirmedVersion.GetProperty("driverConfirmedAtUtc").GetString()!);
            Assert.True(riderConfirmedAt <= driverConfirmedAt);
        }
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

    [Theory]
    [InlineData(451.00, "INR")]
    [InlineData(450.00, "USD")]
    public async Task RideAgreementIdempotencyRejectsChangedAmountOrCurrency(decimal retryAmount, string retryCurrency)
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "agreement-idempotency-rider");
        using var driver = factory.CreateClient("Driver", "agreement-idempotency-driver");
        const string rideId = "RIDE-AGREEMENT-IDEMPOTENCY";
        const string key = "agreement-idempotency-same-key";
        SeedAgreementRide(factory, rideId, "agreement-idempotency-rider", "agreement-idempotency-driver");

        HttpRequestMessage Proposal(decimal amount, string currency)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
            {
                Content = JsonContent.Create(new { amount, currency })
            };
            request.Headers.Add("Idempotency-Key", key);
            return request;
        }

        using var first = Proposal(450.00m, "INR");
        using var firstResponse = await rider.SendAsync(first);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var storedBeforeRetry = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()
            .GetValueOrDefault(rideId)!.AgreementVersions!;
        Assert.Single(storedBeforeRetry);
        Assert.Matches("^[A-F0-9]{64}$", storedBeforeRetry[0].RequestHash!);

        using var changed = Proposal(retryAmount, retryCurrency);
        using var changedResponse = await rider.SendAsync(changed);
        Assert.Equal(HttpStatusCode.Conflict, changedResponse.StatusCode);

        var storedAfterRetry = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()
            .GetValueOrDefault(rideId)!.AgreementVersions!;
        Assert.Single(storedAfterRetry);
        Assert.Equal(450.00m, storedAfterRetry[0].Amount);
        Assert.Equal("INR", storedAfterRetry[0].Currency);
    }

    [Fact]
    public async Task LegacyAgreementWithoutRequestHashReplaysOnlyMatchingStoredTerms()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "agreement-legacy-idempotency-rider");
        using var driver = factory.CreateClient("Driver", "agreement-legacy-idempotency-driver");
        const string rideId = "RIDE-AGREEMENT-LEGACY-IDEMPOTENCY";
        const string key = "agreement-legacy-idempotency-key";
        SeedAgreementRide(factory, rideId, "agreement-legacy-idempotency-rider", "agreement-legacy-idempotency-driver");

        HttpRequestMessage Proposal(decimal amount)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
            {
                Content = JsonContent.Create(new { amount, currency = "INR" })
            };
            request.Headers.Add("Idempotency-Key", key);
            return request;
        }

        using var first = Proposal(450m);
        using var firstResponse = await rider.SendAsync(first);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var persisted = rides[rideId];
        rides[rideId] = persisted with
        {
            AgreementVersions = [persisted.AgreementVersions![0] with { RequestHash = null }]
        };

        using var matchingRetry = Proposal(450.00m);
        using var matchingResponse = await rider.SendAsync(matchingRetry);
        Assert.Equal(HttpStatusCode.OK, matchingResponse.StatusCode);

        using var changedRetry = Proposal(451m);
        using var changedResponse = await rider.SendAsync(changedRetry);
        Assert.Equal(HttpStatusCode.Conflict, changedResponse.StatusCode);
        Assert.Single(rides[rideId].AgreementVersions!);
        Assert.Null(rides[rideId].AgreementVersions![0].RequestHash);
    }

    [Fact]
    public async Task ConcurrentSameKeyAgreementProposalsRejectDifferentPayloads()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var riderOne = factory.CreateClient("Rider", "agreement-idempotency-race-rider");
        using var riderTwo = factory.CreateClient("Rider", "agreement-idempotency-race-rider");
        using var driver = factory.CreateClient("Driver", "agreement-idempotency-race-driver");
        const string rideId = "RIDE-AGREEMENT-IDEMPOTENCY-RACE";
        const string key = "agreement-idempotency-race-same-key";
        SeedAgreementRide(factory, rideId, "agreement-idempotency-race-rider", "agreement-idempotency-race-driver");

        HttpRequestMessage Proposal(decimal amount)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
            {
                Content = JsonContent.Create(new { amount, currency = "INR" })
            };
            request.Headers.Add("Idempotency-Key", key);
            return request;
        }

        using var first = Proposal(450m);
        using var second = Proposal(475m);
        var responses = await Task.WhenAll(riderOne.SendAsync(first), riderTwo.SendAsync(second));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();

        var stored = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()
            .GetValueOrDefault(rideId)!.AgreementVersions!;
        Assert.Single(stored);
        Assert.Contains(stored[0].Amount, new[] { 450m, 475m });
        Assert.Equal("INR", stored[0].Currency);
        Assert.Matches("^[A-F0-9]{64}$", stored[0].RequestHash!);
    }

    [Fact]
    public async Task RideAgreementIdempotencyNormalizesEquivalentDecimalScale()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var rider = factory.CreateClient("Rider", "agreement-scale-idempotency-rider");
        using var driver = factory.CreateClient("Driver", "agreement-scale-idempotency-driver");
        const string rideId = "RIDE-AGREEMENT-SCALE-IDEMPOTENCY";
        const string key = "agreement-scale-idempotency-key";
        SeedAgreementRide(factory, rideId, "agreement-scale-idempotency-rider", "agreement-scale-idempotency-driver");

        HttpRequestMessage Proposal(decimal amount)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
            {
                Content = JsonContent.Create(new { amount, currency = "INR" })
            };
            request.Headers.Add("Idempotency-Key", key);
            return request;
        }

        using var first = Proposal(450.0m);
        using var firstResponse = await rider.SendAsync(first);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var equivalentRetry = Proposal(450.00m);
        using var retryResponse = await rider.SendAsync(equivalentRetry);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Single(factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()
            .GetValueOrDefault(rideId)!.AgreementVersions!);
    }

    [Fact]
    public async Task ConcurrentSameKeyAgreementProposalsWithSamePayloadReturnOneVersion()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        using var riderOne = factory.CreateClient("Rider", "agreement-same-payload-race-rider");
        using var riderTwo = factory.CreateClient("Rider", "agreement-same-payload-race-rider");
        using var driver = factory.CreateClient("Driver", "agreement-same-payload-race-driver");
        const string rideId = "RIDE-AGREEMENT-SAME-PAYLOAD-RACE";
        const string key = "agreement-same-payload-race-key";
        SeedAgreementRide(factory, rideId, "agreement-same-payload-race-rider", "agreement-same-payload-race-driver");

        HttpRequestMessage Proposal()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rides/{rideId}/agreements")
            {
                Content = JsonContent.Create(new { amount = 450m, currency = "INR" })
            };
            request.Headers.Add("Idempotency-Key", key);
            return request;
        }

        using var first = Proposal();
        using var second = Proposal();
        var responses = await Task.WhenAll(riderOne.SendAsync(first), riderTwo.SendAsync(second));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        foreach (var response in responses) response.Dispose();

        var stored = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>()
            .GetValueOrDefault(rideId)!.AgreementVersions!;
        Assert.Single(stored);
        Assert.Equal(450m, stored[0].Amount);
        Assert.Equal("INR", stored[0].Currency);
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
    public async Task CompletedSelectedRidePersistsPrivacyMinimisedAuditEventsForAgreementLifecycleAndPayment()
    {

        var auditMeasurements = new ConcurrentBag<(string EventType, string Outcome, string[] TagKeys)>();
        var auditActivities = new ConcurrentBag<Activity>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurnOvaDiagnostics.MeterName &&
                instrument.Name == "turnova.ride.audit.write_attempts")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var values = tags.ToArray();
            var eventType = values.FirstOrDefault(tag => tag.Key == "event_type").Value?.ToString() ?? "";
            var outcome = values.FirstOrDefault(tag => tag.Key == "outcome").Value?.ToString() ?? "";
            auditMeasurements.Add((eventType, outcome, values.Select(tag => tag.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray()));
        });
        meterListener.Start();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurnOvaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => auditActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string riderId = "ride-audit-rider";
        const string driverId = "ride-audit-driver";
        const string unionId = "ride-audit-union";
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, unionId, "ride-audit-booking", 500m);

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var beforeReport = rides[rideId];
        var beforeEvents = beforeReport.AuditEvents!;
        Assert.Equal(new[]
        {
            "RideCreated",
            "AgreementProposed",
            "AgreementPartyConfirmed",
            "AgreementPartyConfirmed",
            "RideStatusChanged",
            "RideStatusChanged",
            "RideStatusChanged",
            "RideStatusChanged"
        }, beforeEvents.Select(item => item.EventType).ToArray());
        Assert.All(beforeEvents, item =>
        {
            Assert.Equal(rideId, item.RideId);
            Assert.Equal(unionId, item.UnionId);
            Assert.NotEqual(default, item.OccurredAtUtc);
            Assert.Equal(TimeSpan.Zero, item.OccurredAtUtc.Offset);
        });
        Assert.Equal(riderId, beforeEvents[0].ActorId);
        Assert.Equal("Rider", beforeEvents[0].ActorRole);
        Assert.Equal("Negotiating", beforeEvents[0].NewStatus);
        Assert.Equal("Rider", beforeEvents[1].ActorRole);
        Assert.Equal(1, beforeEvents[1].AgreementVersion);
        Assert.Equal(500m, beforeEvents[1].Amount);
        Assert.Equal("INR", beforeEvents[1].Currency);
        Assert.Contains(beforeEvents, item =>
            item.EventType == "AgreementPartyConfirmed" && item.ActorId == riderId && item.ActorRole == "Rider");
        Assert.Contains(beforeEvents, item =>
            item.EventType == "AgreementPartyConfirmed" && item.ActorId == driverId && item.ActorRole == "Driver");
        var statusEvents = beforeEvents.Where(item => item.EventType == "RideStatusChanged").ToArray();
        Assert.Equal(new[] { "Accepted", "Arrived", "InProgress", "Completed" },
            statusEvents.Select(item => item.NewStatus).ToArray());
        Assert.All(statusEvents, item =>
        {
            Assert.Equal(driverId, item.ActorId);
            Assert.Equal("Driver", item.ActorRole);
        });
        Assert.Equal(beforeEvents.Count, beforeEvents.Select(item => item.EventId).Distinct().Count());

        using var reportRequest = CreatePaymentReportRequest(rideId, "ride-audit-payment-key", 450m);
        using var reportResponse = await driver.SendAsync(reportRequest);
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var afterReport = rides[rideId];
        var paymentEvent = Assert.Single(afterReport.AuditEvents!,
            item => item.EventType == "PaymentReportedUnverified");
        Assert.Equal(driverId, paymentEvent.ActorId);
        Assert.Equal("Driver", paymentEvent.ActorRole);
        Assert.Equal("Completed", paymentEvent.PreviousStatus);
        Assert.Equal("Completed", paymentEvent.NewStatus);
        Assert.Equal(1, paymentEvent.AgreementVersion);
        Assert.Equal(500m, paymentEvent.Amount);
        Assert.Equal(450m, paymentEvent.AmountReceived);
        Assert.Equal(50m, paymentEvent.Delta);
        Assert.Equal("INR", paymentEvent.Currency);
        Assert.Equal(afterReport.PaymentReconciliation!.ReportedAtUtc, paymentEvent.OccurredAtUtc);

        var auditJson = JsonSerializer.Serialize(afterReport.AuditEvents);
        Assert.False(auditJson.Contains("KPHB", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("HITEC City", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("17.493", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("pickup", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("dropoff", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("stops", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("latitude", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("longitude", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("phone", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("email", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("document", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("kyc", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditJson.Contains("chat", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(auditMeasurements, item => item.EventType == "ride_created" && item.Outcome == "success");
        Assert.Contains(auditMeasurements, item => item.EventType == "agreement_proposed" && item.Outcome == "success");
        Assert.Contains(auditMeasurements, item => item.EventType == "agreement_party_confirmed" && item.Outcome == "success");
        Assert.Contains(auditMeasurements, item => item.EventType == "ride_status_changed" && item.Outcome == "success");
        Assert.Contains(auditMeasurements, item => item.EventType == "payment_reported_unverified" && item.Outcome == "success");
        Assert.All(auditMeasurements, item =>
            Assert.Equal(new[] { "event_type", "outcome" }, item.TagKeys));
        Assert.Contains(auditActivities, item =>
            item.OperationName == "ride.audit.write" &&
            Equals(item.GetTagItem("turnova.audit.event_type"), "payment_reported_unverified") &&
            Equals(item.GetTagItem("turnova.audit.outcome"), "success"));
        var rideAuditActivities = auditActivities.Where(item => item.OperationName == "ride.audit.write").ToArray();
        Assert.NotEmpty(rideAuditActivities);
        Assert.All(rideAuditActivities, item =>
            Assert.Equal(
                new[] { "turnova.audit.event_type", "turnova.audit.outcome" },
                item.Tags.Select(tag => tag.Key).ToArray()));

        var eventCount = afterReport.AuditEvents!.Count;
        using var retryRequest = CreatePaymentReportRequest(rideId, "ride-audit-payment-key", 450m);
        using var retryResponse = await driver.SendAsync(retryRequest);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(eventCount, rides[rideId].AuditEvents!.Count);
        Assert.Single(rides[rideId].AuditEvents!,
            item => item.EventType == "PaymentReportedUnverified");
    }

    [Fact]
    public async Task AgreementConfirmationRetryDoesNotDuplicateAuditEvents()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string riderId = "ride-audit-confirm-rider";
        const string driverId = "ride-audit-confirm-driver";
        using var rider = factory.CreateClient("Rider", riderId);
        using var driver = factory.CreateClient("Driver", driverId);
        var rideId = await CreateSelectedRideWithConfirmedAgreementAsync(
            factory, rider, driver, driverId, "ride-audit-confirm-union", "ride-audit-confirm", 500m);

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var beforeCount = rides[rideId].AuditEvents!.Count;
        Assert.Equal(2, rides[rideId].AuditEvents!.Count(item => item.EventType == "AgreementPartyConfirmed"));

        using var retry = await rider.PostAsync($"/api/v1/rides/{rideId}/agreements/1/confirm", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(beforeCount, rides[rideId].AuditEvents!.Count);
        Assert.Equal(2, rides[rideId].AuditEvents!.Count(item => item.EventType == "AgreementPartyConfirmed"));
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

        var driverHistory = await driver.GetFromJsonAsync<List<JsonElement>>("/api/v1/driver/rides/mine");
        var driverHistoryItem = Assert.Single(driverHistory!);
        var currentAgreement = driverHistoryItem.GetProperty("currentAgreement");
        Assert.Equal(1, currentAgreement.GetProperty("version").GetInt32());
        Assert.Equal(500m, currentAgreement.GetProperty("amount").GetDecimal());
        Assert.Equal("USD", currentAgreement.GetProperty("currency").GetString());
        if (driverHistoryItem.TryGetProperty("paymentReconciliation", out var existingReport))
            Assert.Equal(JsonValueKind.Null, existingReport.ValueKind);

        var riderHistory = await rider.GetFromJsonAsync<List<JsonElement>>("/api/v1/rides/mine");
        var riderHistoryItem = Assert.Single(riderHistory!);
        Assert.False(riderHistoryItem.TryGetProperty("currentAgreement", out _));
        Assert.False(riderHistoryItem.TryGetProperty("paymentReconciliation", out _));

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


    [Fact]
    public async Task DriverPaymentReportRejectsHistoricalNonCabRideWithoutMutation()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-legacy-noncab-driver";
        const string unionId = "payment-legacy-noncab-union";
        using var rider = factory.CreateClient("Rider", "payment-legacy-noncab-rider");
        using var driver = factory.CreateClient("Driver", driverId);

        var cabRideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, unionId, "payment-legacy-noncab", 500m);
        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var legacyRideId = "legacy-scooty-payment-" + cabRideId;
        rides[legacyRideId] = rides[cabRideId] with
        {
            RideId = legacyRideId,
            ServiceClass = "Scooty"
        };
        var legacyAuditCount = rides[legacyRideId].AuditEvents?.Count ?? 0;

        using var request = CreatePaymentReportRequest(
            legacyRideId, "payment-legacy-noncab-report", 450m);
        using var response = await driver.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Scooty", rides[legacyRideId].ServiceClass);
        Assert.Equal("Completed", rides[legacyRideId].Status);
        Assert.Null(rides[legacyRideId].PaymentReconciliation);
        Assert.Equal(legacyAuditCount, rides[legacyRideId].AuditEvents?.Count ?? 0);
        Assert.Equal("cab", rides[cabRideId].ServiceClass);
        Assert.Null(rides[cabRideId].PaymentReconciliation);
    }

    [Fact]
    public async Task UnionReconciliationExcludesLegacyNonCabRecordsAndKeepsMixedCaseCabRecords()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-legacy-cab-driver";
        const string unionId = "payment-legacy-cab-union";
        using var rider = factory.CreateClient("Rider", "payment-legacy-cab-rider");
        using var driver = factory.CreateClient("Driver", driverId);

        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, unionId, "payment-legacy-cab", 500m);
        using var reportRequest = CreatePaymentReportRequest(
            rideId, "payment-legacy-cab-report", 450m);
        using var reportResponse = await driver.SendAsync(reportRequest);
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var cabRide = rides[rideId] with { ServiceClass = "CAB" };
        rides[rideId] = cabRide;

        // Simulate a retained historical non-Cab aggregate in the in-memory fallback.
        // It carries the same legacy report so the endpoint must filter it, not migrate it.
        var legacyRideId = "legacy-scooty-" + rideId;
        rides[legacyRideId] = cabRide with
        {
            RideId = legacyRideId,
            ServiceClass = "Scooty"
        };

        using var manager = factory.CreateUnionManagerClient(unionId, "payment-legacy-cab-manager");
        using var response = await manager.GetAsync("/api/v1/union/reconciliation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var record = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(rideId, record.GetProperty("rideId").GetString());
        Assert.Equal(unionId, record.GetProperty("unionId").GetString());
        Assert.Equal(450m, record.GetProperty("amountReceived").GetDecimal());
        Assert.Equal("Scooty", rides[legacyRideId].ServiceClass);
        Assert.Equal(450m, rides[legacyRideId].PaymentReconciliation!.AmountReceived);
    }

    [Fact]
    public async Task UnionReconciliationExcludesReportWhoseRideIdDoesNotMatchAggregate()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-aggregate-identity-driver";
        const string unionId = "payment-aggregate-identity-union";
        using var rider = factory.CreateClient("Rider", "payment-aggregate-identity-rider");
        using var driver = factory.CreateClient("Driver", driverId);

        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, unionId, "payment-aggregate-identity", 500m);
        using var reportRequest = CreatePaymentReportRequest(
            rideId, "payment-aggregate-identity-report", 450m);
        using var reportResponse = await driver.SendAsync(reportRequest);
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);

        var rides = factory.Services.GetRequiredService<ConcurrentDictionary<string, DemoRide>>();
        var mismatchedAggregateId = "aggregate-mismatch-" + rideId;
        rides[mismatchedAggregateId] = rides[rideId] with { RideId = mismatchedAggregateId };
        Assert.Equal(rideId, rides[mismatchedAggregateId].PaymentReconciliation!.RideId);

        using var manager = factory.CreateUnionManagerClient(unionId, "payment-aggregate-identity-manager");
        using var response = await manager.GetAsync("/api/v1/union/reconciliation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var record = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(rideId, record.GetProperty("rideId").GetString());
        Assert.NotEqual(mismatchedAggregateId, record.GetProperty("rideId").GetString());
        Assert.Equal(rideId, rides[mismatchedAggregateId].PaymentReconciliation!.RideId);
    }


    [Fact]
    public async Task UnionReconciliationRemainsLinkedToOriginalRideUnionAfterDriverTransfer()
    {
        using var factory = new TurnOvaApiFactory(legacyFifoRiderRequestsEnabled: false);
        const string driverId = "payment-transfer-driver";
        const string originalUnionId = "payment-transfer-original-union";
        using var rider = factory.CreateClient("Rider", "payment-transfer-rider");
        using var driver = factory.CreateClient("Driver", driverId);

        var rideId = await CreateCompletedSelectedRideAsync(
            factory, rider, driver, driverId, originalUnionId, "payment-transfer-ride", 500m);
        using var reportRequest = CreatePaymentReportRequest(
            rideId, "payment-transfer-report", 450m);
        using var reportResponse = await driver.SendAsync(reportRequest);
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);

        using var ops = factory.CreateClient("Rider", "payment-transfer-ops");
        using var createUnionRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ops/unions")
        {
            Content = JsonContent.Create(new
            {
                name = "Payment Transfer Target Union",
                code = "PAYMENT-TRANSFER-TARGET",
                location = "Hyderabad",
                managerName = (string?)null,
                managerPhone = (string?)null,
                zoneIds = new[] { "hyderabad-central" },
                serviceClasses = new[] { "cab" }
            })
        };
        createUnionRequest.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var createUnionResponse = await ops.SendAsync(createUnionRequest);
        Assert.Equal(HttpStatusCode.Created, createUnionResponse.StatusCode);
        using var unionJson = JsonDocument.Parse(await createUnionResponse.Content.ReadAsStringAsync());
        var targetUnionId = unionJson.RootElement.GetProperty("unionId").GetString()!;

        using var transferRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/ops/drivers/{driverId}/union-assignment")
        {
            Content = JsonContent.Create(new
            {
                unionId = targetUnionId,
                reason = "Completed ride reconciliation remains with the original union"
            })
        };
        transferRequest.Headers.Add("X-Driver-Admin-Key", "integration-test-admin-key-32chars");
        using var transferResponse = await ops.SendAsync(transferRequest);
        Assert.Equal(HttpStatusCode.OK, transferResponse.StatusCode);

        using var originalManager = factory.CreateUnionManagerClient(
            originalUnionId, "payment-transfer-original-manager");
        using var originalResponse = await originalManager.GetAsync("/api/v1/union/reconciliation");
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
        using var originalJson = JsonDocument.Parse(await originalResponse.Content.ReadAsStringAsync());
        var originalRecord = Assert.Single(originalJson.RootElement.EnumerateArray());
        Assert.Equal(rideId, originalRecord.GetProperty("rideId").GetString());
        Assert.Equal(originalUnionId, originalRecord.GetProperty("unionId").GetString());
        Assert.Equal(450m, originalRecord.GetProperty("amountReceived").GetDecimal());

        var driverHistory = await driver.GetFromJsonAsync<List<JsonElement>>("/api/v1/driver/rides/mine");
        var driverRecord = Assert.Single(driverHistory!);
        var driverReport = driverRecord.GetProperty("paymentReconciliation");
        Assert.Equal(originalUnionId, driverReport.GetProperty("unionId").GetString());
        Assert.Equal(driverId, driverReport.GetProperty("reportedByDriverId").GetString());
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

    // Callers must create the Driver test client first so the factory registers its approved onboarding profile;
    // this helper seeds the active Cab Union membership and ride aggregate only.
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

    [Fact]
    public async Task RiderTripIntentDraftRequiresReviewAndDriverCannotUseRiderDraftEndpoint()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-intent-rider");
        using var driver = factory.CreateClient("Driver", "ai-intent-driver");

        using var response = await rider.PostAsJsonAsync("/api/v1/assistance/trip-intent", new
        {
            text = "Please book a Cab from Nizampet to HITEC City for 2 passengers"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<TripIntentDraftResponse>();
        Assert.NotNull(draft);
        Assert.Equal("Nizampet", draft.Pickup);
        Assert.Equal("HITEC City", draft.Dropoff);
        Assert.Equal("Cab", draft.ServiceClass);
        Assert.Equal(2, draft.PassengerCount);
        Assert.True(draft.RequiresUserConfirmation);
        Assert.Equal("deterministic-fallback", draft.Source);

        using var historyResponse = await rider.GetAsync("/api/v1/rides/mine");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        Assert.Equal(0, history.RootElement.GetArrayLength());

        using var forbidden = await driver.PostAsJsonAsync("/api/v1/assistance/trip-intent", new
        {
            text = "Cab from Nizampet to HITEC City"
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task RiderAndDriverCanUseApprovedHelpAndUnsupportedQuestionsEscalate()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-help-rider");
        using var driver = factory.CreateClient("Driver", "ai-help-driver");

        using var stateResponse = await rider.PostAsJsonAsync("/api/v1/assistance/help", new
        {
            question = "Explain my current trip",
            rideState = "InProgress"
        });
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        var stateHelp = await stateResponse.Content.ReadFromJsonAsync<HelpResponse>();
        Assert.NotNull(stateHelp);
        Assert.Equal("state-in-progress", stateHelp.SourceId);
        Assert.Contains("foreground", stateHelp.Answer, StringComparison.OrdinalIgnoreCase);

        using var driverResponse = await driver.PostAsJsonAsync("/api/v1/assistance/help", new
        {
            question = "How is the fare negotiated?",
            rideState = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, driverResponse.StatusCode);
        var driverHelp = await driverResponse.Content.ReadFromJsonAsync<HelpResponse>();
        Assert.NotNull(driverHelp);
        Assert.False(driverHelp.EscalationRequired);

        using var unsupportedResponse = await rider.PostAsJsonAsync("/api/v1/assistance/help", new
        {
            question = "What is the weather on Mars?",
            rideState = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, unsupportedResponse.StatusCode);
        var unsupported = await unsupportedResponse.Content.ReadFromJsonAsync<HelpResponse>();
        Assert.NotNull(unsupported);
        Assert.True(unsupported.EscalationRequired);
        Assert.Equal("unsupported", unsupported.SourceId);
    }

    [Fact]
    public async Task AssistantRateLimitIsAppliedPerAuthenticatedCaller()
    {
        using var factory = new TurnOvaApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-rate-limit-rider");

        for (var index = 0; index < 10; index++)
        {
            using var response = await rider.PostAsJsonAsync("/api/v1/assistance/help", new
            {
                question = "How is the fare negotiated?",
                rideState = (string?)null
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var limited = await rider.PostAsJsonAsync("/api/v1/assistance/help", new
        {
            question = "How is the fare negotiated?",
            rideState = (string?)null
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
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

    private sealed class RecordingRideOfferAdvancer : IRideOfferAdvancer
    {
        public ConcurrentQueue<string> AdvancedRideIds { get; } = new();

        public Task<bool> AdvanceAsync(string rideId, CancellationToken cancellationToken = default)
        {
            AdvancedRideIds.Enqueue(rideId);
            return Task.FromResult(true);
        }
    }

    private sealed class QueueReadFailureStore(IQueueStore inner) : IQueueStore
    {
        private int _failNextRead;

        public void FailNextRead() => Interlocked.Exchange(ref _failNextRead, 1);

        public Task<IReadOnlyList<DriverQueueEntry>> GetEligibleAsync(
            string zoneId, string serviceClass, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _failNextRead, 0) == 1)
                throw new InvalidOperationException("sensitive-queue-read-failure");
            return inner.GetEligibleAsync(zoneId, serviceClass, cancellationToken);
        }

        public Task AddAsync(DriverQueueEntry entry, CancellationToken cancellationToken) =>
            inner.AddAsync(entry, cancellationToken);

        public Task<bool> RemoveAsync(
            string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken) =>
            inner.RemoveAsync(driverId, zoneId, serviceClass, cancellationToken);

        public Task<bool> MoveToBackAsync(
            string driverId, string zoneId, string serviceClass, CancellationToken cancellationToken) =>
            inner.MoveToBackAsync(driverId, zoneId, serviceClass, cancellationToken);

        public Task<bool> MoveToBackOnceAsync(
            string driverId, string zoneId, string serviceClass, string offerId, CancellationToken cancellationToken) =>
            inner.MoveToBackOnceAsync(driverId, zoneId, serviceClass, offerId, cancellationToken);

        public Task RecordDispatchExceptionAsync(
            string actorId, string zoneId, string serviceClass, string traceId, string reasonCategory,
            CancellationToken cancellationToken) =>
            inner.RecordDispatchExceptionAsync(
                actorId, zoneId, serviceClass, traceId, reasonCategory, cancellationToken);

        public Task<IReadOnlyList<QueueAuditEvent>> GetAuditEventsAsync(
            string zoneId, string serviceClass, CancellationToken cancellationToken) =>
            inner.GetAuditEventsAsync(zoneId, serviceClass, cancellationToken);
    }

    private sealed class ReservationMakePermanentFailureStore(IDriverRideReservationStore inner)
        : IDriverRideReservationStore
    {
        private int _failNextMakePermanent;
        private int _failAfterMakePermanent;

        public void FailNextMakePermanent() => Interlocked.Exchange(ref _failNextMakePermanent, 1);
        public void FailAfterNextMakePermanent() => Interlocked.Exchange(ref _failAfterMakePermanent, 1);

        public Task<bool> TryAcquireAsync(
            string driverId, string rideId, CancellationToken cancellationToken = default,
            DriverRideReservationAuditContext? auditContext = null) =>
            inner.TryAcquireAsync(driverId, rideId, cancellationToken, auditContext);

        public Task<bool> TryAcquireTemporaryAsync(
            string driverId, string rideId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default,
            DriverRideReservationAuditContext? auditContext = null) =>
            inner.TryAcquireTemporaryAsync(driverId, rideId, expiresAtUtc, cancellationToken, auditContext);

        public async Task<bool> TryMakePermanentAsync(
            string driverId, string rideId, CancellationToken cancellationToken = default,
            DriverRideReservationAuditContext? auditContext = null)
        {
            if (Interlocked.Exchange(ref _failNextMakePermanent, 0) == 1)
                throw new InvalidOperationException("sensitive-reservation-store-failure");
            var result = await inner.TryMakePermanentAsync(driverId, rideId, cancellationToken, auditContext);
            if (result && Interlocked.Exchange(ref _failAfterMakePermanent, 0) == 1)
                throw new InvalidOperationException("sensitive-reservation-post-commit-timeout");
            return result;
        }

        public Task<bool> TrySetExpiryAsync(
            string driverId, string rideId, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken = default,
            DriverRideReservationAuditContext? auditContext = null) =>
            inner.TrySetExpiryAsync(driverId, rideId, expiresAtUtc, cancellationToken, auditContext);

        private Action? _beforeNextGetActive;

        public void BeforeNextGetActive(Action callback) =>
            Interlocked.Exchange(ref _beforeNextGetActive, callback);

        public Task<DriverRideReservation?> GetActiveAsync(
            string driverId, CancellationToken cancellationToken = default)
        {
            Interlocked.Exchange(ref _beforeNextGetActive, null)?.Invoke();
            return inner.GetActiveAsync(driverId, cancellationToken);
        }

        public Task<IReadOnlyList<DriverRideReservation>> GetActiveForDriversAsync(
            IReadOnlyCollection<string> driverIds,
            CancellationToken cancellationToken = default) =>
            inner.GetActiveForDriversAsync(driverIds, cancellationToken);

        public Task<bool> HasActiveAsync(string driverId, CancellationToken cancellationToken = default) =>
            inner.HasActiveAsync(driverId, cancellationToken);

        public Task<bool> TryReleaseAsync(
            string driverId, string rideId, CancellationToken cancellationToken = default,
            DriverRideReservationAuditContext? auditContext = null) =>
            inner.TryReleaseAsync(driverId, rideId, cancellationToken, auditContext);
    }

    private sealed class TurnOvaApiFactory : WebApplicationFactory<Program>
    {
        private readonly bool _legacyFifoRiderRequestsEnabled;
        private readonly double? _nearbyRadiusKm;
        // Test-only fixture; production appsettings intentionally leaves this product policy unset.
        private readonly int? _nearbyLocationMaxAgeSeconds;
        private readonly int? _selectedDriverNegotiationLifetimeMinutes;
        private readonly bool _storageReadinessUnavailable;
        private readonly IRideOfferAdvancer? _rideOfferAdvancer;
        private readonly IReadOnlyList<DispatchZoneConfig>? _dispatchZones;
        private readonly IQueueStore? _queueStore;
        private readonly IDriverRideReservationStore? _reservationStore;

        public TurnOvaApiFactory(bool legacyFifoRiderRequestsEnabled = true, double? nearbyRadiusKm = 10,
            int? selectedDriverNegotiationLifetimeMinutes = 15, bool storageReadinessUnavailable = false,
            int? nearbyLocationMaxAgeSeconds = 120, IRideOfferAdvancer? rideOfferAdvancer = null,
            IReadOnlyList<DispatchZoneConfig>? dispatchZones = null,
            IQueueStore? queueStore = null,
            IDriverRideReservationStore? reservationStore = null)
        {
            _legacyFifoRiderRequestsEnabled = legacyFifoRiderRequestsEnabled;
            _nearbyRadiusKm = nearbyRadiusKm;
            _nearbyLocationMaxAgeSeconds = nearbyLocationMaxAgeSeconds;
            _selectedDriverNegotiationLifetimeMinutes = selectedDriverNegotiationLifetimeMinutes;
            _storageReadinessUnavailable = storageReadinessUnavailable;
            _rideOfferAdvancer = rideOfferAdvancer;
            _dispatchZones = dispatchZones;
            _queueStore = queueStore;
            _reservationStore = reservationStore;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Auth:Jwt:Issuer"] = Issuer,
                    ["Auth:Jwt:Audience"] = Audience,
                    ["Auth:Jwt:SigningKey"] = SigningKey,
                    ["TriSend:BaseUrl"] = "https://localhost:7443",
                    ["DriverOnboarding:AdminApiKey"] = "integration-test-admin-key-32chars",
                    ["Cors:AllowedOrigins:0"] = "https://app.turnova.test",
                    ["Dispatch:NearbyRadiusKm"] = _nearbyRadiusKm?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Dispatch:NearbyLocationMaxAgeSeconds"] = _nearbyLocationMaxAgeSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Dispatch:SelectedDriverNegotiationLifetimeMinutes"] =
                        _selectedDriverNegotiationLifetimeMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Dispatch:LegacyFifoRiderRequestsEnabled"] = _legacyFifoRiderRequestsEnabled ? "true" : "false"
                };

                if (_dispatchZones is not null)
                {
                    for (var index = 0; index < _dispatchZones.Count; index++)
                    {
                        var zone = _dispatchZones[index];
                        settings[$"Dispatch:Zones:{index}:ZoneId"] = zone.ZoneId;
                        settings[$"Dispatch:Zones:{index}:MinLatitude"] =
                            zone.MinLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        settings[$"Dispatch:Zones:{index}:MaxLatitude"] =
                            zone.MaxLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        settings[$"Dispatch:Zones:{index}:MinLongitude"] =
                            zone.MinLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        settings[$"Dispatch:Zones:{index}:MaxLongitude"] =
                            zone.MaxLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                configuration.AddInMemoryCollection(settings);
            });

            var rideOfferAdvancer = _rideOfferAdvancer;
            var queueStore = _queueStore;
            var reservationStore = _reservationStore;
            if (_storageReadinessUnavailable || rideOfferAdvancer is not null ||
                queueStore is not null || reservationStore is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    if (queueStore is not null)
                    {
                        services.RemoveAll<IQueueStore>();
                        services.AddSingleton<IQueueStore>(queueStore);
                    }

                    if (reservationStore is not null)
                    {
                        services.RemoveAll<IDriverRideReservationStore>();
                        services.AddSingleton<IDriverRideReservationStore>(reservationStore);
                    }

                    if (_storageReadinessUnavailable)
                    {
                        services.RemoveAll<StorageReadinessProbe>();
                        services.AddSingleton(new StorageReadinessProbe(
                            _ => Task.FromException(new HttpRequestException("test-only-storage-failure")),
                            TimeSpan.FromSeconds(3)));
                    }

                    if (rideOfferAdvancer is not null)
                    {
                        services.RemoveAll<IRideOfferAdvancer>();
                        services.AddSingleton<IRideOfferAdvancer>(rideOfferAdvancer);
                    }
                });
            }
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


    private static string ExtractPayslipPdfText(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfDocument.Open(stream);
        return string.Join("\\n", document.GetPages().Select(page => page.Text));
    }

}
