using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TurnOva.API;

namespace TurnOva.Tests;

public sealed class AuthApiIntegrationTests
{
    private const string Issuer = "turnova-auth-tests";
    private const string Audience = "turnova-api";
    private const string SigningKey = "turnova-auth-integration-test-signing-key-32-bytes";

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

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class AuthApiFactory(RecordingHandler handler) : WebApplicationFactory<Program>
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
                    ["TriSend:BaseUrl"] = "https://auth.test.invalid",
                    ["DriverOnboarding:AdminApiKey"] = "integration-test-admin-key-32chars"
                }));
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
