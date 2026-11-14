namespace TurnOva.API;

/// <summary>
/// Validates one configured browser origin before it is added to the API CORS allow-list.
/// </summary>
public static class CorsOriginValidator
{
    private const string InvalidOriginMessage =
        "Cors:AllowedOrigins must contain explicit HTTPS origins (HTTP is allowed only in Development); wildcards and URL paths are not allowed.";

    public static void ValidateOrigin(string? origin, bool allowHttp)
    {
        if (string.IsNullOrWhiteSpace(origin) ||
            origin.Contains('*') ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) ||
            (parsedOrigin.Scheme != Uri.UriSchemeHttps &&
             !(allowHttp && parsedOrigin.Scheme == Uri.UriSchemeHttp)) ||
            parsedOrigin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(parsedOrigin.UserInfo) ||
            !string.IsNullOrEmpty(parsedOrigin.Query) ||
            !string.IsNullOrEmpty(parsedOrigin.Fragment))
        {
            throw new InvalidOperationException(InvalidOriginMessage);
        }
    }
}
