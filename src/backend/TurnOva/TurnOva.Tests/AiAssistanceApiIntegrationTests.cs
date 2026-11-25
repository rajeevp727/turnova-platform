using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging;
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
        using var anonymous = factory.CreateAnonymousClient();

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
        using var anonymous = factory.CreateAnonymousClient();

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

    [Theory]
    [InlineData("UnionManager")]
    [InlineData("Ops")]
    public async Task OpsAndUnionManagerCannotUseRiderOrDriverAssistanceEndpoints(string role)
    {
        using var factory = new AiAssistanceApiFactory();
        using var nonRiderDriver = factory.CreateClient(role, $"ai-{role.ToLowerInvariant()}");

        using var tripIntent = await nonRiderDriver.PostAsJsonAsync(
            "/api/v1/assistance/trip-intent",
            new { text = "Cab from KPHB to HITEC City for 2 passengers" });
        using var help = await nonRiderDriver.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?" });

        Assert.Equal(HttpStatusCode.Forbidden, tripIntent.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, help.StatusCode);
        Assert.True(tripIntent.Headers.CacheControl?.NoStore);
        Assert.True(help.Headers.CacheControl?.NoStore);
        Assert.Contains(tripIntent.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(help.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AssistanceAuthorizationRejectionsAreNonCacheable()
    {
        using var factory = new AiAssistanceApiFactory();
        using var driver = factory.CreateClient("Driver", "ai-forbidden-trip-intent");
        using var anonymous = factory.CreateAnonymousClient();

        using var forbiddenResponse = await driver.PostAsJsonAsync(
            "/api/v1/assistance/trip-intent",
            new { text = "Cab from Nizampet to HITEC City for 2 passengers" });

        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        Assert.True(forbiddenResponse.Headers.CacheControl?.NoStore);
        Assert.Contains(forbiddenResponse.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));

        using var unauthorizedResponse = await anonymous.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?" });

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        Assert.True(unauthorizedResponse.Headers.CacheControl?.NoStore);
        Assert.Contains(unauthorizedResponse.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AssistanceValidationReturnsTypedNonCacheableBadRequest()
    {
        using var factory = new AiAssistanceApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-validation");

        using var tripResponse = await rider.PostAsJsonAsync(
            "/api/v1/assistance/trip-intent",
            new { text = " " });

        Assert.Equal(HttpStatusCode.BadRequest, tripResponse.StatusCode);
        Assert.True(tripResponse.Headers.CacheControl?.NoStore);
        Assert.Contains(tripResponse.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        using var tripError = JsonDocument.Parse(await tripResponse.Content.ReadAsStringAsync());
        var tripMessage = tripError.RootElement.GetProperty("message").GetString()!;
        Assert.StartsWith("A non-empty request is required.", tripMessage, StringComparison.Ordinal);

        using var helpResponse = await rider.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?", rideState = new string('x', 41) });

        Assert.Equal(HttpStatusCode.BadRequest, helpResponse.StatusCode);
        Assert.True(helpResponse.Headers.CacheControl?.NoStore);
        Assert.Contains(helpResponse.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        using var helpError = JsonDocument.Parse(await helpResponse.Content.ReadAsStringAsync());
        var helpMessage = helpError.RootElement.GetProperty("message").GetString()!;
        Assert.StartsWith("Ride state is invalid.", helpMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 41), helpMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApiDocumentsTypedAiAssistanceContractsAndResponses()
    {
        using var factory = new AiAssistanceApiFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        var tripIntent = paths.GetProperty("/api/v1/assistance/trip-intent").GetProperty("post");
        var help = paths.GetProperty("/api/v1/assistance/help").GetProperty("post");

        AssertBearerRequired(tripIntent);
        AssertBearerRequired(help);

        var tripRequest = ResolveSchema(
            root,
            tripIntent.GetProperty("requestBody").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"));
        Assert.True(tripRequest.GetProperty("properties").TryGetProperty("text", out _));

        var tripResponses = tripIntent.GetProperty("responses");
        var tripDraft = ResolveSchema(
            root,
            tripResponses.GetProperty("200").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"));
        foreach (var property in new[]
        {
            "pickup", "dropoff", "serviceClass", "passengerCount",
            "needsClarification", "missingFields", "requiresUserConfirmation", "source"
        })
            Assert.True(tripDraft.GetProperty("properties").TryGetProperty(property, out _),
                $"Trip-intent response is missing {property}.");
        AssertErrorResponses(root, tripResponses);

        var helpRequest = ResolveSchema(
            root,
            help.GetProperty("requestBody").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"));
        var helpRequestProperties = helpRequest.GetProperty("properties");
        Assert.True(helpRequestProperties.TryGetProperty("question", out _));
        Assert.True(helpRequestProperties.TryGetProperty("rideState", out _));

        var helpResponses = help.GetProperty("responses");
        var helpResponse = ResolveSchema(
            root,
            helpResponses.GetProperty("200").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"));
        foreach (var property in new[] { "answer", "sourceId", "escalationRequired", "source" })
            Assert.True(helpResponse.GetProperty("properties").TryGetProperty(property, out _),
                $"Help response is missing {property}.");
        AssertErrorResponses(root, helpResponses);
    }

    private static void AssertBearerRequired(JsonElement operation)
    {
        Assert.Contains(operation.GetProperty("security").EnumerateArray(),
            requirement => requirement.TryGetProperty("Bearer", out _));
    }

    private static void AssertErrorResponses(JsonElement root, JsonElement responses)
    {
        var validationError = ResolveSchema(
            root,
            responses.GetProperty("400").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"));
        Assert.True(validationError.GetProperty("properties").TryGetProperty("message", out _));

        var tooManyRequests = responses.GetProperty("429");
        Assert.True(tooManyRequests.GetProperty("content").TryGetProperty("application/problem+json", out _));

        foreach (var status in new[] { "401", "403", "413" })
        {
            Assert.True(responses.TryGetProperty(status, out var response),
                $"Assistance contract is missing HTTP {status}.");
            Assert.False(response.TryGetProperty("content", out _),
                $"HTTP {status} is documented without a response body.");
        }
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var schemaReference))
            return schema;

        var schemaName = schemaReference.GetString()!.Split('/').Last();
        return root.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
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

    [Fact]
    public async Task SubjectlessPrincipalUsesIpRateLimitOnlyOnAssistanceEndpoints()
    {
        using var factory = new AiAssistanceApiFactory();
        using var subjectless = factory.CreateClientWithoutSubject("Rider");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var allowed = await subjectless.PostAsJsonAsync(
                "/api/v1/assistance/help",
                new { question = "What does queue status mean?" });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await subjectless.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.CacheControl?.NoStore);

        using var rejectedIdentityRead = await subjectless.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, rejectedIdentityRead.StatusCode);
        Assert.True(rejectedIdentityRead.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task AssistantRateLimitIsSharedAcrossTripIntentAndHelpEndpoints()
    {
        using var factory = new AiAssistanceApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-rate-limit-shared-endpoints");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var allowed = await rider.PostAsJsonAsync(
                "/api/v1/assistance/help",
                new { question = "What does queue status mean?" });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await rider.PostAsJsonAsync(
            "/api/v1/assistance/trip-intent",
            new { text = "Cab from Nizampet to HITEC City for 2 passengers" });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.CacheControl?.NoStore);
        Assert.Contains(rejected.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AssistantRateLimitUsesNameIdentifierWhenSubjectClaimIsBlank()
    {
        using var factory = new AiAssistanceApiFactory();
        using var firstCaller = factory.CreateClientWithBlankSubject("Rider", "ai-rate-limit-blank-first");
        using var secondCaller = factory.CreateClientWithBlankSubject("Rider", "ai-rate-limit-blank-second");

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

    [Fact]
    public async Task AssistantRateLimitDoesNotTrustForwardedHeadersWhenIdentityClaimsAreBlank()
    {
        using var factory = new AiAssistanceApiFactory();
        using var caller = factory.CreateClientWithBlankSubject("Rider", string.Empty);

        caller.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.10");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var allowed = await caller.PostAsJsonAsync(
                "/api/v1/assistance/help",
                new { question = "What does queue status mean?" });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await caller.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        caller.DefaultRequestHeaders.Remove("X-Forwarded-For");
        caller.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.11");

        using var spoofedPartitionAttempt = await caller.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "What does queue status mean?" });
        Assert.Equal(HttpStatusCode.TooManyRequests, spoofedPartitionAttempt.StatusCode);
    }

    [Fact]
    public async Task AssistantRateLimitRejectionIsNonCacheable()
    {
        using var factory = new AiAssistanceApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-rate-limit-cache");

        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var response = await rider.PostAsJsonAsync(
                "/api/v1/assistance/help",
                new { question = "What does queue status mean?" });

            if (attempt < 10)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                continue;
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains(response.Headers.Pragma, value =>
                value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
            Assert.DoesNotContain("ai-rate-limit-cache", body.RootElement.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AssistantRequestLogsNeverContainUserPromptOrSensitiveValues()
    {
        using var loggerProvider = new CapturingLoggerProvider();
        using var factory = new AiAssistanceApiFactory(loggerProvider);
        using var rider = factory.CreateClient("Rider", "ai-log-privacy");

        using var tripResponse = await rider.PostAsJsonAsync(
            "/api/v1/assistance/trip-intent",
            new { text = "Cab from 12 Private Road to Secret Clinic for 2 passengers" });
        Assert.Equal(HttpStatusCode.OK, tripResponse.StatusCode);

        using var helpResponse = await rider.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new { question = "How is payment received? Contact raja@example.test or +91 9876543210 from 12 Private Road." });
        Assert.Equal(HttpStatusCode.OK, helpResponse.StatusCode);

        var messages = string.Join("\n", loggerProvider.Messages);
        Assert.Contains("Assistant request completed", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("12 Private Road", messages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret Clinic", messages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raja@example.test", messages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9876543210", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("payment", messages, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task TripIntentRejectsUnmappedRideLocationAndFareFields()
    {
        using var factory = new AiAssistanceApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-strict-trip-request");

        using var response = await rider.PostAsJsonAsync(
            "/api/v1/assistance/trip-intent",
            new
            {
                text = "Cab from Nizampet to HITEC City",
                rideId = "must-not-be-accepted",
                pickupLatitude = 17.5,
                fareAmount = 500
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HelpRejectsUnmappedLocationContactAndPaymentFields()
    {
        using var factory = new AiAssistanceApiFactory();
        using var rider = factory.CreateClient("Rider", "ai-strict-help-request");

        using var response = await rider.PostAsJsonAsync(
            "/api/v1/assistance/help",
            new
            {
                question = "What does queue status mean?",
                exactLocation = "17.5,78.4",
                customerEmail = "rider@example.test",
                paymentToken = "must-not-be-accepted"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            string categoryName,
            ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) =>
                string.Equals(categoryName, "TurnOva.AiAssistance", StringComparison.Ordinal);

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    messages.Enqueue(formatter(state, exception));
            }
        }
    }

    private sealed class AiAssistanceApiFactory : WebApplicationFactory<Program>
    {
        private readonly ILoggerProvider? _loggerProvider;

        public AiAssistanceApiFactory(ILoggerProvider? loggerProvider = null) =>
            _loggerProvider = loggerProvider;

        private const string Issuer = "turnova-ai-assistance-tests";
        private const string Audience = "turnova-api";
        private const string SigningKey = "turnova-ai-assistance-test-signing-key-32-bytes";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            if (_loggerProvider is not null)
                builder.ConfigureLogging(logging => logging.AddProvider(_loggerProvider));

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
            var client = base.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(role, subject));
            return client;
        }

        public HttpClient CreateClientWithBlankSubject(string role, string nameIdentifier)
        {
            var client = base.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateTokenWithBlankSubject(role, nameIdentifier));
            return client;
        }

        public HttpClient CreateClientWithoutSubject(string role)
        {
            var client = base.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateTokenWithoutSubject(role));
            return client;
        }

        public HttpClient CreateAnonymousClient() =>
            base.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });

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

        private static string CreateTokenWithoutSubject(string role)
        {
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim("role", role),
                new Claim("name", "subjectless-assistance-test")
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

        private static string CreateTokenWithBlankSubject(string role, string nameIdentifier)
        {
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256);
            var payload = new JwtPayload
            {
                ["iss"] = Issuer,
                ["aud"] = Audience,
                ["sub"] = string.Empty,
                ["role"] = role,
                ["name"] = nameIdentifier,
                [ClaimTypes.NameIdentifier] = nameIdentifier,
                ["nbf"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
                ["exp"] = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()
            };
            var token = new JwtSecurityToken(new JwtHeader(credentials), payload);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
