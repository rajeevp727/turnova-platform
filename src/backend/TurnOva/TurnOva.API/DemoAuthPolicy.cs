namespace TurnOva.API;

public static class DemoAuthPolicy
{
    public static bool IsEnabled(string? environmentName, bool configured) =>
        configured &&
        !string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
}
