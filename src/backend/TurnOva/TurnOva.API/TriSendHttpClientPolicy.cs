using System.Net.Http;

namespace TurnOva.API;

public static class TriSendHttpClientPolicy
{
    public static SocketsHttpHandler CreatePrimaryHandler() => new()
    {
        // Authentication requests contain passwords and refresh tokens. Never follow an
        // upstream redirect that could forward credentials or opaque tokens to another host.
        AllowAutoRedirect = false,
        // The API forwards bearer tokens explicitly. Do not persist Set-Cookie values in the
        // pooled handler's shared CookieContainer or replay them across Rider/Driver requests.
        UseCookies = false
    };
}
