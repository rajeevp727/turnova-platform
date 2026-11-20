namespace TurnOva.API;

public static class OpenApiEndpointPolicy
{
    public static bool IsEnabled(string? environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
}
