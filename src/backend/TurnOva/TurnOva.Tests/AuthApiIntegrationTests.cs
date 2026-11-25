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
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                CreateToken(includeSecondRole: true));

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
    [InlineData("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")]
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
    public async Task UnionManagerRoleRemainsValidForScopedPartnerApi()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            CreateToken(roleValue: "UnionManager", unionIdValue: "union-1"));

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
            "Bearer",
            CreateToken(roleValue: "Ops"));

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
        var schemes = root.GetProperty("components").GetProperty("securitySchemes");

        var bearer = schemes.GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
        Assert.Equal("TurnOva-issued JWT access token.", bearer.GetProperty("description").GetString());

        var operationsKey = schemes.GetProperty("OpsAdminKey");
        Assert.Equal("apiKey", operationsKey.GetProperty("type").GetString());
        Assert.Equal("header", operationsKey.GetProperty("in").GetString());
        Assert.Equal("X-Driver-Admin-Key", operationsKey.GetProperty("name").GetString());

        var paths = root.GetProperty("paths");
        var pathNames = paths.EnumerateObject().Select(path => path.Name).ToArray();
        Assert.Contains("/api/v1/auth/{role}/register", pathNames);
        Assert.Contains("/api/v1/auth/{role}/login", pathNames);
        Assert.Contains("/api/v1/auth/refresh", pathNames);
        Assert.Contains("/api/v1/auth/me", pathNames);
        Assert.Contains("/api/v1/rider/discovery", pathNames);
        Assert.Contains("/api/v1/rider/bookings", pathNames);
        Assert.Contains("/api/v1/union/reconciliation", pathNames);
        Assert.Contains("/api/v1/driver/rides/{rideId}/payment-report", pathNames);
        Assert.Contains("/api/v1/assistance/trip-intent", pathNames);
        Assert.Contains("/api/v1/assistance/help", pathNames);
        Assert.DoesNotContain(pathNames, path => path.Contains("/__test/throw", StringComparison.Ordinal));

        Assert.True(HasSecurityScheme(paths.GetProperty("/api/v1/auth/me").GetProperty("get"), "Bearer"));
        Assert.True(HasSecurityScheme(paths.GetProperty("/api/v1/rider/discovery").GetProperty("get"), "Bearer"));
        Assert.True(HasSecurityScheme(paths.GetProperty("/api/v1/rider/bookings").GetProperty("post"), "Bearer"));
        Assert.True(HasSecurityScheme(paths.GetProperty("/api/v1/union/reconciliation").GetProperty("get"), "Bearer"));
        Assert.True(HasSecurityScheme(
            paths.GetProperty("/api/v1/driver/rides/{rideId}/payment-report").GetProperty("post"),
            "Bearer"));

        Assert.False(HasAnySecurityRequirement(paths.GetProperty("/api/v1/auth/{role}/login").GetProperty("post")));
        Assert.False(HasAnySecurityRequirement(paths.GetProperty("/api/v1/auth/{role}/register").GetProperty("post")));
        Assert.False(HasAnySecurityRequirement(paths.GetProperty("/api/v1/auth/refresh").GetProperty("post")));

        var operations = paths.EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/v1/ops/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value)
            .ToArray();
        Assert.NotEmpty(operations);
        Assert.All(operations, operation =>
        {
            Assert.True(HasSecurityScheme(operation, "OpsAdminKey"));
            Assert.False(HasSecurityScheme(operation, "Bearer"));
        });

        var publicAuthPaths = new[]
        {
            "/api/v1/auth/{role}/register",
            "/api/v1/auth/{role}/login",
            "/api/v1/auth/refresh"
        };
        foreach (var path in paths.EnumerateObject().Where(path =>
                     path.Name.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase)))
        {
            var isOperationsPath =
                path.Name.Equals("/api/v1/ops", StringComparison.OrdinalIgnoreCase) ||
                path.Name.StartsWith("/api/v1/ops/", StringComparison.OrdinalIgnoreCase);
            foreach (var operation in path.Value.EnumerateObject().Where(operation =>
                         operation.Name is "get" or "put" or "post" or "delete" or "options" or "head" or "patch" or "trace"))
            {
                if (isOperationsPath)
                {
                    Assert.True(HasSecurityScheme(operation.Value, "OpsAdminKey"),
                        $"Missing OpsAdminKey security for {operation.Name} {path.Name}.");
                    Assert.False(HasSecurityScheme(operation.Value, "Bearer"),
                        $"Unexpected Bearer security for operations route {operation.Name} {path.Name}.");
                }
                else if (publicAuthPaths.Contains(path.Name, StringComparer.OrdinalIgnoreCase))
                {
                    Assert.False(HasAnySecurityRequirement(operation.Value),
                        $"Unexpected security requirement for public auth route {operation.Name} {path.Name}.");
                }
                else
                {
                    Assert.True(HasSecurityScheme(operation.Value, "Bearer"),
                        $"Missing Bearer security for authenticated route {operation.Name} {path.Name}.");
                    Assert.False(HasSecurityScheme(operation.Value, "OpsAdminKey"),
                        $"Unexpected OpsAdminKey security for authenticated route {operation.Name} {path.Name}.");
                }
            }
        }
    }

    [Fact]
    public async Task OpenApiDocumentIsNotMappedInStaging()
    {
        using var factory = new AuthApiFactory(environmentName: "Staging");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static bool HasSecurityScheme(JsonElement operation, string schemeName) =>
        operation.TryGetProperty("security", out var security) &&
        security.ValueKind == JsonValueKind.Array &&
        security.EnumerateArray().Any(requirement => requirement.TryGetProperty(schemeName, out _));

    private static bool HasAnySecurityRequirement(JsonElement operation) =>
        operation.TryGetProperty("security", out var security) &&
        security.ValueKind == JsonValueKind.Array &&
        security.GetArrayLength() > 0;

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
        using var factory = new AuthApiFactory(new Dictionary<string, string?> { ["Auth:Jwt:SigningKey"] = "too-short" });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("Auth:Jwt", exception.ToString(), StringComparison.Ordinal);
    }

    private static string CreateToken(bool includeSubject = true, bool includeNameId = false, string algorithm = SecurityAlgorithms.HmacSha256, bool includeSecondRole = false, bool includeRole = true, string roleValue = "Rider", string? nameIdValue = null, string? unionIdValue = null, string? alternateRoleClaimType = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, algorithm);
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

    private sealed class AuthApiFactory(IReadOnlyDictionary<string, string?>? overrides = null, string environmentName = "Testing")
        : WebApplicationFactory<Program>
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
