using Microsoft.AspNetCore.Builder;

namespace TurnOva.API;

public static class ApiResponseCachePolicyExtensions
{
    public static IApplicationBuilder UseApiResponseCachePolicy(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1") ||
                context.Request.Path.StartsWithSegments("/hubs/rides"))
            {
                // Register at response start so exception-handler Response.Clear() cannot
                // remove the non-cacheable policy from an API/realtime error response.
                context.Response.OnStarting(() =>
                {
                    // Preserve endpoint-specific directives such as "private"; add the
                    // non-cacheable directives without replacing an existing policy.
                    var cacheControl = context.Response.Headers["Cache-Control"].ToString();
                    if (!cacheControl.Split(',').Any(directive =>
                            directive.Trim().Equals("no-store", StringComparison.OrdinalIgnoreCase)))
                    {
                        context.Response.Headers["Cache-Control"] = string.IsNullOrWhiteSpace(cacheControl)
                            ? "no-store"
                            : $"{cacheControl}, no-store";
                    }

                    var pragma = context.Response.Headers["Pragma"].ToString();
                    if (!pragma.Split(',').Any(directive =>
                            directive.Trim().Equals("no-cache", StringComparison.OrdinalIgnoreCase)))
                    {
                        context.Response.Headers["Pragma"] = string.IsNullOrWhiteSpace(pragma)
                            ? "no-cache"
                            : $"{pragma}, no-cache";
                    }

                    return Task.CompletedTask;
                });
            }

            await next();
        });

        return app;
    }
}
