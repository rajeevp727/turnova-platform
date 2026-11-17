using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using TurnOva.Application.Support;
using TurnOva.Domain.Support;

namespace TurnOva.API.Support;

public static class SupportRequestEndpoints
{
    public static WebApplication MapSupportRequestEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/support")
            .WithTags("Support")
            .RequireAuthorization(policy => policy.RequireRole("Rider", "Driver"));

        group.MapPost("/requests", async Task<IResult> (
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            SupportRequestSubmission? request,
            ClaimsPrincipal user,
            ISupportRequestStore store,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
                return Results.BadRequest(new { message = "A support request body is required." });

            var subject = request.Subject?.Trim();
            var description = request.Description?.Trim();
            if (subject is null || subject.Length is < 3 or > 120)
                return Results.BadRequest(new { message = "Subject must contain 3 to 120 characters." });
            if (description is null || description.Length is < 10 or > 4000)
                return Results.BadRequest(new { message = "Description must contain 10 to 4000 characters." });
            if (!IsValidIdempotencyKey(idempotencyKey))
                return Results.BadRequest(new { message = "A valid Idempotency-Key header is required." });

            var reporterId = user.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(reporterId))
                reporterId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var reporterRole = user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role);
            if (string.IsNullOrWhiteSpace(reporterId) || reporterRole is not ("Rider" or "Driver"))
                return Results.Forbid();

            var id = ComputeRequestId(reporterId, idempotencyKey!);
            var supportRequest = new SupportRequest(
                id,
                reporterId,
                reporterRole,
                subject,
                description,
                DateTimeOffset.UtcNow);

            var result = await store.CreateOrGetAsync(supportRequest, cancellationToken);
            var response = new SupportRequestSubmissionResponse(
                result.Request.Id,
                result.Request.SubmittedAtUtc,
                result.Status == SupportRequestWriteStatus.Replayed);

            return result.Status switch
            {
                SupportRequestWriteStatus.Created =>
                    Results.Json(response, statusCode: StatusCodes.Status202Accepted),
                SupportRequestWriteStatus.Replayed => Results.Ok(response),
                _ => Results.Conflict(new { message = "This Idempotency-Key was already used for different support content." })
            };
        })
        .WithSummary("Submit a neutral Rider or Driver support request")
        .WithDescription("Accepts only a subject and description. No category, severity, priority, assignment, escalation, response target, attachment or retention policy is inferred. Do not include passwords, tokens or payment credentials. Support content is not logged.")
        .Produces<SupportRequestSubmissionResponse>(StatusCodes.Status202Accepted)
        .Produces<SupportRequestSubmissionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static bool IsValidIdempotencyKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);

    private static string ComputeRequestId(string reporterId, string idempotencyKey)
    {
        var bytes = Encoding.UTF8.GetBytes($"{reporterId}\n{idempotencyKey}");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SupportRequestSubmission(string? Subject, string? Description);

public sealed record SupportRequestSubmissionResponse(
    string RequestId,
    DateTimeOffset SubmittedAtUtc,
    bool Replayed);
