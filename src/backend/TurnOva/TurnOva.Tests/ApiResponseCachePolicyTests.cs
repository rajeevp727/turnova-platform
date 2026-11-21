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
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.Private);
        Assert.Equal(TimeSpan.FromSeconds(60), cacheControl.MaxAge);
        Assert.False(cacheControl.NoStore);
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
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.Private);
        Assert.Equal(TimeSpan.FromSeconds(60), cacheControl.MaxAge);
        Assert.True(cacheControl.NoStore);
        Assert.Contains(response.Headers.Pragma, value =>
            value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/api/v10/test", false)]
    [InlineData("/api/v1evil", false)]
    [InlineData("/hubs/rideshare/negotiate", false)]
    [InlineData("/API/V1/test", true)]
    [InlineData("/HUBS/RIDES/negotiate", true)]
    public async Task CachePolicyMatchesWholePathSegmentsCaseInsensitively(string path, bool expectedNoStore)
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.Private);
        Assert.Equal(TimeSpan.FromSeconds(60), cacheControl.MaxAge);
        Assert.Equal(expectedNoStore, cacheControl.NoStore);

        if (expectedNoStore)
        {
            Assert.Contains(response.Headers.Pragma, value =>
                value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.Empty(response.Headers.Pragma);
        }
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
