namespace TurnOva.Infrastructure.Database;

/// <summary>Fail-closed checks for Cosmos conditional-write version tokens.</summary>
public static class CosmosEtagPolicy
{
    public static bool HasObservedEtag(string? etag) => !string.IsNullOrWhiteSpace(etag);
}
