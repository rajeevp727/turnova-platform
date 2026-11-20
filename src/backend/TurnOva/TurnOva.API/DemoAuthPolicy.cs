namespace TurnOva.API;

public static class DemoAuthPolicy
{
    public static bool IsEnabled(string? environmentName, bool configured) =>
        configured &&
        (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(environmentName, "QA", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(environmentName, "Demo", StringComparison.OrdinalIgnoreCase));
}
