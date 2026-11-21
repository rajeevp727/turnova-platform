namespace TurnOva.API;

/// <summary>
/// Explicitly marks a read-only GET/HEAD demo endpoint that may accept a restricted demo JWT.
/// The route must also be under /api/v1/demo; the path prefix alone grants no access.
/// Mutation methods remain blocked even when this metadata is present.
/// </summary>
public sealed record DemoEndpointMetadata;
