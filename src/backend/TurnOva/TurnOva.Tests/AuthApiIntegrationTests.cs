using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TurnOva.API;

namespace TurnOva.Tests;

public sealed class AuthApiIntegrationTests
{
    private const string Issuer = "turnova-auth-tests";
    private const string Audience = "turnova-api";
    private const string SigningKey = "turnova-auth-integration-test-signing-key-32-bytes";

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthProbesAreAvailableWithoutAuthentication(string path)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Health probes must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DriverRegistrationForwardsNormalizedIdentityAndServerOwnedRole()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.Created,
            """{"accessToken":"access","refreshToken":"refresh","user":{"role":"Driver"}}""")));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/driver/register",
            new { email = "  DRIVER@example.com  ", password = "StrongPassword!123", name = "  Test Driver  ", role = "Admin" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/auth/register", request.Path);
        Assert.Equal("POST", request.Method);
        Assert.Equal("driver@example.com", request.Body.GetProperty("email").GetString());
        Assert.Equal("Test Driver", request.Body.GetProperty("name").GetString());
        Assert.Equal("turnova", request.Body.GetProperty("appId").GetString());
        Assert.Equal("Driver", request.Body.GetProperty("role").GetString());
        Assert.NotEqual("Admin", request.Body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task RegistrationWithMissingCredentialsDoesNotCallTriSend()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("TriSend must not be called."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/register",
            new { email = " ", password = "secret", name = "Rider" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnsupportedRoleDoesNotCallTriSend()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("TriSend must not be called."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/admin/register",
            new { email = "admin@example.com", password = "secret", name = "Admin" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LoginRejectsWrongAppRoleAndRevokesReturnedSession()
    {
        using var handler = new RecordingHandler((request, _) =>
        {
            if (request.Path == "/auth/login")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    """{"accessToken":"wrong-role-access-token","refreshToken":"refresh","user":{"role":"Rider"}}"""));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"success":true}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/driver/login",
            new { email = "rider@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(new[] { "/auth/login", "/auth/logout" }, handler.Requests.Select(x => x.Path));
        Assert.Equal("Bearer", handler.Requests[1].AuthorizationScheme);
        Assert.Equal("wrong-role-access-token", handler.Requests[1].AuthorizationParameter);
    }

    [Fact]
    public async Task RefreshForwardsRefreshTokenWithoutRewritingIt()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            """{"accessToken":"new-access","refreshToken":"new-refresh"}""")));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = "opaque-refresh-token" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/auth/refresh", request.Path);
        Assert.Equal("opaque-refresh-token", request.Body.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task TriSendConnectionFailureReturnsServiceUnavailableWithoutLeakingDetails()
    {
        using var handler = new RecordingHandler((_, _) => throw new HttpRequestException("private upstream detail"));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("temporarily unavailable", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private upstream detail", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DriverEndpointRejectsMissingBearerToken()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Authorization must reject before calling TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/driver/availability");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("Bearer malformed")]
    public async Task DriverEndpointRejectsMalformedBearerToken(string token)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Authorization must reject before calling TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token.StartsWith("Bearer ", StringComparison.Ordinal) ? token : $"Bearer {token}");

        using var response = await client.GetAsync("/api/v1/driver/availability");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DriverEndpointRejectsTokenWithInvalidSignature()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Authorization must reject before calling TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(signingKey: "different-signing-key-for-tests-32-bytes"));

        using var response = await client.GetAsync("/api/v1/driver/availability");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("wrong-issuer", "turnova-api")]
    [InlineData("turnova-auth-tests", "wrong-audience")]
    public async Task DriverEndpointRejectsTokenWithWrongIssuerOrAudience(string issuer, string audience)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Authorization must reject before calling TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(issuer: issuer, audience: audience));

        using var response = await client.GetAsync("/api/v1/driver/availability");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DriverEndpointRejectsExpiredToken()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Authorization must reject before calling TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(expires: DateTime.UtcNow.AddMinutes(-5)));

        using var response = await client.GetAsync("/api/v1/driver/availability");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RiderTokenCannotAccessDriverEndpoint()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Authorization must reject before calling TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(role: "Rider"));

        using var response = await client.GetAsync("/api/v1/driver/availability");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DriverTokenCannotAccessRiderEndpoint()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Authorization must reject before calling TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(role: "Driver"));

        using var response = await client.GetAsync("/api/v1/rides/RIDE-NOT-OWNED");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ValidDriverTokenCanAccessDriverEndpoint()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Driver availability must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(role: "Driver"));

        using var response = await client.GetAsync("/api/v1/driver/availability");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Auth:Jwt:Issuer", " ")]
    [InlineData("Auth:Jwt:Audience", " ")]
    [InlineData("Auth:Jwt:SigningKey", " ")]
    public void StartupRejectsMissingJwtConfiguration(string setting, string value)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Startup validation must run before TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { [setting] = value });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("Auth:Jwt", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void StartupRejectsWeakJwtSigningKey()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Startup validation must run before TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["Auth:Jwt:SigningKey"] = "too-short" });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("Auth:Jwt", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://auth.test.invalid")]
    [InlineData("not-a-valid-url")]
    [InlineData(" ")]
    public void StartupRejectsNonHttpsOrInvalidTriSendBaseUrl(string baseUrl)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Startup validation must run before TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["TriSend:BaseUrl"] = baseUrl });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("TriSend:BaseUrl", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    private static string CreateToken(
        string role = "Driver",
        string issuer = Issuer,
        string audience = Audience,
        string signingKey = SigningKey,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "driver-1"),
            new Claim("role", role)
        };
        var token = new JwtSecurityToken(
            issuer, audience, claims,
            notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires ?? DateTime.UtcNow.AddMinutes(5),
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class AuthApiFactory(
        RecordingHandler handler,
        IReadOnlyDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
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
                    ["TriSend:BaseUrl"] = "https://auth.test.invalid",
                    ["DriverOnboarding:AdminApiKey"] = "integration-test-admin-key-32chars"
                };
                if (overrides is not null)
                    foreach (var setting in overrides)
                        settings[setting.Key] = setting.Value;
                configuration.AddInMemoryCollection(settings);
            });
            builder.ConfigureTestServices(services =>
                services.AddHttpClient("TriSend")
                    .ConfigurePrimaryHttpMessageHandler(() => handler));
        }
    }

    private sealed record RecordedRequest(
        string Method, string Path, JsonElement Body,
        string? AuthorizationScheme, string? AuthorizationParameter);

    private sealed class RecordingHandler(
        Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? JsonDocument.Parse("{}").RootElement.Clone()
                : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            var recorded = new RecordedRequest(
                request.Method.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                body,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter);
            Requests.Add(recorded);
            return await responder(recorded, cancellationToken);
        }
    }
}
