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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurnOva.API.Support;
using TurnOva.Application.Support;
using TurnOva.Domain.Support;

namespace TurnOva.Tests;

public sealed class SupportRequestKestrelIntegrationTests
{
    [Fact]
    public async Task KestrelRejectsOversizedSupportRequestBeforeStoreAccess()
    {
        var store = new CountingSupportRequestStore();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(SupportRequestKestrelIntegrationTests).Assembly.GetName().Name
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SupportRequests:PolicyApproved"] = "false"
        });
        builder.Services.AddAuthentication("KestrelTest")
            .AddScheme<AuthenticationSchemeOptions, KestrelSupportAuthenticationHandler>(
                "KestrelTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<ISupportRequestStore>(store);

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSupportRequestEndpoints(requireRetention: false);

        await app.StartAsync();
        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            Assert.NotNull(addresses);
            var address = Assert.Single(addresses!);

            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var json = JsonSerializer.Serialize(new
            {
                subject = "Oversized support request",
                description = new string('x', 20 * 1024)
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/support/requests");
            request.Headers.Add("Idempotency-Key", "kestrel-body-limit-01");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            Assert.True(request.Content.Headers.ContentLength > 16 * 1024);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);

            Assert.Equal(413, (int)response.StatusCode);
            Assert.Equal(0, store.WriteCount);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed class KestrelSupportAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim("sub", "kestrel-support-user"),
                new Claim(ClaimTypes.NameIdentifier, "kestrel-support-user"),
                new Claim("role", "Rider"),
                new Claim(ClaimTypes.Role, "Rider")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class CountingSupportRequestStore : ISupportRequestStore
    {
        private int _writeCount;

        public int WriteCount => Volatile.Read(ref _writeCount);

        public Task<SupportRequestWriteResult> CreateOrGetAsync(
            SupportRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _writeCount);
            return Task.FromResult(new SupportRequestWriteResult(
                SupportRequestWriteStatus.Created,
                request));
        }
    }
}
