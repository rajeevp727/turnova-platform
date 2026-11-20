using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TurnOva.Application.Assistance;
using TurnOva.API;

namespace TurnOva.API.Assistance;

public static class AiAssistanceEndpoints
{
    private const string RateLimitPolicy = "ai-assistance";

    public static IServiceCollection AddAiAssistance(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AiAssistantOptions>, AiAssistantOptionsValidator>();
        services.AddOptions<AiAssistantOptions>()
            .Bind(configuration.GetSection("AiAssistant"))
            .ValidateOnStart();

        services.AddSingleton<IAiAssistantProvider, UnavailableAiAssistantProvider>();
        services.AddSingleton(sp => new AiAssistanceService(
            sp.GetRequiredService<IAiAssistantProvider>(),
            sp.GetRequiredService<IOptions<AiAssistantOptions>>().Value));

        var requestsPerMinute = configuration.GetValue<int?>("AiAssistant:MaxRequestsPerMinute") ?? 10;
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { title = "Too many requests. Please retry later.", status = StatusCodes.Status429TooManyRequests },
                    cancellationToken);
            };
            options.AddPolicy(RateLimitPolicy, context =>
            {
                var partitionKey =
                    context.User.FindFirstValue("sub") ??
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                    context.Connection.RemoteIpAddress?.ToString() ??
                    "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = requestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
            options.AddPolicy(DemoAuthPolicy.RateLimitPolicyName, context =>
            {
                // Demo issuance is anonymous: partition only by the server-observed remote IP.
                // Do not trust an arbitrary client-supplied forwarding header here.
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = DemoAuthPolicy.RequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        return services;
    }

    public static WebApplication MapAiAssistanceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assistance")
            .RequireRateLimiting(RateLimitPolicy);

        group.MapPost("/trip-intent", async Task<IResult> (
            TripIntentRequest request,
            AiAssistanceService assistant,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await assistant.DraftTripIntentAsync(request.Text, cancellationToken);
                loggerFactory.CreateLogger("TurnOva.AiAssistance").LogInformation(
                    "Assistant request completed. UseCase {UseCase} Source {Source} NeedsClarification {NeedsClarification}",
                    "TripIntent", result.Source, result.NeedsClarification);
                return Results.Ok(result);
            }
            catch (ArgumentException exception)
            {
                loggerFactory.CreateLogger("TurnOva.AiAssistance").LogWarning(
                    "Assistant request rejected. UseCase {UseCase} ValidationType {ValidationType}",
                    "TripIntent", exception.GetType().Name);
                return Results.BadRequest(new { message = exception.Message });
            }
        }).RequireAuthorization(policy => policy.RequireRole("Rider"))
            .WithMetadata(new RequestSizeLimitAttribute(16 * 1024))
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        group.MapPost("/help", async Task<IResult> (
            HelpRequest request,
            AiAssistanceService assistant,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await assistant.ExplainHelpAsync(request.Question, request.RideState, cancellationToken);
                loggerFactory.CreateLogger("TurnOva.AiAssistance").LogInformation(
                    "Assistant request completed. UseCase {UseCase} Source {Source} EscalationRequired {EscalationRequired}",
                    "Help", result.Source, result.EscalationRequired);
                return Results.Ok(result);
            }
            catch (ArgumentException exception)
            {
                loggerFactory.CreateLogger("TurnOva.AiAssistance").LogWarning(
                    "Assistant request rejected. UseCase {UseCase} ValidationType {ValidationType}",
                    "Help", exception.GetType().Name);
                return Results.BadRequest(new { message = exception.Message });
            }
        }).RequireAuthorization(policy => policy.RequireRole("Rider", "Driver"))
            .WithMetadata(new RequestSizeLimitAttribute(16 * 1024))
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        return app;
    }
}
