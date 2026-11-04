namespace TurnOva.Infrastructure.Database;

public sealed class CosmosDbOptions
{
    public const string SectionName = "CosmosDb";
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "TurnOvaDb";
}
