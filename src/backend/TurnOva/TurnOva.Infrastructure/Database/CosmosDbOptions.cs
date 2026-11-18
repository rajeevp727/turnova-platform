namespace TurnOva.Infrastructure.Database;

public sealed class CosmosDbOptions
{
    public const string SectionName = "CosmosDb";
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "TurnOvaDb";

    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("CosmosDb:Endpoint must be a valid HTTPS URI.");

        if (string.IsNullOrWhiteSpace(Key))
            throw new InvalidOperationException("CosmosDb:Key must be configured.");

        if (string.IsNullOrWhiteSpace(DatabaseName) ||
            DatabaseName.Length > 255 ||
            DatabaseName != DatabaseName.Trim() ||
            DatabaseName.IndexOfAny(['/', '\\', '?', '#']) >= 0)
            throw new InvalidOperationException("CosmosDb:DatabaseName must be a valid Cosmos DB database ID (1-255 characters, no /, \\, ?, or #).");
    }
}
