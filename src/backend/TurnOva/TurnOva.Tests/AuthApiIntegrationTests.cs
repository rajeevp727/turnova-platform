using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TurnOva.API;

namespace TurnOva.Tests;

public sealed class AuthApiIntegrationTests
{
    private const string Issuer = "turnova-auth-tests";
    private const string Audience = "turnova-api";
    private const string SigningKey = "turnova-auth-integration-test-signing-key-32-bytes";

    [Theory]
    [InlineData("rider")]
    [InlineData("driver")]
    public async Task DemoAuthenticationEndpointsAreNotAvailable(string role)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/v1/auth/demo/{role}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("rider", "Rider")]
    [InlineData("driver", "Driver")]
    public async Task RegistrationRequiresConfiguredCosmosStorage(string routeRole, string role)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync($"/api/v1/auth/{routeRole}/register",
            new { email = "new.user@example.com", password = "SecurePass123", name = "Test User" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("rider")]
    [InlineData("driver")]
    public async Task LoginRequiresConfiguredCosmosStorage(string role)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync($"/api/v1/auth/{role}/login",
            new { email = "user@example.com", password = "SecurePass123" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsWeakPasswordBeforeStorageAccess()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/register",
            new { email = "new.user@example.com", password = "weak", name = "Test User" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnsupportedRoleIsNotAccepted()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/admin/login",
            new { email = "admin@example.com", password = "SecurePass123" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RefreshRejectsMissingToken()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MeRejectsAnonymousRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MeReturnsIdentityFromValidatedTurnOvaJwt()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken());

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(body?.User);
        Assert.Equal("rider-123", body.User.Id);
        Assert.Equal("rider@example.com", body.User.Email);
        Assert.Equal("Rider", body.User.Role);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointsRemainAvailable(string path)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void StartupRejectsWeakJwtSigningKey()
    {
        using var factory = new AuthApiFactory(new Dictionary<string, string?> { ["Auth:Jwt:SigningKey"] = "too-short" });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("Auth:Jwt", exception.ToString(), StringComparison.Ordinal);
    }

    private static string CreateToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "rider-123"),
            new Claim(JwtRegisteredClaimNames.Email, "rider@example.com"),
            new Claim("name", "Test Rider"),
            new Claim("role", "Rider")
        };
        var token = new JwtSecurityToken(Issuer, Audience, claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record MeResponse(MeUser User);
    private sealed record MeUser(string Id, string Email, string? Name, string Role);

    private sealed class AuthApiFactory(IReadOnlyDictionary<string, string?>? overrides = null)
        : WebApplicationFactory<Program>
    {
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
                    ["DriverOnboarding:AdminApiKey"] = "integration-test-admin-key-32chars"
                };
                if (overrides is not null)
                    foreach (var setting in overrides)
                        settings[setting.Key] = setting.Value;
                configuration.AddInMemoryCollection(settings);
            });
        }
    }
}
