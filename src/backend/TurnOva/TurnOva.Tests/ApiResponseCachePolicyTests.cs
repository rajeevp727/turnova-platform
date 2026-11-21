using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using TurnOva.API;

namespace TurnOva.Tests;

public sealed class ApiResponseCachePolicyTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/openapi/v1.json")]
    public async Task NonApiPathsDoNotReceiveApiCacheDirectives(string path)
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("private, max-age=60", response.Headers.CacheControl?.ToString());
        Assert.Empty(response.Headers.Pragma);
    }

    [Theory]
    [InlineData("/api/v1/test")]
    [InlineData("/hubs/rides/negotiate")]
    public async Task ApiAndRideHubPathsReceiveNoStoreAndNoCache(string path)
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Contains("private, max-age=60", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(response.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
    }

    private static Task<IHost> CreateHostAsync() =>
        new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.Configure(app =>
                {
                    app.UseApiResponseCachePolicy();
                    app.Run(async context =>
                    {
                        context.Response.Headers["Cache-Control"] = "private, max-age=60";
                        await context.Response.WriteAsync("ok");
                    });
                });
            })
            .StartAsync();
}
