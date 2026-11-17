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
using Microsoft.IdentityModel.Tokens;
using TurnOva.API;

namespace TurnOva.Tests;

[CollectionDefinition(nameof(SupportRequestApiIntegrationCollection), DisableParallelization = true)]
public sealed class SupportRequestApiIntegrationCollection { }

[Collection(nameof(SupportRequestApiIntegrationCollection))]
public sealed class SupportRequestApiIntegrationTests
{
    private const string Issuer = "turnova-support-tests";
    private const string Audience = "turnova-api";
    private const string SigningKey = "turnova-support-integration-test-signing-key-32-bytes";

    [Theory]
    [InlineData("Rider")]
    [InlineData("Driver")]
    public async Task RiderAndDriverCanSubmitNeutralSupportRequests(string role)
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "support-request-0001");

        using var response = await client.PostAsJsonAsync("/api/v1/support/requests",
            new { subject = "Trip update issue", description = "The trip status did not refresh after reconnecting." });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestId = body.RootElement.GetProperty("requestId").GetString();
        Assert.NotNull(requestId);
        Assert.Equal(64, requestId!.Length);
        Assert.False(body.RootElement.GetProperty("replayed").GetBoolean());
        Assert.True(body.RootElement.TryGetProperty("submittedAtUtc", out _));
        Assert.False(body.RootElement.TryGetProperty("reporterId", out _));
        Assert.False(body.RootElement.TryGetProperty("subject", out _));
        Assert.False(body.RootElement.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task SameReporterKeyAndContentReplaysOriginalRequest()
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Rider"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "support-request-replay-01");
        var payload = new { subject = "Trip update issue", description = "The trip status did not refresh after reconnecting." };

        using var first = await client.PostAsJsonAsync("/api/v1/support/requests", payload);
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var replay = await client.PostAsJsonAsync("/api/v1/support/requests", payload);
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody.RootElement.GetProperty("requestId").GetString(),
            replayBody.RootElement.GetProperty("requestId").GetString());
        Assert.True(replayBody.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(firstBody.RootElement.GetProperty("submittedAtUtc").GetString(),
            replayBody.RootElement.GetProperty("submittedAtUtc").GetString());
    }

    [Fact]
    public async Task SameReporterKeyWithDifferentContentReturnsConflict()
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Rider"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "support-request-conflict-01");

        using var first = await client.PostAsJsonAsync("/api/v1/support/requests",
            new { subject = "Trip update issue", description = "The trip status did not refresh after reconnecting." });
        using var conflict = await client.PostAsJsonAsync("/api/v1/support/requests",
            new { subject = "Different issue", description = "I need to report different support content." });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task AnonymousCallerCannotSubmitSupportRequest()
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "support-request-anon-01");

        using var response = await client.PostAsJsonAsync("/api/v1/support/requests",
            new { subject = "Trip update issue", description = "The trip status did not refresh after reconnecting." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonRiderDriverRoleCannotSubmitSupportRequest()
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Ops"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "support-request-ops-01");

        using var response = await client.PostAsJsonAsync("/api/v1/support/requests",
            new { subject = "Trip update issue", description = "The trip status did not refresh after reconnecting." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingIdempotencyKeyIsRejected()
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Driver"));

        using var response = await client.PostAsJsonAsync("/api/v1/support/requests",
            new { subject = "Trip update issue", description = "The trip status did not refresh after reconnecting." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidSubjectOrDescriptionIsRejected()
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Rider"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "support-request-invalid-01");

        using var response = await client.PostAsJsonAsync("/api/v1/support/requests",
            new { subject = "x", description = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SupportRequestsDoNotExposeAReadOrListEndpoint()
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Rider"));

        using var response = await client.GetAsync("/api/v1/support/requests");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task UnknownCategorySeverityAssignmentAndAttachmentFieldsAreRejected()
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Rider"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "support-request-policy-01");
        using var content = new StringContent(
            """{"subject":"Trip update issue","description":"The trip status did not refresh after reconnecting.","category":"Safety","severity":"Critical","assignedTo":"Ops","attachments":["https://example.invalid/file"]}""",
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/api/v1/support/requests", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocumentsSupportRouteWithBearerAndOnlyNeutralBodyFields()
    {
        using var factory = new SupportApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var operation = root.GetProperty("paths")
            .GetProperty("/api/v1/support/requests")
            .GetProperty("post");
        Assert.True(operation.GetProperty("security").GetArrayLength() > 0);

        var schema = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var schemaReference))
        {
            var schemaName = schemaReference.GetString()!.Split('/').Last();
            schema = root.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        }
        var propertyNames = schema.GetProperty("properties").EnumerateObject()
            .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "description", "subject" }, propertyNames);
    }

    private static string CreateToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "support-user-1"),
            new Claim("role", role)
        };
        var token = new JwtSecurityToken(
            Issuer, Audience, claims,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(5),
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class SupportApiFactory : WebApplicationFactory<Program>
    {
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
                    ["AiAssistant:Enabled"] = "false"
                });
            });
        }
    }
}
