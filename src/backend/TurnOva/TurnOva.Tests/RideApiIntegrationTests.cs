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
using Microsoft.IdentityModel.Tokens;

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
                    ["TriSend:BaseUrl"] = "https://localhost:7443"
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
