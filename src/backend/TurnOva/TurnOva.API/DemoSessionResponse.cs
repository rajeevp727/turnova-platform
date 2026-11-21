namespace TurnOva.API;

public sealed record DemoSessionUserResponse(
    string Id,
    string Email,
    string Name,
    string Role,
    bool IsDemo);

public sealed record DemoSessionResponse(
    string AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    bool IsDemo,
    DemoSessionUserResponse User);
