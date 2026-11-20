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

[CollectionDefinition(nameof(AuthApiIntegrationCollection), DisableParallelization = true)]
public sealed class AuthApiIntegrationCollection { }

[Collection(nameof(AuthApiIntegrationCollection))]
public sealed class AuthApiIntegrationTests
{
    private const string Issuer = "turnova-auth-tests";
    private const string Audience = "turnova-api";
    private const string SigningKey = "turnova-auth-integration-test-signing-key-32-bytes";

    [Fact]
    public async Task DemoSessionIsDisabledUnlessExplicitlyEnabled()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo auth must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/auth/demo/rider", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoSessionIsDisabledForUnknownEnvironmentEvenWhenExplicitlyEnabled()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo auth must not call TriSend."));
        using var factory = new AuthApiFactory(
            handler,
            new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" },
            environmentName: "Preview");
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/auth/demo/rider", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("rider", "Rider", "demo-rider-001")]
    [InlineData("driver", "Driver", "demo-driver-001")]
    public async Task DemoSessionIssuesShortLivedRoleScopedToken(string role, string expectedRole, string expectedSubject)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo auth must not call TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/v1/auth/demo/{role}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("isDemo").GetBoolean());
        Assert.Equal(1800, body.RootElement.GetProperty("expiresIn").GetInt32());
        Assert.Equal(expectedRole, body.RootElement.GetProperty("user").GetProperty("role").GetString());
        var token = new JwtSecurityTokenHandler().ReadJwtToken(body.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal(expectedSubject, token.Subject);
        Assert.Equal(expectedRole, token.Claims.Single(claim => claim.Type == "role").Value);
        Assert.Equal("true", token.Claims.Single(claim => claim.Type == "demo").Value);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoSessionRejectsUnsupportedRole()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo auth must not call TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/auth/demo/admin", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoTokenCannotAccessNormalAuthenticatedEndpoints()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo token must not reach TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();
        using var issued = await client.PostAsync("/api/v1/auth/demo/rider", null);
        using var issuedBody = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var accessToken = issuedBody.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoTokenCannotAccessAnUnmarkedEndpointUnderTheDemoPrefix()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo token must not reach TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();
        using var issued = await client.PostAsync("/api/v1/auth/demo/rider", null);
        using var issuedBody = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var accessToken = issuedBody.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.GetAsync("/api/v1/demo/__test/unmarked");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoTokenCanAccessOnlyAnExplicitlyMarkedDemoEndpoint()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo token must not reach TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();
        using var issued = await client.PostAsync("/api/v1/auth/demo/rider", null);
        using var issuedBody = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var accessToken = issuedBody.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.GetAsync("/api/v1/demo/__test/marked");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoTokenCanAccessAnExplicitlyMarkedHeadDemoEndpoint()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo token must not reach TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();
        using var issued = await client.PostAsync("/api/v1/auth/demo/rider", null);
        using var issuedBody = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var accessToken = issuedBody.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var request = new HttpRequestMessage(HttpMethod.Head, "/api/v1/demo/__test/marked-head");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoTokenCannotAccessAnExplicitlyMarkedEndpointOutsideDemoPrefix()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo token must not reach TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();
        using var issued = await client.PostAsync("/api/v1/auth/demo/rider", null);
        using var issuedBody = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var accessToken = issuedBody.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.GetAsync("/api/v1/demo-outside/__test/marked");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoTokenCannotAccessMarkedEndpointWhenDemoAuthIsDisabled()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo token must not reach a demo endpoint while disabled."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken(role: "Rider", demo: true));

        using var response = await client.GetAsync("/api/v1/demo/__test/marked");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DisablingDemoAuthAfterIssuanceRejectsStillValidDemoToken()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo auth must not call TriSend."));
        using var factory = new AuthApiFactory(
            handler,
            new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();

        using var issued = await client.PostAsync("/api/v1/auth/demo/rider", null);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var issuedBody = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var accessToken = issuedBody.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var allowed = await client.GetAsync("/api/v1/demo/__test/marked");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        factory.Services.GetRequiredService<IConfiguration>()["DemoAuth:Enabled"] = "false";
        using var denied = await client.GetAsync("/api/v1/demo/__test/marked");

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoSessionIssuanceIsRateLimitedPerRemoteAddress()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo auth must not call TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var index = 0; index < DemoAuthPolicy.RequestsPerMinute + 1; index++)
        {
            using var response = await client.PostAsync("/api/v1/auth/demo/rider", null);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(DemoAuthPolicy.RequestsPerMinute,
            statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.TooManyRequests));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoSessionRateLimitDoesNotTrustClientForwardedForHeaders()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo auth must not call TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var index = 0; index < DemoAuthPolicy.RequestsPerMinute + 1; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/demo/rider");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"198.51.100.{index + 1}");
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(DemoAuthPolicy.RequestsPerMinute,
            statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.TooManyRequests));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DemoTokenCannotAccessAnExplicitlyMarkedMutationEndpoint()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo token must not reach TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();
        using var issued = await client.PostAsync("/api/v1/auth/demo/rider", null);
        using var issuedBody = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var accessToken = issuedBody.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.PostAsync("/api/v1/demo/__test/marked-write", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

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
    public async Task RiderRegistrationForwardsNormalizedIdentityAndServerOwnedRole()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.Created,
            """{"accessToken":"access","refreshToken":"refresh","user":{"role":"Rider"}}""")));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/register",
            new { email = "  RIDER@example.com  ", password = "StrongPassword!123", name = "  Test Rider  ", role = "Admin" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/auth/register", request.Path);
        Assert.Equal("rider@example.com", request.Body.GetProperty("email").GetString());
        Assert.Equal("Test Rider", request.Body.GetProperty("name").GetString());
        Assert.Equal("turnova", request.Body.GetProperty("appId").GetString());
        Assert.Equal("Rider", request.Body.GetProperty("role").GetString());
        Assert.NotEqual("Admin", request.Body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task DuplicateRegistrationPreservesTriSendConflictWithoutReturningTokens()
    {
        var callCount = 0;
        using var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/auth/register", request.Path);
            Assert.Equal("rider@example.com", request.Body.GetProperty("email").GetString());
            Assert.Equal("Rider", request.Body.GetProperty("role").GetString());
            Assert.Equal("turnova", request.Body.GetProperty("appId").GetString());

            if (Interlocked.Increment(ref callCount) == 1)
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.Created,
                    """{"accessToken":"first-access","refreshToken":"first-refresh","user":{"role":"Rider"}}"""));
            }

            return Task.FromResult(JsonResponse(HttpStatusCode.Conflict,
                """{"message":"Email already registered."}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        var payload = new { email = "Rider@example.com", password = "StrongPassword!123", name = "Existing Rider" };

        using var first = await client.PostAsJsonAsync("/api/v1/auth/rider/register", payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsJsonAsync("/api/v1/auth/rider/register", payload);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("Email already registered.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task MeEndpointRejectsAnonymousRequestBeforeCallingTriSend()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("Anonymous profile reads must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MeEndpointReturnsUpstreamProfileAndForwardsBearerToken()
    {
        var accessToken = CreateToken("Rider");
        using var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/auth/me", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                """{"id":"rider-1","email":"rider@example.com","name":"Rider","role":"Rider"}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profileBody = await response.Content.ReadAsStringAsync();
        using var profile = JsonDocument.Parse(profileBody);
        Assert.Equal("rider-1", profile.RootElement.GetProperty("id").GetString());
        Assert.Equal("Rider", profile.RootElement.GetProperty("role").GetString());
        Assert.DoesNotContain("accessToken", profileBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", profileBody, StringComparison.OrdinalIgnoreCase);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(accessToken, request.AuthorizationParameter);
    }



    [Fact]
    public async Task OpenApiDocumentDescribesCoreRoutesAndAccurateSecuritySchemes()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("OpenAPI generation must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("TurnOva API", root.GetProperty("info").GetProperty("title").GetString());
        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/auth/me", out var mePath));
        Assert.True(paths.TryGetProperty("/api/v1/auth/{role}/login", out var loginPath));
        Assert.True(paths.TryGetProperty("/api/v1/auth/{role}/register", out var registerPath));
        Assert.True(paths.TryGetProperty("/api/v1/auth/refresh", out var refreshPath));
        Assert.True(paths.TryGetProperty("/api/v1/auth/demo/{role}", out var demoAuthPath));
        Assert.True(paths.TryGetProperty("/api/v1/rider/discovery", out _));
        Assert.True(paths.TryGetProperty("/api/v1/rider/bookings", out var riderBookingPath));
        Assert.True(paths.TryGetProperty("/api/v1/driver/rides/{rideId}/payment-report", out var paymentReportPath));
        Assert.True(paths.TryGetProperty("/api/v1/union/reconciliation", out var unionReconciliationPath));
        Assert.True(paths.TryGetProperty("/api/v1/ops/drivers/{driverId}", out var opsPath));
        Assert.True(paths.TryGetProperty("/api/v1/ops/unions", out var opsUnionsPath));
        Assert.False(paths.TryGetProperty("/__test/throw", out _),
            "Testing-only exception routes must not appear in the published API contract.");
        Assert.False(paths.TryGetProperty("/api/v1/demo/__test/unmarked", out _));
        Assert.False(paths.TryGetProperty("/api/v1/demo/__test/marked", out _));
        Assert.False(paths.TryGetProperty("/api/v1/demo/__test/marked-write", out _));

        var schemes = root.GetProperty("components").GetProperty("securitySchemes");
        Assert.Equal("http", schemes.GetProperty("Bearer").GetProperty("type").GetString());
        Assert.Equal("bearer", schemes.GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.Equal("apiKey", schemes.GetProperty("OpsAdminKey").GetProperty("type").GetString());
        Assert.Equal("X-Driver-Admin-Key", schemes.GetProperty("OpsAdminKey").GetProperty("name").GetString());

        var meSecurity = mePath.GetProperty("get").GetProperty("security");
        Assert.Contains(meSecurity.EnumerateArray(), requirement => requirement.TryGetProperty("Bearer", out _));
        Assert.False(loginPath.GetProperty("post").TryGetProperty("security", out _));
        Assert.False(registerPath.GetProperty("post").TryGetProperty("security", out _));
        Assert.False(refreshPath.GetProperty("post").TryGetProperty("security", out _));
        var demoAuthOperation = demoAuthPath.GetProperty("post");
        Assert.False(demoAuthOperation.TryGetProperty("security", out _));
        var demoAuthResponses = demoAuthOperation.GetProperty("responses");
        Assert.True(demoAuthResponses.TryGetProperty("200", out var demoAuthSuccess));
        var successSchema = demoAuthSuccess.GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        Assert.True(successSchema.TryGetProperty("$ref", out var successSchemaRef));
        var schemaName = successSchemaRef.GetString()!.Split('/').Last();
        var demoSessionSchema = root.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        var responseProperties = demoSessionSchema.GetProperty("properties");
        foreach (var property in new[] { "accessToken", "refreshToken", "expiresIn", "isDemo", "user" })
            Assert.True(responseProperties.TryGetProperty(property, out _), $"Demo session response is missing {property}.");
        Assert.True(demoAuthResponses.TryGetProperty("404", out _));
        Assert.True(demoAuthResponses.TryGetProperty("429", out _));
        Assert.True(demoAuthResponses.TryGetProperty("503", out _));

        foreach (var security in new[]
        {
            riderBookingPath.GetProperty("post").GetProperty("security"),
            paymentReportPath.GetProperty("post").GetProperty("security"),
            unionReconciliationPath.GetProperty("get").GetProperty("security")
        })
            Assert.Contains(security.EnumerateArray(), requirement => requirement.TryGetProperty("Bearer", out _));

        var opsSecurity = opsPath.GetProperty("get").GetProperty("security");
        Assert.Contains(opsSecurity.EnumerateArray(), requirement => requirement.TryGetProperty("OpsAdminKey", out _));
        Assert.DoesNotContain(opsSecurity.EnumerateArray(), requirement => requirement.TryGetProperty("Bearer", out _));
        var opsUnionSecurity = opsUnionsPath.GetProperty("post").GetProperty("security");
        Assert.Contains(opsUnionSecurity.EnumerateArray(), requirement => requirement.TryGetProperty("OpsAdminKey", out _));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OpenApiDocumentIsNotMappedInStaging()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("Staging OpenAPI must not call TriSend."));
        using var factory = new AuthApiFactory(handler, environmentName: "Staging");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("/api/v1/auth/rider/login", "RIDER@example.com", "rider@example.com", "Rider", "rider-access-token", "rider-refresh-token")]
    [InlineData("/api/v1/auth/driver/login", "DRIVER@example.com", "driver@example.com", "Driver", "driver-access-token", "driver-refresh-token")]
    public async Task SuccessfulRiderAndDriverLoginForwardsNormalizedIdentityAndReturnsMatchingSession(
        string route,
        string submittedEmail,
        string normalizedEmail,
        string expectedRole,
        string accessToken,
        string refreshToken)
    {
        using var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/auth/login", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                accessToken,
                refreshToken,
                user = new
                {
                    id = $"{expectedRole.ToLowerInvariant()}-1",
                    email = normalizedEmail,
                    name = $"Test {expectedRole}",
                    role = expectedRole
                }
            })));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(route, new
        {
            email = $"  {submittedEmail}  ",
            password = "StrongPassword!123",
            role = "Admin"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(accessToken, responseJson.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal(refreshToken, responseJson.RootElement.GetProperty("refreshToken").GetString());
        Assert.Equal(expectedRole, responseJson.RootElement.GetProperty("user").GetProperty("role").GetString());

        var upstreamRequest = Assert.Single(handler.Requests);
        Assert.Equal("POST", upstreamRequest.Method);
        Assert.Equal("/auth/login", upstreamRequest.Path);
        Assert.Equal(normalizedEmail, upstreamRequest.Body.GetProperty("email").GetString());
        Assert.Equal("StrongPassword!123", upstreamRequest.Body.GetProperty("password").GetString());
        Assert.Equal("turnova", upstreamRequest.Body.GetProperty("appId").GetString());
        Assert.False(upstreamRequest.Body.TryGetProperty("role", out _));
    }

    [Fact]
    public async Task DriverMeEndpointReturnsDriverProfileAndForwardsExactBearerToken()
    {
        var accessToken = CreateToken("Driver");
        using var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/auth/me", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {"id":"driver-1","email":"driver@example.com","name":"Driver","role":"Driver"}
                """));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profileBody = await response.Content.ReadAsStringAsync();
        using var profile = JsonDocument.Parse(profileBody);
        Assert.Equal("driver-1", profile.RootElement.GetProperty("id").GetString());
        Assert.Equal("driver@example.com", profile.RootElement.GetProperty("email").GetString());
        Assert.Equal("Driver", profile.RootElement.GetProperty("role").GetString());
        Assert.DoesNotContain("accessToken", profileBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", profileBody, StringComparison.OrdinalIgnoreCase);

        var upstreamRequest = Assert.Single(handler.Requests);
        Assert.Equal("GET", upstreamRequest.Method);
        Assert.Equal("/auth/me", upstreamRequest.Path);
        Assert.Equal("Bearer", upstreamRequest.AuthorizationScheme);
        Assert.Equal(accessToken, upstreamRequest.AuthorizationParameter);
    }

    [Theory]
    [InlineData("/api/v1/auth/rider/login", "", "password")]
    [InlineData("/api/v1/auth/rider/login", "rider@example.com", " ")]
    [InlineData("/api/v1/auth/driver/login", "", "password")]
    [InlineData("/api/v1/auth/driver/login", "driver@example.com", " ")]
    public async Task LoginWithMissingCredentialsDoesNotCallTriSend(
        string route,
        string email,
        string password)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("TriSend must not be called."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(route, new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnsupportedLoginRoleDoesNotCallTriSend()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("TriSend must not be called."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/admin/login",
            new { email = "admin@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(handler.Requests);
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
    public async Task RiderLoginRejectsDriverAccountAndRevokesReturnedSession()
    {
        using var handler = new RecordingHandler((request, _) =>
        {
            if (request.Path == "/auth/login")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    """{"accessToken":"wrong-role-access-token","refreshToken":"refresh","user":{"role":"Driver"}}"""));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"success":true}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/login",
            new { email = "driver@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(new[] { "/auth/login", "/auth/logout" }, handler.Requests.Select(x => x.Path));
        Assert.Equal("Bearer", handler.Requests[1].AuthorizationScheme);
        Assert.Equal("wrong-role-access-token", handler.Requests[1].AuthorizationParameter);
    }

    [Theory]
    [InlineData("opaque-refresh-token")]
    [InlineData(" opaque-refresh-token ")]
    public async Task RefreshForwardsNonBlankOpaqueTokenWithoutRewritingIt(string refreshToken)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            """{"accessToken":"new-access","refreshToken":"new-refresh"}""")));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/auth/refresh", request.Path);
        Assert.Equal(refreshToken, request.Body.GetProperty("refreshToken").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"refreshToken\":null}")]
    [InlineData("{\"refreshToken\":\"\"}")]
    [InlineData("{\"refreshToken\":\" \\t\\r\\n \"}")]
    public async Task RefreshRejectsMissingOrWhitespaceTokenWithoutCallingTriSend(string payload)
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("TriSend must not be called for a missing refresh token."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/auth/refresh",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(handler.Requests);
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
            "Bearer", CreateToken(notBefore: DateTime.UtcNow.AddMinutes(-10), expires: DateTime.UtcNow.AddMinutes(-5)));

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
    [InlineData("Auth:Jwt:Issuer", " ", true)]
    [InlineData("Auth:Jwt:Audience", " ", true)]
    [InlineData("Auth:Jwt:SigningKey", " ", false)]
    public void StartupRejectsMissingJwtConfiguration(string setting, string value, bool expectConfigurationMessage)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Startup validation must run before TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { [setting] = value });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        // A blank signing key can make WebApplicationFactory surface its host-disposal
        // wrapper after ValidateOnStart has already rejected the configuration. The
        // startup-failure and no-upstream-call assertions are the contract for that case.
        if (expectConfigurationMessage)
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
        DateTime? expires = null,
        bool demo = false)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "driver-1"),
            new("role", role)
        };
        if (demo)
            claims.Add(new Claim("demo", "true"));
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
        IReadOnlyDictionary<string, string?>? overrides = null,
        string environmentName = "Testing") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
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
