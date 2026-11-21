using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        builder.UseApiResponseCachePolicy();
        builder.Run(_ => Task.CompletedTask);
        var app = builder.Build();

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Response.Headers["Cache-Control"] = "private, max-age=60";

        await app(context);
        await context.Response.StartAsync();

        Assert.Equal("private, max-age=60", context.Response.Headers["Cache-Control"].ToString());
        Assert.Empty(context.Response.Headers["Pragma"].ToArray());
    }

    [Theory]
    [InlineData("/api/v1/test")]
    [InlineData("/hubs/rides/negotiate")]
    public async Task ApiAndRideHubPathsReceiveNoStoreAndNoCache(string path)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        builder.UseApiResponseCachePolicy();
        builder.Run(_ => Task.CompletedTask);
        var app = builder.Build();

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Response.Headers.CacheControl = "private, max-age=60";

        await app(context);
        await context.Response.StartAsync();

        Assert.Contains("private, max-age=60", context.Response.Headers.CacheControl.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", context.Response.Headers.CacheControl.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(context.Response.Headers["Pragma"].ToString(), "no-cache", StringComparison.OrdinalIgnoreCase);
    }
}
