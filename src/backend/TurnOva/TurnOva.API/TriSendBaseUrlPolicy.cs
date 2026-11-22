using System;

namespace TurnOva.API;

public static class TriSendBaseUrlPolicy
{
    public static bool IsAllowed(string? baseUrl, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        return string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) &&
            (uri.Scheme == Uri.UriSchemeHttps ||
             (isDevelopment && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
    }
}
