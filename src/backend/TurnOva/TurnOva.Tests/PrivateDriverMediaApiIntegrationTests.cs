using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed partial class RideApiIntegrationTests
{
    [Fact]
    public async Task PrivateDriverMediaUploadIssuesOpaqueOwnerBoundReferenceAndPrivateRead()
    {
        using var factory = new TurnOvaApiFactory();
        using var owner = factory.CreateClient("Driver", "private-media-owner");
        using var other = factory.CreateClient("Driver", "private-media-other");
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\nprivate-test-document");

        using var upload = await owner.PostAsJsonAsync("/api/v1/driver/onboarding/media", new
        {
            documentType = "DrivingLicence",
            contentType = "application/pdf",
            contentBase64 = Convert.ToBase64String(pdf)
        });

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var body = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var reference = body.RootElement.GetProperty("storageReference").GetString();
        Assert.NotNull(reference);
        var mediaReference = reference!;
        Assert.True(mediaReference.StartsWith("drvmedia_", StringComparison.Ordinal));
        Assert.False(mediaReference.Contains("private-media-owner", StringComparison.Ordinal));
        Assert.Equal("Clean", body.RootElement.GetProperty("scanStatus").GetString());

        using var ownerRead = await owner.GetAsync($"/api/v1/driver/onboarding/media/{Uri.EscapeDataString(mediaReference)}");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        Assert.True(ownerRead.Headers.CacheControl?.Private);
        Assert.True(ownerRead.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", ownerRead.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(pdf, await ownerRead.Content.ReadAsByteArrayAsync());

        using var statusRead = await owner.GetAsync($"/api/v1/driver/onboarding/media/{Uri.EscapeDataString(mediaReference)}/status");
        Assert.Equal(HttpStatusCode.OK, statusRead.StatusCode);
        using var statusJson = JsonDocument.Parse(await statusRead.Content.ReadAsStringAsync());
        Assert.Equal("Clean", statusJson.RootElement.GetProperty("scanStatus").GetString());

        using var otherRead = await other.GetAsync($"/api/v1/driver/onboarding/media/{Uri.EscapeDataString(mediaReference)}");
        Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
        using var otherStatus = await other.GetAsync($"/api/v1/driver/onboarding/media/{Uri.EscapeDataString(mediaReference)}/status");
        Assert.Equal(HttpStatusCode.NotFound, otherStatus.StatusCode);
    }

    [Fact]
    public async Task OnboardingPersistsPrivateSelfieReferenceAndRejectsAnotherDriversDocumentReference()
    {
        using var factory = new TurnOvaApiFactory();
        using var owner = factory.CreateClient("Driver", "private-media-onboarding-owner");
        using var other = factory.CreateClient("Driver", "private-media-onboarding-other");
        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles.TryRemove("private-media-onboarding-owner", out _);
        profiles.TryRemove("private-media-onboarding-other", out _);

        static async Task<string> UploadAsync(HttpClient client, string documentType, string contentType, byte[] bytes)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/driver/onboarding/media", new
            {
                documentType,
                contentType,
                contentBase64 = Convert.ToBase64String(bytes)
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("storageReference").GetString()!;
        }

        var selfieBytes = Convert.FromBase64String("/9j/2Q==");
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\nprivate-test-document");
        var ownerSelfie = await UploadAsync(owner, "ProfilePhoto", "image/jpeg", selfieBytes);
        var ownerLicence = await UploadAsync(owner, "DrivingLicence", "application/pdf", pdf);
        var ownerRegistration = await UploadAsync(owner, "VehicleRegistration", "application/pdf", pdf);

        using var onboarding = await owner.PostAsJsonAsync("/api/v1/driver/onboarding", new
        {
            zoneId = "hyderabad-central",
            fullName = "Private Media Driver",
            phoneNumber = "+919876543210",
            vehicleMake = "Maruti",
            vehicleModel = "Dzire",
            vehicleRegistration = "TS09AB1234",
            serviceClass = "cab",
            passengerCapacity = 3,
            documents = new[]
            {
                new { documentType = "DrivingLicence", storageReference = ownerLicence },
                new { documentType = "VehicleRegistration", storageReference = ownerRegistration }
            },
            profilePhotoStorageReference = ownerSelfie
        });

        Assert.Equal(HttpStatusCode.Accepted, onboarding.StatusCode);
        var saved = profiles["private-media-onboarding-owner"];
        Assert.Null(saved.ProfilePhotoBase64);
        Assert.Equal(ownerSelfie, saved.ProfilePhotoStorageReference);
        Assert.All(saved.Documents, document =>
            Assert.True(document.StorageReference.StartsWith("drvmedia_", StringComparison.Ordinal)));

        var ownProfile = await owner.GetFromJsonAsync<DriverOnboardingProfile>("/api/v1/driver/onboarding/me");
        Assert.NotNull(ownProfile);
        Assert.Equal(ownerSelfie, ownProfile.ProfilePhotoStorageReference);
        Assert.Equal(Convert.ToBase64String(selfieBytes), ownProfile.ProfilePhotoBase64);

        var otherSelfie = await UploadAsync(other, "ProfilePhoto", "image/jpeg", selfieBytes);
        var otherRegistration = await UploadAsync(other, "VehicleRegistration", "application/pdf", pdf);
        using var foreignReference = await other.PostAsJsonAsync("/api/v1/driver/onboarding", new
        {
            zoneId = "hyderabad-central",
            fullName = "Other Driver",
            phoneNumber = "+919876543211",
            vehicleMake = "Maruti",
            vehicleModel = "Dzire",
            vehicleRegistration = "TS09AB5678",
            serviceClass = "cab",
            passengerCapacity = 3,
            documents = new[]
            {
                new { documentType = "DrivingLicence", storageReference = ownerLicence },
                new { documentType = "VehicleRegistration", storageReference = otherRegistration }
            },
            profilePhotoStorageReference = otherSelfie
        });

        Assert.Equal(HttpStatusCode.BadRequest, foreignReference.StatusCode);
        Assert.False(profiles.ContainsKey("private-media-onboarding-other"));
    }


    [Fact]
    public async Task PrivateDriverMediaUploadRejectsMismatchedContentSignature()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "private-media-invalid-signature");

        using var response = await driver.PostAsJsonAsync("/api/v1/driver/onboarding/media", new
        {
            documentType = "DrivingLicence",
            contentType = "application/pdf",
            contentBase64 = Convert.ToBase64String(Encoding.ASCII.GetBytes("not-a-pdf"))
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnionManagerCanReadOnlyCurrentAssignedDriversScanClearedDocuments()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "private-media-union-driver");
        const string unionId = "private-media-union";
        SeedBookableDriver(factory, "private-media-union-driver", unionId);

        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\\nunion-review-document");
        using var licenceUpload = await driver.PostAsJsonAsync("/api/v1/driver/onboarding/media", new
        {
            documentType = "DrivingLicence",
            contentType = "application/pdf",
            contentBase64 = Convert.ToBase64String(pdf)
        });
        Assert.Equal(HttpStatusCode.Created, licenceUpload.StatusCode);
        using var licenceJson = JsonDocument.Parse(await licenceUpload.Content.ReadAsStringAsync());
        var licenceReference = licenceJson.RootElement.GetProperty("storageReference").GetString()!;

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles["private-media-union-driver"] = profiles["private-media-union-driver"] with
        {
            Documents =
            [
                new DriverDocumentReference("DrivingLicence", licenceReference, "Approved"),
                new DriverDocumentReference("VehicleRegistration", "test/vehicle-registration", "Approved")
            ]
        };

        using var manager = factory.CreateUnionManagerClient(unionId, "private-media-union-manager");
        using var otherManager = factory.CreateUnionManagerClient("another-private-media-union", "private-media-other-manager");
        using var ownerRead = await manager.GetAsync(
            "/api/v1/union/drivers/private-media-union-driver/documents/DrivingLicence");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        Assert.True(ownerRead.Headers.CacheControl?.Private);
        Assert.True(ownerRead.Headers.CacheControl?.NoStore);
        Assert.Equal(pdf, await ownerRead.Content.ReadAsByteArrayAsync());

        using var otherRead = await otherManager.GetAsync(
            "/api/v1/union/drivers/private-media-union-driver/documents/DrivingLicence");
        Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
    }

    [Fact]
    public async Task UnionManagerCannotRetrieveDocumentsForHistoricalNonCabDriver()
    {
        using var factory = new TurnOvaApiFactory();
        using var driver = factory.CreateClient("Driver", "private-media-noncab-driver");
        const string unionId = "private-media-noncab-union";
        SeedBookableDriver(factory, "private-media-noncab-driver", unionId);

        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\\nlegacy-noncab-document");
        using var upload = await driver.PostAsJsonAsync("/api/v1/driver/onboarding/media", new
        {
            documentType = "DrivingLicence",
            contentType = "application/pdf",
            contentBase64 = Convert.ToBase64String(pdf)
        });
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var json = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var reference = json.RootElement.GetProperty("storageReference").GetString()!;

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        profiles["private-media-noncab-driver"] = profiles["private-media-noncab-driver"] with
        {
            ServiceClass = "auto",
            Documents = [new DriverDocumentReference("DrivingLicence", reference, "Approved")]
        };

        using var manager = factory.CreateUnionManagerClient(unionId, "private-media-noncab-manager");
        using var response = await manager.GetAsync(
            "/api/v1/union/drivers/private-media-noncab-driver/documents/DrivingLicence");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Rider")]
    [InlineData("Driver")]
    [InlineData("Ops")]
    public async Task NonUnionManagerRoleCannotAccessUnionOversightEvenWithMatchingUnionClaim(string role)
    {
        const string driverId = "union-oversight-role-boundary-driver";
        const string unionId = "union-oversight-role-boundary-union";

        using var factory = new TurnOvaApiFactory();
        using var nonManager = factory.CreateClient(role, driverId, unionId);
        SeedBookableDriver(factory, driverId, unionId);

        var profiles = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverOnboardingProfile>>();
        var assignments = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAssignment>>();
        var auditEvents = factory.Services.GetRequiredService<ConcurrentDictionary<string, DriverUnionAuditEvent>>();
        var initialProfile = profiles[driverId];
        var initialAssignment = assignments[driverId];
        var initialAuditCount = auditEvents.Count;

        using var members = await nonManager.GetAsync("/api/v1/union/drivers");
        using var reconciliation = await nonManager.GetAsync("/api/v1/union/reconciliation");
        using var document = await nonManager.GetAsync(
            $"/api/v1/union/drivers/{driverId}/documents/DrivingLicence");
        using var selfieReviewRequest = new HttpRequestMessage(
            HttpMethod.Put, $"/api/v1/union/drivers/{driverId}/selfie-review")
        {
            Content = JsonContent.Create(new { status = "Approved", reason = (string?)null })
        };
        using var selfieReview = await nonManager.SendAsync(selfieReviewRequest);

        foreach (var response in new[] { members, reconciliation, document, selfieReview })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains(response.Headers.Pragma, value =>
                value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(initialProfile, profiles[driverId]);
        Assert.Equal(initialAssignment, assignments[driverId]);
        Assert.Equal(initialAuditCount, auditEvents.Count);
    }

}
