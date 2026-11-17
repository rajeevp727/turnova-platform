using TurnOva.Domain.Support;
using TurnOva.Application.Support;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task SupportRequestUsesConfiguredPerItemTtlSeconds()
    {
        var store = new CapturingSupportRequestStore();
        using var factory = new SupportApiFactory(retentionDays: 7, supportStore: store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Rider"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "support-request-retention-01");

        using var response = await client.PostAsJsonAsync("/api/v1/support/requests",
            new { subject = "Trip update issue", description = "The trip status did not refresh after reconnecting." });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(7 * 86_400, store.LastRequest?.Ttl);
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
        using var conflictBody = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal(
            "This Idempotency-Key was already used for different support content.",
            conflictBody.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("Different issue", conflictBody.RootElement.GetRawText(), StringComparison.Ordinal);
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
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("message", out _));
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
    public async Task StagingSupportIntakeFailsClosedUntilPolicyIsApproved()
    {
        using var factory = new SupportApiFactory("Staging");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Rider"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "support-request-staging-01");

        using var response = await client.PostAsJsonAsync("/api/v1/support/requests",
            new { subject = "Trip update issue", description = "The trip status did not refresh after reconnecting." });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public void StagingSupportIntakeCannotEnableWithoutApprovedRetentionDays()
    {
        using var factory = new SupportApiFactory("Staging", policyApproved: true);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("SupportRequests:RetentionDays", exception.ToString());
    }

    [Fact]
    public void StagingSupportIntakeCannotEnableWithoutCosmosPersistence()
    {
        using var factory = new SupportApiFactory("Staging", policyApproved: true, retentionDays: 7);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("Staging support intake requires CosmosDb:Endpoint and CosmosDb:Key", exception.ToString());
    }

    [Fact]
    public async Task StagingSupportIntakeReturns503BeforeParsingMalformedBody()
    {
        using var factory = new SupportApiFactory("Staging");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Rider"));
        using var content = new StringContent("{not-json", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/v1/support/requests", content);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
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
        var propertyNames = GetSchemaPropertyNames(schema, root);
        Assert.Equal(new[] { "description", "subject" }, propertyNames);

        var badRequestSchema = operation.GetProperty("responses").GetProperty("400")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal(new[] { "message" }, GetSchemaPropertyNames(badRequestSchema, root));

        var conflictSchema = operation.GetProperty("responses").GetProperty("409")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal(new[] { "message" }, GetSchemaPropertyNames(conflictSchema, root));
        Assert.True(operation.GetProperty("responses").TryGetProperty("413", out _));
    }

    [Fact]
    public void SupportRequestEndpointDeclaresSixteenKilobyteBodyLimit()
    {
        using var factory = new SupportApiFactory();
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>();
        var endpoint = Assert.Single(endpoints.Where(candidate =>
            string.Equals(candidate.RoutePattern.RawText, "/api/v1/support/requests", StringComparison.Ordinal)));

        var limit = endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
        Assert.NotNull(limit);
        Assert.Equal(16 * 1024, limit.MaxRequestBodySize);
    }

    private static string[] GetSchemaPropertyNames(JsonElement schema, JsonElement documentRoot)
    {
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        var visitedReferences = new HashSet<string>(StringComparer.Ordinal);

        Visit(schema);
        return propertyNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        void Visit(JsonElement candidate)
        {
            if (candidate.ValueKind != JsonValueKind.Object)
                return;

            if (candidate.TryGetProperty("$ref", out var referenceElement) &&
                referenceElement.ValueKind == JsonValueKind.String)
            {
                var reference = referenceElement.GetString();
                const string prefix = "#/components/schemas/";
                if (reference is not null &&
                    reference.StartsWith(prefix, StringComparison.Ordinal) &&
                    visitedReferences.Add(reference))
                {
                    var schemaName = reference[prefix.Length..]
                        .Replace("~1", "/", StringComparison.Ordinal)
                        .Replace("~0", "~", StringComparison.Ordinal);
                    Visit(documentRoot.GetProperty("components").GetProperty("schemas").GetProperty(schemaName));
                }
            }

            if (candidate.TryGetProperty("properties", out var properties) &&
                properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                    propertyNames.Add(property.Name);
            }

            foreach (var compositionName in new[] { "allOf", "oneOf", "anyOf" })
            {
                if (!candidate.TryGetProperty(compositionName, out var composition) ||
                    composition.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var nestedSchema in composition.EnumerateArray())
                    Visit(nestedSchema);
            }
        }
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

    private sealed class SupportApiFactory(
        string environmentName = "Testing",
        bool policyApproved = false,
        int? retentionDays = null,
        ISupportRequestStore? supportStore = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            builder.ConfigureTestServices(services =>
            {
                if (supportStore is not null)
                {
                    services.RemoveAll<ISupportRequestStore>();
                    services.AddSingleton(supportStore);
                }
            });
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["Auth:Jwt:Issuer"] = Issuer,
                    ["Auth:Jwt:Audience"] = Audience,
                    ["Auth:Jwt:SigningKey"] = SigningKey,
                    ["TriSend:BaseUrl"] = "https://auth.test.invalid",
                    ["DriverOnboarding:AdminApiKey"] = "integration-test-admin-key-32chars",
                    ["AiAssistant:Enabled"] = "false",
                    ["SupportRequests:PolicyApproved"] = policyApproved ? "true" : "false"
                };
                if (retentionDays.HasValue)
                    values["SupportRequests:RetentionDays"] = retentionDays.Value.ToString(CultureInfo.InvariantCulture);
                configuration.AddInMemoryCollection(values);
            });
        }
    }

    private sealed class CapturingSupportRequestStore : ISupportRequestStore
    {
        public SupportRequest? LastRequest { get; private set; }

        public Task<SupportRequestWriteResult> CreateOrGetAsync(
            SupportRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(new SupportRequestWriteResult(SupportRequestWriteStatus.Created, request));
        }
    }
}
