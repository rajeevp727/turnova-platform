namespace TurnOva.API;

public static class CosmosPersistenceEnvironmentPolicy
{
    public static void Validate(string? environmentName, string? endpoint, string? key)
    {
        var useCosmos = !string.IsNullOrWhiteSpace(endpoint) || !string.IsNullOrWhiteSpace(key);

        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) && !useCosmos)
            throw new InvalidOperationException("Production requires CosmosDb:Endpoint and CosmosDb:Key. In-memory persistence is not allowed in Production.");

        if (useCosmos && (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key)))
            throw new InvalidOperationException("Both CosmosDb:Endpoint and CosmosDb:Key must be configured.");
    }
}
