using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using TurnOva.Application.Support;
using TurnOva.Domain.Support;

namespace TurnOva.API.Support;

public static class SupportRequestEndpoints
{
    public static bool IsEnabledForEnvironment(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        return environment.IsDevelopment() ||
            environment.IsEnvironment("Testing") ||
            configuration.GetValue<bool>("SupportRequests:PolicyApproved");
    }

    public static bool TryGetRequestTtl(
        IHostEnvironment environment,
        IConfiguration configuration,
        bool requireRetention,
        out int? ttlSeconds)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);
        ttlSeconds = GetRetentionTtlSeconds(configuration);
        return IsEnabledForEnvironment(environment, configuration) &&
            (!requireRetention || ttlSeconds.HasValue);
    }

    public static int? GetRetentionTtlSeconds(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configuredDays = configuration["SupportRequests:RetentionDays"];
        if (!int.TryParse(configuredDays, NumberStyles.None, CultureInfo.InvariantCulture, out var retentionDays) ||
            retentionDays <= 0 || retentionDays > int.MaxValue / 86_400)
            return null;
        return checked(retentionDays * 86_400);
    }

    public static WebApplication MapSupportRequestUnavailable(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/support")
            .WithTags("Support")
            .RequireAuthorization(policy => policy.RequireRole("Rider", "Driver"));

        group.MapPost("/requests", () => Results.Problem(
                title: "Support intake is not enabled for this environment.",
                statusCode: StatusCodes.Status503ServiceUnavailable))
            .WithSummary("Support intake is disabled pending policy approval")
            .WithDescription("No request body is accepted or persisted until SupportRequests:PolicyApproved is explicitly enabled, a positive approved SupportRequests:RetentionDays is configured, and durable Cosmos storage is available.")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
    public static WebApplication MapSupportRequestEndpoints(this WebApplication app, bool requireRetention)
    {
        var environment = app.Environment;
        var group = app.MapGroup("/api/v1/support")
            .WithTags("Support")
            .RequireAuthorization(policy => policy.RequireRole("Rider", "Driver"));

        group.MapPost("/requests", async Task<IResult> (
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            SupportRequestSubmission? request,
            ClaimsPrincipal user,
            ISupportRequestStore store,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetRequestTtl(environment, configuration, requireRetention, out var retentionTtlSeconds))
                return Results.Problem(
                    title: "Support intake is unavailable until policy approval and durable retention are configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            if (request is null)
                return Results.BadRequest(new SupportRequestErrorResponse("A support request body is required."));

            var subject = request.Subject?.Trim();
            var description = request.Description?.Trim();
            if (subject is null || subject.Length is < 3 or > 120)
                return Results.BadRequest(new SupportRequestErrorResponse("Subject must contain 3 to 120 characters."));
            if (description is null || description.Length is < 10 or > 4000)
                return Results.BadRequest(new SupportRequestErrorResponse("Description must contain 10 to 4000 characters."));
            if (!IsValidIdempotencyKey(idempotencyKey))
                return Results.BadRequest(new SupportRequestErrorResponse("A valid Idempotency-Key header is required."));

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
                DateTimeOffset.UtcNow,
                retentionTtlSeconds);

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
                _ => Results.Conflict(new SupportRequestErrorResponse("This Idempotency-Key was already used for different support content."))
            };
        })
        .WithMetadata(new RequestSizeLimitAttribute(16 * 1024))
        .WithSummary("Submit a neutral Rider or Driver support request")
        .WithDescription("Accepts only a subject and description. No category, severity, priority, assignment, escalation, response target, attachment or retention policy is inferred. Do not include passwords, tokens or payment credentials. Support content is not logged.")
        .Produces<SupportRequestSubmissionResponse>(StatusCodes.Status202Accepted)
        .Produces<SupportRequestSubmissionResponse>(StatusCodes.Status200OK)
        .Produces<SupportRequestErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces<SupportRequestErrorResponse>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

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

public sealed record SupportRequestErrorResponse(string Message);
