using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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

    [Theory]
    [InlineData("rider")]
    [InlineData("driver")]
    public async Task DemoAuthenticationEndpointsAreNotAvailable(string role)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/v1/auth/demo/{role}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Theory]
    [InlineData("rider")]
    [InlineData("driver")]
    public async Task RegistrationRequiresConfiguredCosmosStorage(string routeRole)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync($"/api/v1/auth/{routeRole}/register",
            new { email = "new.user@example.com", password = "SecurePass123", name = "Test User" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStoreHeaders(response);
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
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task RegistrationRejectsWeakPasswordBeforeStorageAccess()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/register",
            new { email = "new.user@example.com", password = "weak", name = "Test User" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task UnsupportedRoleIsNotAccepted()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/admin/login",
            new { email = "admin@example.com", password = "SecurePass123" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task RefreshRejectsMissingToken()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Theory]
    [InlineData("/api/v1/auth/rider/register")]
    [InlineData("/api/v1/auth/driver/register")]
    [InlineData("/api/v1/auth/rider/login")]
    [InlineData("/api/v1/auth/driver/login")]
    [InlineData("/api/v1/auth/refresh")]
    public async Task MalformedJsonOnPublicAuthEndpointsIsRejectedWithoutCaching(string path)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(path, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task RefreshRequiresConfiguredCosmosStorage()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = "synthetic-refresh-token" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task LogoutRejectsAnonymousRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task LogoutRequiresConfiguredCosmosStorage()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken());

        using var response = await client.PostAsync("/api/v1/auth/logout", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task MeRejectsAnonymousRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task InvalidBearerTokenDoesNotExposeJwtValidationDetails()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-jwt");

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error=", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_description", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsSignedJwtUsingNonConfiguredAlgorithm()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                CreateToken(algorithm: SecurityAlgorithms.HmacSha384));

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error=", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_description", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingRoleClaimIsRejectedByJwtValidation()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                CreateToken(includeRole: false));

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error=", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_description", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlankRoleClaimIsRejectedByJwtValidation(string roleValue)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                CreateToken(roleValue: roleValue));

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error=", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_description", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultipleRoleClaimsAreRejectedByJwtValidation()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken(includeSecondRole: true));

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error=", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_description", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("roles")]
    [InlineData(ClaimTypes.Role)]
    public async Task AlternateRoleClaimAliasesAreRejectedAlongsideCanonicalRole(string alternateRoleClaimType)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                CreateToken(alternateRoleClaimType: alternateRoleClaimType));

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error=", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_description", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RideHubNegotiationRequiresAnAuthenticatedJwt()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/hubs/rides/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task RideHubNegotiationAcceptsQueryBearerOnlyOnRideHub()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var accessToken = Uri.EscapeDataString(CreateToken());

        using var hubResponse = await client.PostAsync(
            $"/hubs/rides/negotiate?negotiateVersion=1&access_token={accessToken}",
            null);

        Assert.Equal(HttpStatusCode.OK, hubResponse.StatusCode);

        using var apiResponse = await client.GetAsync($"/api/v1/auth/me?access_token={accessToken}");

        Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);
        AssertNoStoreHeaders(apiResponse);
    }

    [Fact]
    public async Task SubjectlessSignedTokenCannotNegotiateRideHub()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var accessToken = Uri.EscapeDataString(CreateToken(includeSubject: false));

        using var response = await client.PostAsync(
            $"/hubs/rides/negotiate?negotiateVersion=1&access_token={accessToken}",
            null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoStoreHeaders(response);
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
        AssertNoStoreHeaders(response);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(body?.User);
        Assert.Equal("rider-123", body.User.Id);
        Assert.Equal("rider@example.com", body.User.Email);
        Assert.Equal("Rider", body.User.Role);
    }

    [Fact]
    public async Task SubjectlessSignedTokenCannotReadIdentity()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken(includeSubject: false));
        using var response = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task AmbiguousSubjectClaimsAreRejectedByJwtValidation()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken(includeNameId: true));
        using var response = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task DuplicateSubjectAliasesAreRejectedEvenWhenTheirValuesMatch()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            CreateToken(includeNameId: true, nameIdValue: "rider-123"));

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error=", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_description", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("FleetManager")]
    [InlineData("Operations")]
    public async Task RolesOutsideTheMvpJwtContractAreRejectedByJwtValidation(string roleValue)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(roleValue: roleValue));

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStoreHeaders(response);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error=", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_description", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnionManagerRoleRemainsValidForScopedPartnerApi()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(roleValue: "UnionManager", unionIdValue: "union-123"));

        using var response = await client.GetAsync("/api/v1/union/drivers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStoreHeaders(response);
        var drivers = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(drivers);
        Assert.Empty(drivers);
    }

    [Fact]
    public async Task OpsJwtIsNonPrivilegedAndDoesNotReplaceOperationsAdminKey()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateToken(roleValue: "Ops"));

        using var zones = await client.GetAsync("/api/v1/zones");
        Assert.Equal(HttpStatusCode.OK, zones.StatusCode);
        AssertNoStoreHeaders(zones);

        using var operations = await client.GetAsync("/api/v1/ops/unions");
        Assert.Equal(HttpStatusCode.Unauthorized, operations.StatusCode);
        AssertNoStoreHeaders(operations);
    }

    [Fact]
    public async Task PublicAuthEndpointsAreRateLimitedByServerObservedRemoteAddress()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var lastStatus = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/rider/login", new { email = "rider@example.com", password = "SecurePass123" });
            lastStatus = response.StatusCode;
            if (attempt < 10) Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            if (attempt == 10) AssertNoStoreHeaders(response);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }

    [Fact]
    public async Task UnhandledAuthRouteExceptionStillReturnsNoStoreHeaders()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/auth/__test/throw");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertNoStoreHeaders(response);
        Assert.DoesNotContain("sensitive-auth-test-exception", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnmatchedAuthRouteReturnsNotFoundWithNoStoreHeaders()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/auth/not-a-route");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertNoStoreHeaders(response);
    }

    [Fact]
    public async Task CorsPreflightForPublicAuthRouteReceivesNoStoreHeaders()
    {
        using var factory = new AuthApiFactory(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://client.example"
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/rider/login");
        request.Headers.Add("Origin", "https://client.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertNoStoreHeaders(response);
        Assert.Contains("https://client.example", response.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task OpenApiDocumentDescribesCoreRoutesAndAccurateSecuritySchemes()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("TurnOva API", root.GetProperty("info").GetProperty("title").GetString());

        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/rider/bookings", out var booking));
        AssertOperationRequiresBearer(booking.GetProperty("post"));
        Assert.True(paths.TryGetProperty("/api/v1/driver/payments/report", out var paymentReport));
        AssertOperationRequiresBearer(paymentReport.GetProperty("post"));
        Assert.True(paths.TryGetProperty("/api/v1/union/reconciliation", out var reconciliation));
        AssertOperationRequiresBearer(reconciliation.GetProperty("get"));
        Assert.True(paths.TryGetProperty("/api/v1/ops/drivers", out var operationsDrivers));
        AssertOperationRequiresOperationsKey(operationsDrivers.GetProperty("get"));
        Assert.True(paths.TryGetProperty("/api/v1/ops/unions", out var operationsUnions));
        AssertOperationRequiresOperationsKey(operationsUnions.GetProperty("get"));
        Assert.True(paths.TryGetProperty("/api/v1/auth/{role}/register", out var register));
        AssertOperationHasNoSecurity(register.GetProperty("post"));
        Assert.True(paths.TryGetProperty("/api/v1/auth/{role}/login", out var login));
        AssertOperationHasNoSecurity(login.GetProperty("post"));
        Assert.True(paths.TryGetProperty("/api/v1/auth/refresh", out var refresh));
        AssertOperationHasNoSecurity(refresh.GetProperty("post"));
        Assert.True(paths.TryGetProperty("/api/v1/auth/logout", out var logout));
        AssertOperationRequiresBearer(logout.GetProperty("post"));
        Assert.True(paths.TryGetProperty("/api/v1/auth/me", out var me));
        AssertOperationRequiresBearer(me.GetProperty("get"));
        Assert.False(paths.TryGetProperty("/api/v1/auth/__test/throw", out _));
        Assert.False(paths.TryGetProperty("/api/v1/auth/__test/private-cache", out _));
        Assert.True(root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out var bearer));
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Contains("TurnOva", bearer.GetProperty("bearerFormat").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("TriSend", bearer.GetProperty("bearerFormat").GetString(), StringComparison.Ordinal);
        Assert.True(root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("OpsAdminKey", out var operationsKey));
        Assert.Equal("apiKey", operationsKey.GetProperty("type").GetString());
        Assert.Equal("header", operationsKey.GetProperty("in").GetString());
        Assert.Equal("X-Ops-Admin-Key", operationsKey.GetProperty("name").GetString());
    }

    [Fact]
    public async Task OpenApiDocumentIsNotMappedInStaging()
    {
        using var factory = new AuthApiFactory(environmentName: "Staging");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static void AssertOperationRequiresBearer(JsonElement operation)
    {
        Assert.True(operation.TryGetProperty("security", out var security));
        Assert.Equal(1, security.GetArrayLength());
        Assert.True(security[0].TryGetProperty("Bearer", out _));
        Assert.False(security[0].TryGetProperty("OpsAdminKey", out _));
    }

    private static void AssertOperationRequiresOperationsKey(JsonElement operation)
    {
        Assert.True(operation.TryGetProperty("security", out var security));
        Assert.Equal(1, security.GetArrayLength());
        Assert.True(security[0].TryGetProperty("OpsAdminKey", out _));
        Assert.False(security[0].TryGetProperty("Bearer", out _));
    }

    private static void AssertOperationHasNoSecurity(JsonElement operation)
    {
        Assert.False(operation.TryGetProperty("security", out _));
    }

    private static void AssertNoStoreHeaders(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
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
        using var factory = new AuthApiFactory(new Dictionary<string, string?>
        {
            ["Auth:Jwt:SigningKey"] = "too-short"
        }, startImmediately: false);

        Assert.Throws<InvalidOperationException>(() => factory.StartServer());
    }

    private static string CreateToken(
        string roleValue = "Rider",
        bool includeSubject = true,
        bool includeNameId = false,
        string? nameIdValue = null,
        bool includeRole = true,
        bool includeSecondRole = false,
        string? alternateRoleClaimType = null,
        string? unionIdValue = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), algorithm);
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Email, "rider@example.com"),
            new Claim("name", "Test Rider")
        };
        if (includeRole) claims.Add(new Claim("role", roleValue));
        if (includeSubject) claims.Add(new Claim(JwtRegisteredClaimNames.Sub, "rider-123"));
        if (includeNameId) claims.Add(new Claim("nameid", nameIdValue ?? "second-subject"));
        if (unionIdValue is not null) claims.Add(new Claim("unionId", unionIdValue));
        if (includeSecondRole) claims.Add(new Claim("role", "Driver"));
        if (alternateRoleClaimType is not null) claims.Add(new Claim(alternateRoleClaimType, "Rider"));
        var token = new JwtSecurityToken(Issuer, Audience, claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record MeResponse(MeUser User);
    private sealed record MeUser(string Id, string Email, string? Name, string Role);
}
