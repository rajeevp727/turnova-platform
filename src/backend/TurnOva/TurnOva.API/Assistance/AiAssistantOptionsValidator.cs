using Microsoft.Extensions.Options;
using TurnOva.Application.Assistance;

namespace TurnOva.API.Assistance;

public sealed class AiAssistantOptionsValidator : IValidateOptions<AiAssistantOptions>
{
    public ValidateOptionsResult Validate(string? name, AiAssistantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.RequestTimeoutSeconds is < 1 or > 10)
            failures.Add("AiAssistant:RequestTimeoutSeconds must be between 1 and 10.");

        if (options.MaxInputCharacters is < 64 or > 2000)
            failures.Add("AiAssistant:MaxInputCharacters must be between 64 and 2000.");

        if (options.MaxOutputCharacters is < 100 or > 1000)
            failures.Add("AiAssistant:MaxOutputCharacters must be between 100 and 1000.");

        if (options.MaxRequestsPerMinute is < 1 or > 60)
            failures.Add("AiAssistant:MaxRequestsPerMinute must be between 1 and 60.");

        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Provider))
                failures.Add("AiAssistant:Provider is required when assistance is enabled.");

            if (string.IsNullOrWhiteSpace(options.Model))
                failures.Add("AiAssistant:Model is required when assistance is enabled.");

            if (!IsValidHttpsEndpoint(options.Endpoint))
                failures.Add("AiAssistant:Endpoint must be an absolute HTTPS URL without userinfo, query, fragment, raw whitespace or control characters.");

            if (string.IsNullOrWhiteSpace(options.CredentialReference))
                failures.Add("AiAssistant:CredentialReference must identify a managed credential when assistance is enabled.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidHttpsEndpoint(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Inspect the original text before System.Uri can normalize whitespace,
        // empty query/fragment delimiters or an empty userinfo delimiter.
        if (value.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character) ||
                character == '\\'))
            return false;

        if (value.Contains('?') || value.Contains('#'))
            return false;

        var schemeSeparator = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator <= 0 ||
            !string.Equals(value[..schemeSeparator], Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        var authorityStart = schemeSeparator + 3;
        var authorityEnd = value.IndexOf('/', authorityStart);
        if (authorityEnd < 0)
            authorityEnd = value.Length;

        if (authorityEnd == authorityStart)
            return false;

        // Reject any raw authority '@', including https://@host where Uri.UserInfo
        // becomes an empty string after parsing.
        var authority = value[authorityStart..authorityEnd];
        if (authority.Contains('@'))
            return false;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
            return false;

        return string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(endpoint.Host) &&
            string.IsNullOrEmpty(endpoint.UserInfo) &&
            string.IsNullOrEmpty(endpoint.Query) &&
            string.IsNullOrEmpty(endpoint.Fragment) &&
            endpoint.IsWellFormedOriginalString();
    }
}
