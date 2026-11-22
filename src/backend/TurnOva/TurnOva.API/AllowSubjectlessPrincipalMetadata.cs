namespace TurnOva.API;

/// <summary>
/// Explicitly permits an authenticated subject-less principal for endpoints whose
/// rate-limit partition deliberately falls back to the server-observed client IP.
/// Do not apply this metadata to ownership, Union audit, booking, ride or SignalR routes.
/// </summary>
public sealed record AllowSubjectlessPrincipalMetadata;
