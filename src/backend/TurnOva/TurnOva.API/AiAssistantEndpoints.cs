using Microsoft.AspNetCore.RateLimiting;

namespace TurnOva.API;

public static class AiAssistantEndpoints
{
    public static void MapAiAssistantEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/ai")
            .RequireRateLimiting("ai-assistant");

        group.MapPost("/trip-intent-draft", async (
            TripIntentDraftRequest request,
            IAiAssistantService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.CreateTripIntentDraftAsync(request.Text, cancellationToken));
            }
            catch (AiAssistantInputException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        }).RequireAuthorization(policy => policy.RequireRole("Rider"));

        group.MapPost("/help", async (
            ProductHelpRequest request,
            IAiAssistantService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetProductHelpAsync(request.Question, cancellationToken));
            }
            catch (AiAssistantInputException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        }).RequireAuthorization(policy => policy.RequireRole("Rider", "Driver"));
    }
}
