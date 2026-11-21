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
    private const int MaximumAuthResponseBytes = 256 * 1024;

    [Fact]
    public async Task DemoSessionIsDisabledUnlessExplicitlyEnabled()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo auth must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/auth/demo/rider", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void TriSendPrimaryHandlerDisablesAutomaticRedirects()
    {
        using var primaryHandler = TriSendHttpClientPolicy.CreatePrimaryHandler();

        Assert.False(primaryHandler.AllowAutoRedirect);
    }

    [Fact]
    public void TriSendPrimaryHandlerDisablesAutomaticCookiePersistence()
    {
        using var primaryHandler = TriSendHttpClientPolicy.CreatePrimaryHandler();

        Assert.False(primaryHandler.UseCookies);
    }


    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.RedirectMethod)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task AuthenticationUpstreamRedirectIsRejectedWithoutExposingRedirectDetails(HttpStatusCode redirectStatus)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(redirectStatus)
        {
            Headers = { Location = new Uri("https://untrusted.example/collect") },
            Content = new StringContent("sensitive-upstream-redirect-body")
        }));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "SensitivePassword123!" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Null(response.Headers.Location);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("untrusted.example", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-upstream-redirect-body", body, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
        Assert.Equal("/auth/login", handler.Requests[0].Path);
    }

    [Fact]
    public async Task UnhandledAuthRouteExceptionStillReturnsNoStoreHeaders()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Auth error path must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/__test/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive-auth-test-exception", body, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnhandledApiExceptionOutsideAuthStillReturnsNoStoreHeaders()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("An API error must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/__test/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive-api-test-exception", body, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuthRouteCachePolicyPreservesPrivateDirectivesAndAddsNoStore()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("The cache-policy fixture must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/__test/private-cache");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.Private);
        Assert.True(cacheControl.NoStore);
        Assert.Equal(TimeSpan.FromSeconds(60), cacheControl.MaxAge);
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuthRouteCorsPreflightStillReturnsNoStoreHeaders()
    {
        const string allowedOrigin = "https://rider.example";
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("CORS preflight must not call TriSend."));
        using var factory = new AuthApiFactory(
            handler,
            new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = allowedOrigin });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/rider/login");
        request.Headers.Add("Origin", allowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(allowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuthRouteCorsPreflightFromDisallowedOriginStillReturnsNoStoreHeaders()
    {
        const string allowedOrigin = "https://rider.example";
        const string disallowedOrigin = "https://untrusted.example";
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("Rejected CORS preflight must not call TriSend."));
        using var factory = new AuthApiFactory(
            handler,
            new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = allowedOrigin });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/rider/login");
        request.Headers.Add("Origin", disallowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RideHubCorsPreflightReturnsNoStoreHeaders()
    {
        const string allowedOrigin = "https://rider.example";
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("Ride-hub CORS preflight must not call TriSend."));
        using var factory = new AuthApiFactory(
            handler,
            new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = allowedOrigin });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/rides/negotiate");
        request.Headers.Add("Origin", allowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(allowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        AssertNoStoreHeaders(response);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnmatchedAuthRouteReturnsNotFoundWithNoStoreHeaders()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("An unmatched auth route must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/__test/not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnmatchedNonAuthApiRouteReturnsNotFoundWithNoStoreHeaders()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("An unmatched API route must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnsupportedMethodOnAuthenticationRouteReturnsMethodNotAllowedWithNoStoreHeaders()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("A method mismatch on an auth route must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/auth/me", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuthRouteHttpsRedirectStillReturnsNoStoreHeaders()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("HTTPS redirection must not call TriSend."));
        using var factory = new AuthApiFactory(
            handler,
            new Dictionary<string, string?> { ["HTTPS_PORT"] = "443" },
            environmentName: "Development");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(Uri.UriSchemeHttps, response.Headers.Location!.Scheme);
        Assert.Equal("localhost", response.Headers.Location.Host);
        Assert.Equal("/api/v1/auth/me", response.Headers.Location.AbsolutePath);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("isDemo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("refreshToken").ValueKind);
        Assert.Equal(1800, body.RootElement.GetProperty("expiresIn").GetInt32());
        var user = body.RootElement.GetProperty("user");
        Assert.True(user.GetProperty("isDemo").GetBoolean());
        Assert.Equal(expectedRole, user.GetProperty("role").GetString());
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
            }
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

    [Fact]
    public async Task DemoTokenCannotNegotiateSignalRHubUsingQueryStringAccessToken()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Demo token must not reach SignalR or TriSend."));
        using var factory = new AuthApiFactory(
            handler,
            new Dictionary<string, string?> { ["DemoAuth:Enabled"] = "true" });
        using var client = factory.CreateClient();

        using var issued = await client.PostAsync("/api/v1/auth/demo/rider", null);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var issuedBody = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var accessToken = issuedBody.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        // SignalR's browser/mobile transport places access tokens in the query string
        // on the negotiate request. This special JWT extraction path must not bypass
        // the demo-token restriction applied to normal authenticated API routes.
        var negotiatePath = $"/hubs/rides/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(accessToken!)}";
        using var response = await client.PostAsync(negotiatePath, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnmatchedRideHubRouteReturnsNotFoundWithNoStoreHeaders()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Unmatched hub routes must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/hubs/rides/unmapped");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertNoStoreHeaders(response);
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
            JsonSerializer.Serialize(new { accessToken = CreateToken("Driver"), refreshToken = "refresh", user = new { role = "Driver" } }))));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/driver/register",
            new { email = "  DRIVER@example.com  ", password = "StrongPassword!123", name = "  Test Driver  ", role = "Admin" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
            JsonSerializer.Serialize(new { accessToken = CreateToken("Rider"), refreshToken = "refresh", user = new { role = "Rider" } }))));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/register",
            new { email = "  RIDER@example.com  ", password = "StrongPassword!123", name = "  Test Rider  ", role = "Admin" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
                    JsonSerializer.Serialize(new { accessToken = CreateToken("Rider"), refreshToken = "first-refresh", user = new { role = "Rider" } })));
            }

            return Task.FromResult(JsonResponse(HttpStatusCode.Conflict,
                """{"message":"Email already registered."}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        var payload = new { email = "Rider@example.com", password = "StrongPassword!123", name = "Existing Rider" };

        using var first = await client.PostAsJsonAsync("/api/v1/auth/rider/register", payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        Assert.Contains(first.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));

        using var second = await client.PostAsJsonAsync("/api/v1/auth/rider/register", payload);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("no-store", second.Headers.CacheControl?.ToString());
        Assert.Contains(second.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("Email already registered.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Basic")]
    [InlineData("Bearer")]
    [InlineData("Negotiate token")]
    [InlineData(" ")]
    [InlineData("Bearer first, second=two")]
    [InlineData("Bearer token with-space")]
    [InlineData("Bearer =padding")]
    [InlineData("Bearer token=padding=extra")]
    public async Task LogoutRejectsUnsupportedAuthorizationBeforeCallingTriSend(string authorization)
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("Unsupported logout authorization must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);

        using var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("dXNlcjpwYXNz", body, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LogoutRejectsMultipleAuthorizationFieldValuesBeforeCallingTriSend()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("Ambiguous logout authorization must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization",
            new[] { "Bearer first-token", "Bearer second-token" });

        using var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("first-token", body, StringComparison.Ordinal);
        Assert.DoesNotContain("second-token", body, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LogoutWithoutAuthorizationHeaderKeepsBestEffortForwarding()
    {
        using var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}")));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStoreHeaders(response);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/auth/logout", request.Path);
        Assert.Null(request.AuthorizationScheme);
        Assert.Null(request.AuthorizationParameter);
    }

    [Theory]
    [InlineData("expired-or-invalid-token")]
    [InlineData("opaque+/._~-token==")]
    public async Task LogoutForwardsExpiredBearerUnchangedWithoutLocalTokenValidation(string expiredBearer)
    {
        using var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}")));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization", $"Bearer {expiredBearer}");

        using var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStoreHeaders(response);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/auth/logout", request.Path);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(expiredBearer, request.AuthorizationParameter);
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MeEndpointRejectsInvalidBearerTokenWithNoStoreHeaders()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("Invalid bearer profile reads must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-valid-jwt");

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
        var demoSessionSchema = successSchema;
        if (successSchema.TryGetProperty("$ref", out var successSchemaRef))
        {
            var schemaName = successSchemaRef.GetString()!.Split('/').Last();
            demoSessionSchema = root.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        }
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
    [InlineData("/api/v1/auth/rider/login", "RIDER@example.com", "rider@example.com", "Rider")]
    [InlineData("/api/v1/auth/driver/login", "DRIVER@example.com", "driver@example.com", "Driver")]
    public async Task SuccessfulRiderAndDriverLoginForwardsNormalizedIdentityAndReturnsMatchingSession(
        string route,
        string submittedEmail,
        string normalizedEmail,
        string expectedRole)
    {
        var accessToken = CreateToken(expectedRole);
        const string refreshToken = "valid-refresh-token";
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "/auth/login", "/auth/logout" }, handler.Requests.Select(x => x.Path));
        Assert.Equal("Bearer", handler.Requests[1].AuthorizationScheme);
        Assert.Equal("wrong-role-access-token", handler.Requests[1].AuthorizationParameter);
    }


    [Fact]
    public async Task LoginRejectsJwtRoleThatDiffersFromTheUserRoleAndRevokesReturnedSession()
    {
        var accessToken = CreateToken("Rider");
        using var handler = new RecordingHandler((request, _) =>
        {
            if (request.Path == "/auth/login")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    accessToken,
                    refreshToken = "refresh",
                    user = new { role = "Driver" }
                })));

            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"success":true}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/driver/login",
            new { email = "driver@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoStoreHeaders(response);
        Assert.Equal(new[] { "/auth/login", "/auth/logout" }, handler.Requests.Select(x => x.Path));
        Assert.Equal(accessToken, handler.Requests[1].AuthorizationParameter);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("missing-role")]
    [InlineData("missing-subject")]
    [InlineData("duplicate-subject")]
    [InlineData("duplicate-mixed-subject")]
    [InlineData("empty-subject")]
    [InlineData("duplicate-mapped-legacy-subject")]
    [InlineData("empty-mapped-legacy-subject")]
    public async Task LoginRejectsAccessTokenThatDoesNotMatchApiValidationPolicy(string failure)
    {
        var accessToken = failure switch
        {
            "signature" => CreateToken("Driver", signingKey: "different-signing-key-for-tests-32-bytes"),
            "issuer" => CreateToken("Driver", issuer: "wrong-issuer"),
            "audience" => CreateToken("Driver", audience: "wrong-audience"),
            "expired" => CreateToken("Driver", notBefore: DateTime.UtcNow.AddMinutes(-10), expires: DateTime.UtcNow.AddMinutes(-5)),
            "missing-role" => CreateToken(includeRole: false),
            "missing-subject" => CreateToken(subjectClaimCount: 0),
            "duplicate-subject" => CreateToken(subjectClaimCount: 2),
            "duplicate-mixed-subject" => CreateToken(subjectClaimCount: 1, includeLegacySubject: true),
            "empty-subject" => CreateToken(subjectClaimCount: 1, subjectValue: " "),
            "duplicate-mapped-legacy-subject" => CreateToken(subjectClaimCount: 1, includeMappedLegacySubject: true),
            "empty-mapped-legacy-subject" => CreateToken(subjectClaimCount: 0, includeMappedLegacySubject: true, mappedLegacySubjectValue: " "),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        using var handler = new RecordingHandler((request, _) =>
        {
            if (request.Path == "/auth/login")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    accessToken,
                    refreshToken = "refresh",
                    user = new { role = "Driver" }
                })));

            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"success":true}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/driver/login",
            new { email = "driver@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(accessToken, body, StringComparison.Ordinal);
        Assert.Equal(new[] { "/auth/login", "/auth/logout" }, handler.Requests.Select(x => x.Path));
        Assert.Equal(accessToken, handler.Requests[1].AuthorizationParameter);
    }

    [Fact]
    public async Task LoginAcceptsSingleLegacyNameIdentifierSubjectClaim()
    {
        var accessToken = CreateToken(subjectClaimCount: 0, includeLegacySubject: true);
        using var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/auth/login", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                accessToken,
                refreshToken = "refresh",
                user = new { role = "Driver" }
            })));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/driver/login",
            new { email = "driver@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStoreHeaders(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(accessToken, body.RootElement.GetProperty("accessToken").GetString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoginAcceptsSingleMappedNameIdSubjectClaim()
    {
        var accessToken = CreateToken(subjectClaimCount: 0, includeMappedLegacySubject: true);
        using var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/auth/login", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                accessToken,
                refreshToken = "refresh",
                user = new { role = "Driver" }
            })));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/driver/login",
            new { email = "driver@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStoreHeaders(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(accessToken, body.RootElement.GetProperty("accessToken").GetString());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("whitespace")]
    [InlineData("oversized")]
    public async Task LoginDoesNotAttemptRevocationWithMalformedOrOversizedAccessToken(string failure)
    {
        var accessToken = failure == "whitespace"
            ? "malformed access token"
            : new string('a', 16 * 1024 + 1);
        using var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/auth/login", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                accessToken,
                refreshToken = "refresh",
                user = new { role = "Driver" }
            })));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/driver/login",
            new { email = "driver@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(accessToken, body, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("not-json provider-secret", false)]
    [InlineData("[]", false)]
    [InlineData("{\"user\":{\"role\":\"Rider\"}}", false)]
    [InlineData("{\"accessToken\":\"upstream-access-token\",\"refreshToken\":\"upstream-refresh-token\"}", true)]
    [InlineData("{\"accessToken\":\"upstream-access-token\",\"user\":{\"role\":\"Rider\"}}", true)]
    [InlineData("{\"accessToken\":\"upstream-access-token\",\"refreshToken\":\"upstream-refresh-token\",\"user\":{\"role\":17}}", true)]
    [InlineData("{\"accessToken\":\"upstream-access-token\",\"refreshToken\":\"upstream-refresh-token\",\"user\":[]}", true)]
    [InlineData("{\"accessToken\":\"upstream-access-token\",\"accessToken\":\"second-access-token\",\"refreshToken\":\"upstream-refresh-token\",\"user\":{\"role\":\"Rider\"}}", false)]
    [InlineData("{\"accessToken\":\"upstream-access-token\",\"refreshToken\":\"first-refresh\",\"refreshToken\":\"second-refresh\",\"user\":{\"role\":\"Rider\"}}", true)]
    [InlineData("{\"accessToken\":\"upstream-access-token\",\"refreshToken\":\"upstream-refresh-token\",\"user\":{\"role\":\"Rider\"},\"user\":{\"role\":\"Rider\"}}", true)]
    [InlineData("{\"accessToken\":\"upstream-access-token\",\"refreshToken\":\"upstream-refresh-token\",\"user\":{\"role\":\"Rider\",\"role\":\"Driver\"}}", true)]
    public async Task MalformedSuccessfulAuthenticationResponseIsRejectedWithoutExposingUpstreamContent(
        string upstreamBody,
        bool expectedRevocation)
    {
        using var handler = new RecordingHandler((request, _) =>
        {
            if (request.Path == "/auth/login")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, upstreamBody));

            Assert.Equal("/auth/logout", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"success":true}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "password" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid response", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upstream-access-token", body, StringComparison.Ordinal);
        Assert.DoesNotContain("upstream-refresh-token", body, StringComparison.Ordinal);

        var expectedPaths = expectedRevocation
            ? new[] { "/auth/login", "/auth/logout" }
            : new[] { "/auth/login" };
        Assert.Equal(expectedPaths, handler.Requests.Select(request => request.Path));
        if (expectedRevocation)
        {
            Assert.Equal("Bearer", handler.Requests[1].AuthorizationScheme);
            Assert.Equal("upstream-access-token", handler.Requests[1].AuthorizationParameter);
        }
    }

    [Theory]
    [InlineData("not-json provider-secret", false)]
    [InlineData("[]", false)]
    [InlineData("{\"refreshToken\":\"next-refresh\"}", false)]
    [InlineData("{\"accessToken\":\"next-access-token\"}", true)]
    [InlineData("{\"accessToken\":\"next-access-token\",\"refreshToken\":17}", true)]
    [InlineData("{\"accessToken\":\"next-access-token\",\"accessToken\":\"second-access-token\",\"refreshToken\":\"next-refresh\"}", false)]
    [InlineData("{\"accessToken\":\"next-access-token\",\"refreshToken\":\"first-refresh\",\"refreshToken\":\"second-refresh\"}", true)]
    public async Task MalformedSuccessfulRefreshResponseIsRejectedAndRevokedWhenPossible(
        string upstreamBody,
        bool expectedRevocation)
    {
        using var handler = new RecordingHandler((request, _) =>
        {
            if (request.Path == "/auth/refresh")
            {
                Assert.Equal("opaque-refresh-token", request.Body.GetProperty("refreshToken").GetString());
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, upstreamBody));
            }

            Assert.Equal("/auth/logout", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"success":true}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = "opaque-refresh-token" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid response", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("next-access-token", body, StringComparison.Ordinal);
        Assert.DoesNotContain("next-refresh", body, StringComparison.Ordinal);

        var expectedPaths = expectedRevocation
            ? new[] { "/auth/refresh", "/auth/logout" }
            : new[] { "/auth/refresh" };
        Assert.Equal(expectedPaths, handler.Requests.Select(request => request.Path));
        if (expectedRevocation)
        {
            Assert.Equal("Bearer", handler.Requests[1].AuthorizationScheme);
            Assert.Equal("next-access-token", handler.Requests[1].AuthorizationParameter);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedAuthenticationResponseIsRejectedForKnownAndUnknownLengthBodies(bool knownLength)
    {
        var payload = Encoding.UTF8.GetBytes(
            """{"accessToken":"provider-secret","refreshToken":"next-refresh"}""" +
            new string('x', MaximumAuthResponseBytes * 4));
        NonSeekableReadStream? unknownLengthStream = null;
        HttpContent content;
        if (knownLength)
        {
            content = new ByteArrayContent(payload);
        }
        else
        {
            unknownLengthStream = new NonSeekableReadStream(new MemoryStream(payload));
            content = new StreamContent(unknownLengthStream);
        }
        content.Headers.ContentType = new("application/json");

        using var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "StrongPassword!123" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid response", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("next-refresh", body, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
        if (!knownLength)
            Assert.True(unknownLengthStream!.BytesRead < payload.Length,
                "The API must stop reading the upstream stream as soon as the configured size limit is exceeded.");
    }

    [Fact]
    public async Task TriSendHeaderTimeoutReturnsGatewayTimeoutWithoutLeakingDetails()
    {
        using var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new OperationCanceledException("private upstream header timeout")));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "StrongPassword!123" });

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("timed out", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private upstream header timeout", body, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TriSendBodyReadTimeoutReturnsGatewayTimeoutWithoutLeakingDetails()
    {
        using var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new TimedOutReadStream())
            }));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "StrongPassword!123" });

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("timed out", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private upstream body timeout", body, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TriSendBodyReadIOExceptionReturnsServiceUnavailableWithoutLeakingPartialBody()
    {
        const string partialBody = """{"accessToken":"provider-secret","refreshToken":"next-refresh",""";
        using var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingAfterPrefixReadStream(Encoding.UTF8.GetBytes(partialBody)))
            }));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "StrongPassword!123" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("temporarily unavailable", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("next-refresh", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private upstream stream failure", body, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TriSendHeaderIOExceptionReturnsServiceUnavailableWithoutLeakingDetails()
    {
        using var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new IOException("private upstream header transport failure")));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "StrongPassword!123" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("temporarily unavailable", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private upstream header transport failure", body, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task InvalidUtf8AuthenticationTokenIsRejectedWithoutReturningCorruptedCredentials()
    {
        var prefix = Encoding.UTF8.GetBytes("{\"accessToken\":\"provider-secret");
        var suffix = Encoding.UTF8.GetBytes("\",\"refreshToken\":\"next-refresh\",\"user\":{\"role\":\"Rider\"}}");
        var responseBody = new byte[prefix.Length + 1 + suffix.Length];
        Buffer.BlockCopy(prefix, 0, responseBody, 0, prefix.Length);
        responseBody[prefix.Length] = 0xFF;
        Buffer.BlockCopy(suffix, 0, responseBody, prefix.Length + 1, suffix.Length);

        using var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBody)
            }));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "StrongPassword!123" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid response", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("next-refresh", body, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationDuringTriSendBodyReadIsNotMappedToGatewayTimeout()
    {
        var readStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingReadStream(readStarted))
            }));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();

        var pending = client.PostAsJsonAsync(
            "/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "StrongPassword!123" },
            cancellation.Token);

        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MalformedLoginRevocationDoesNotBufferUpstreamLogoutBody()
    {
        var logoutPayload = Encoding.UTF8.GetBytes(new string('x', MaximumAuthResponseBytes * 4));
        var logoutStream = new NonSeekableReadStream(new MemoryStream(logoutPayload));
        using var handler = new RecordingHandler((request, _) =>
        {
            if (request.Path == "/auth/login")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    """{"accessToken":"revoked-access"}"""));

            if (request.Path == "/auth/logout")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(logoutStream)
                });

            throw new InvalidOperationException("Unexpected TriSend request path.");
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/rider/login",
            new { email = "rider@example.com", password = "StrongPassword!123" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid response", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revoked-access", body, StringComparison.Ordinal);
        Assert.Equal(new[] { "/auth/login", "/auth/logout" },
            handler.Requests.Select(request => request.Path));
        Assert.Equal("Bearer", handler.Requests[1].AuthorizationScheme);
        Assert.Equal("revoked-access", handler.Requests[1].AuthorizationParameter);
        Assert.Equal(0, logoutStream.BytesRead);
    }

    [Theory]
    [InlineData("opaque-refresh-token")]
    [InlineData(" opaque-refresh-token ")]
    public async Task RefreshForwardsNonBlankOpaqueTokenWithoutRewritingIt(string refreshToken)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { accessToken = CreateToken("Rider"), refreshToken = "new-refresh" }))));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/auth/refresh", request.Path);
        Assert.Equal(refreshToken, request.Body.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task RefreshAcceptsSingleMappedNameIdSubjectClaim()
    {
        var accessToken = CreateToken("Rider", subjectClaimCount: 0, includeMappedLegacySubject: true);
        using var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/auth/refresh", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                accessToken,
                refreshToken = "next-refresh-token"
            })));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = "opaque-refresh-token" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStoreHeaders(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(accessToken, body.RootElement.GetProperty("accessToken").GetString());
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/auth/refresh", request.Path);
    }

    [Theory]
    [InlineData("missing-subject")]
    [InlineData("duplicate-subject")]
    [InlineData("duplicate-mixed-subject")]
    [InlineData("empty-subject")]
    [InlineData("duplicate-mapped-legacy-subject")]
    [InlineData("empty-mapped-legacy-subject")]
    public async Task RefreshRejectsAccessTokenWithoutExactlyOneSubjectAndRevokesReturnedSession(string failure)
    {
        var accessToken = failure switch
        {
            "missing-subject" => CreateToken("Rider", subjectClaimCount: 0),
            "duplicate-subject" => CreateToken("Rider", subjectClaimCount: 2),
            "duplicate-mixed-subject" => CreateToken("Rider", subjectClaimCount: 1, includeLegacySubject: true),
            "empty-subject" => CreateToken("Rider", subjectClaimCount: 1, subjectValue: " "),
            "duplicate-mapped-legacy-subject" => CreateToken("Rider", subjectClaimCount: 1, includeMappedLegacySubject: true),
            "empty-mapped-legacy-subject" => CreateToken("Rider", subjectClaimCount: 0, includeMappedLegacySubject: true, mappedLegacySubjectValue: " "),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        using var handler = new RecordingHandler((request, _) =>
        {
            if (request.Path == "/auth/refresh")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    accessToken,
                    refreshToken = "next-refresh-token"
                })));

            Assert.Equal("/auth/logout", request.Path);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"success":true}"""));
        });
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = "opaque-refresh-token" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertNoStoreHeaders(response);
        Assert.Equal(new[] { "/auth/refresh", "/auth/logout" },
            handler.Requests.Select(request => request.Path));
        Assert.Equal(accessToken, handler.Requests[1].AuthorizationParameter);
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
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
        AssertNoStoreHeaders(response);
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
        AssertNoStoreHeaders(response);
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
        AssertNoStoreHeaders(response);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RideHubNegotiationResponseDoesNotCacheConnectionCredentials()
    {
        using var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("SignalR negotiation must not call TriSend."));
        using var factory = new AuthApiFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(role: "Driver"));

        using var response = await client.PostAsync("/hubs/rides/negotiate?negotiateVersion=1", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStoreHeaders(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("connectionToken", out var connectionToken));
        Assert.Equal(JsonValueKind.String, connectionToken.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(connectionToken.GetString()));
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
    [InlineData("http://localhost:5001")]
    [InlineData("not-a-valid-url")]
    [InlineData(" ")]
    [InlineData("https://user:secret@auth.test.invalid")]
    [InlineData("https://auth.test.invalid/?access_token=secret")]
    [InlineData("https://auth.test.invalid/#access_token=secret")]
    public void StartupRejectsNonHttpsOrInvalidTriSendBaseUrl(string baseUrl)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Startup validation must run before TriSend."));
        using var factory = new AuthApiFactory(handler, new Dictionary<string, string?> { ["TriSend:BaseUrl"] = baseUrl });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("TriSend:BaseUrl", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://localhost:5001")]
    [InlineData("http://127.0.0.1:5001")]
    [InlineData("http://[::1]:5001")]
    public void DevelopmentAllowsLoopbackHttpTriSendBaseUrl(string baseUrl)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Startup validation must run before TriSend."));
        using var factory = new AuthApiFactory(
            handler,
            new Dictionary<string, string?> { ["TriSend:BaseUrl"] = baseUrl },
            environmentName: "Development");

        using var client = factory.CreateClient();

        Assert.NotNull(client);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://auth.test.invalid")]
    [InlineData("http://192.0.2.10:5001")]
    public void DevelopmentRejectsRemoteHttpTriSendBaseUrl(string baseUrl)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Startup validation must run before TriSend."));
        using var factory = new AuthApiFactory(
            handler,
            new Dictionary<string, string?> { ["TriSend:BaseUrl"] = baseUrl },
            environmentName: "Development");

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
        bool demo = false,
        bool includeRole = true,
        int subjectClaimCount = 1,
        bool includeLegacySubject = false,
        string? subjectValue = null,
        bool includeMappedLegacySubject = false,
        string? mappedLegacySubjectValue = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>();
        for (var index = 0; index < subjectClaimCount; index++)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subjectValue ?? $"driver-{index + 1}"));
        if (includeLegacySubject)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, "legacy-driver-1"));
        if (includeMappedLegacySubject)
            claims.Add(new Claim("nameid", mappedLegacySubjectValue ?? "legacy-driver-1"));
        if (includeRole)
            claims.Add(new Claim("role", role));
        if (demo)
            claims.Add(new Claim("demo", "true"));
        var token = new JwtSecurityToken(
            issuer, audience, claims,
            notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires ?? DateTime.UtcNow.AddMinutes(5),
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void AssertNoStoreHeaders(HttpResponseMessage response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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

    private sealed class TimedOutReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new OperationCanceledException("private upstream body timeout");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Task.FromException<int>(
                new OperationCanceledException("private upstream body timeout")));

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(
                new OperationCanceledException("private upstream body timeout"));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingAfterPrefixReadStream(byte[] prefix) : Stream
    {
        private bool _prefixReturned;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_prefixReturned)
                throw new IOException("private upstream stream failure");

            _prefixReturned = true;
            var bytesToCopy = Math.Min(count, prefix.Length);
            Array.Copy(prefix, 0, buffer, offset, bytesToCopy);
            return bytesToCopy;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_prefixReturned)
                return ValueTask.FromException<int>(new IOException("private upstream stream failure"));

            _prefixReturned = true;
            var bytesToCopy = Math.Min(buffer.Length, prefix.Length);
            prefix.AsMemory(0, bytesToCopy).CopyTo(buffer);
            return ValueTask.FromResult(bytesToCopy);
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingReadStream(TaskCompletionSource<bool> readStarted) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            readStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = inner.Read(buffer, offset, count);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesRead = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var bytesRead = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            BytesRead += bytesRead;
            return bytesRead;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
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
