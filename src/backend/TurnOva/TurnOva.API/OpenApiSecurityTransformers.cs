using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TurnOva.API;

public sealed class TurnOvaOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "TurnOva API",
            Version = "v1",
            Description = "Cab-only Rider and Driver ride discovery and coordination API. Riders select Drivers; fares are negotiated directly and payments are not collected by TurnOva."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "TriSend-issued TurnOva access token."
            },
            ["OpsAdminKey"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Driver-Admin-Key",
                Description = "Operations-only server key. Never place this value in a Rider or Driver app."
            }
        };

        return Task.CompletedTask;
    }
}

public sealed class TurnOvaOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<AllowAnonymousAttribute>().Any())
            return Task.CompletedTask;

        var path = (context.Description.RelativePath ?? string.Empty).TrimStart('/');
        string? scheme = null;

        if (metadata.OfType<IAuthorizeData>().Any())
            scheme = "Bearer";
        else if (path.StartsWith("api/v1/ops/", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(path, "api/v1/ops", StringComparison.OrdinalIgnoreCase))
            scheme = "OpsAdminKey";

        if (scheme is not null)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(scheme, context.Document)] = []
            });
        }

        return Task.CompletedTask;
    }
}
