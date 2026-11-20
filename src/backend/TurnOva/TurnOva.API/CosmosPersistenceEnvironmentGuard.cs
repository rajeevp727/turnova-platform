namespace TurnOva.API;

public static class CosmosPersistenceEnvironmentGuard
{
    public static void Validate(string environmentName, bool useCosmos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        if (useCosmos || IsDevelopmentOrTesting(environmentName))
            return;

        throw new InvalidOperationException(
            "Non-development/test environments require CosmosDb:Endpoint and CosmosDb:Key. In-memory persistence is development/test only.");
    }

    public static bool IsDevelopmentOrTesting(string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
    }
}
