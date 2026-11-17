using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurnOva.API.Assistance;
using TurnOva.Application.Assistance;

namespace TurnOva.Tests;

public sealed class AiAssistanceKestrelIntegrationTests
{
    [Theory]
    [InlineData("/api/v1/assistance/trip-intent", "text")]
    [InlineData("/api/v1/assistance/help", "question")]
    public async Task KestrelRejectsOversizedAssistanceRequestBeforeProviderInvocation(
        string path,
        string propertyName)
    {
        var provider = new CountingAiAssistantProvider();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(AiAssistanceKestrelIntegrationTests).Assembly.GetName().Name
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiAssistant:Enabled"] = "true",
            ["AiAssistant:Provider"] = "test-provider-placeholder",
            ["AiAssistant:Model"] = "test-model-placeholder",
            ["AiAssistant:Endpoint"] = "https://provider.example.test",
            ["AiAssistant:CredentialReference"] = "test-managed-secret-reference",
            ["AiAssistant:RequestTimeoutSeconds"] = "4",
            ["AiAssistant:MaxInputCharacters"] = "2000",
            ["AiAssistant:MaxOutputCharacters"] = "600",
            ["AiAssistant:MaxRequestsPerMinute"] = "10"
        });

        builder.Services.AddAuthentication("KestrelTest")
            .AddScheme<AuthenticationSchemeOptions, KestrelAiAuthenticationHandler>(
                "KestrelTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddAiAssistance(builder.Configuration);
        builder.Services.RemoveAll<IAiAssistantProvider>();
        builder.Services.AddSingleton<IAiAssistantProvider>(provider);

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapAiAssistanceEndpoints();

        await app.StartAsync();
        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            Assert.NotNull(addresses);
            var address = Assert.Single(addresses!);

            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [propertyName] = new string('x', 20 * 1024)
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            Assert.True(request.Content.Headers.ContentLength > 16 * 1024);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);

            Assert.Equal((HttpStatusCode)413, response.StatusCode);
            Assert.Equal(0, provider.InvocationCount);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed class KestrelAiAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim("sub", "kestrel-ai-rider"),
                new Claim(ClaimTypes.NameIdentifier, "kestrel-ai-rider"),
                new Claim("role", "Rider"),
                new Claim(ClaimTypes.Role, "Rider")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class CountingAiAssistantProvider : IAiAssistantProvider
    {
        private int _invocationCount;

        public bool IsAvailable => true;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public Task<TripIntentProviderResult?> DraftTripIntentAsync(
            SafeTripIntentPrompt request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            return Task.FromResult<TripIntentProviderResult?>(
                new TripIntentProviderResult("[PICKUP]", "[DROPOFF]", "Cab"));
        }

        public Task<HelpProviderResult?> ClassifyHelpAsync(
            SafeHelpPrompt request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            return Task.FromResult<HelpProviderResult?>(new HelpProviderResult("fare-negotiation"));
        }
    }
}
