using Microsoft.Azure.Cosmos;
using CosmosDatabase = Microsoft.Azure.Cosmos.Database;

namespace TurnOva.Infrastructure.Database;

public sealed class CosmosMigrationDatabase(CosmosDatabase database) : ICosmosMigrationDatabase
{
    public async Task CreateContainerIfNotExistsAsync(
        string containerName,
        string partitionKeyPath,
        CancellationToken cancellationToken,
        int? defaultTimeToLive = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerName))
            throw new ArgumentException("Container name is required.", nameof(containerName));
        if (string.IsNullOrWhiteSpace(partitionKeyPath) || !partitionKeyPath.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("A valid Cosmos partition-key path is required.", nameof(partitionKeyPath));

        var properties = new ContainerProperties(containerName, partitionKeyPath)
        {
            DefaultTimeToLive = defaultTimeToLive
        };
        var response = await database.CreateContainerIfNotExistsAsync(
            properties,
            cancellationToken: cancellationToken);

        if (defaultTimeToLive.HasValue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await response.Container.ReadContainerAsync(cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Resource.DefaultTimeToLive != defaultTimeToLive)
            {
                current.Resource.DefaultTimeToLive = defaultTimeToLive;
                cancellationToken.ThrowIfCancellationRequested();
                await response.Container.ReplaceContainerAsync(
                    current.Resource,
                    cancellationToken: cancellationToken);
            }
        }
    }
}
