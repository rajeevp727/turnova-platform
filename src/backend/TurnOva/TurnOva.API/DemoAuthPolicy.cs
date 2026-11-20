namespace TurnOva.API;

public static class DemoAuthPolicy
{
    public static bool IsEnabled(string? environmentName, bool configured) =>
        configured &&
        !string.IsNullOrWhiteSpace(environmentName) &&
        !string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
}
