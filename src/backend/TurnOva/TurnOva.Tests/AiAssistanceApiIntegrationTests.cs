using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TurnOva.API;

namespace TurnOva.Tests;

public sealed class AiAssistanceApiIntegrationTests
{
    [Fact]
    public async Task TripIntentRequiresRiderAndReturnsMissingPassengerCountForManualCompletion()
    {
        using var factory = new AiAssistanceApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-rider");
        using var driver = factory.CreateClient("Driver", "ai-driver");
        using var anonymous = factory.CreateClient();

        using var response = await rider.PostAsJsonAsync(
            "/api/v1/assistance/trip-intent",
            new { text = "Cab from Nizampet to HITEC City" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("passengerCount").ValueKind);
        Assert.Contains(
            "passengerCount",
            root.GetProperty("missingFields").EnumerateArray().Select(value => value.GetString()));
        Assert.True(root.GetProperty("needsClarification").GetBoolean());
        Assert.True(root.GetProperty("requiresUserConfirmation").GetBoolean());
        Assert.False(root.TryGetProperty("rideId", out _));

        using var driverResponse = await driver.PostAsJsonAsync(
            "/api/v1/assistance/trip-intent",
            new { text = "Cab from Nizampet to HITEC City for 2 passengers" });
        Assert.Equal(HttpStatusCode.Forbidden, driverResponse.StatusCode);

        using var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/v1/assistance/trip-intent",
            new { text = "Cab from Nizampet to HITEC City for 2 passengers" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
    }

    [Fact]
    public async Task ApprovedHelpEndpointAllowsRiderAndDriverButRejectsAnonymousCaller()
    {
        using var factory = new AiAssistanceApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-help-rider");
        using var driver = factory.CreateClient("Driver", "ai-help-driver");
        using var anonymous = factory.CreateClient();

        using var riderResponse = await rider.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?" });
        using var driverResponse = await driver.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does Online availability mean?" });
        using var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?" });

        Assert.Equal(HttpStatusCode.OK, riderResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, driverResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
    }

    [Fact]
    public async Task AssistantRateLimitIsPartitionedByAuthenticatedSubject()
    {
        using var factory = new AiAssistanceApiFactory();
        using var firstCaller = factory.CreateClient("Rider", "ai-rate-limit-first");
        using var secondCaller = factory.CreateClient("Rider", "ai-rate-limit-second");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var allowed = await firstCaller.PostAsJsonAsync(
                "/api/v1/assistance/help",
                new { question = "What does queue status mean?" });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await firstCaller.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        using var otherCaller = await secondCaller.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?" });
        Assert.Equal(HttpStatusCode.OK, otherCaller.StatusCode);
    }

    private sealed class AiAssistanceApiFactory : WebApplicationFactory<Program>
    {
        private const string Issuer = "turnova-ai-assistance-tests";
        private const string Audience = "turnova-api";
        private const string SigningKey = "turnova-ai-assistance-test-signing-key-32-bytes";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Jwt:Issuer"] = Issuer,
                    ["Auth:Jwt:Audience"] = Audience,
                    ["Auth:Jwt:SigningKey"] = SigningKey,
                    ["TriSend:BaseUrl"] = "https://auth.test.invalid",
                    ["DriverOnboarding:AdminApiKey"] = "integration-test-admin-key-32chars",
                    ["Cors:AllowedOrigins:0"] = "https://app.turnova.test",
                    ["Dispatch:NearbyRadiusKm"] = "10",
                    ["Dispatch:NearbyLocationMaxAgeSeconds"] = "120",
                    ["Dispatch:SelectedDriverNegotiationLifetimeMinutes"] = "15",
                    ["Dispatch:LegacyFifoRiderRequestsEnabled"] = "true",
                    ["AiAssistant:Enabled"] = "false"
                });
            });
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
            var claims = new[]
            {
                new Claim("sub", subject),
                new Claim("role", role),
                new Claim("name", subject)
            };
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
