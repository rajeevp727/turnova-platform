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

        // Container creation is a committed write. Validate its response without
        // retroactively turning a successful create into cancellation.
        var createdProperties = CosmosReadResponseGuard.ReadResponseResource(
            response,
            static containerResponse => containerResponse.Resource,
            value => string.Equals(value.Id, containerName, StringComparison.Ordinal) &&
                string.Equals(value.PartitionKeyPath, partitionKeyPath, StringComparison.Ordinal),
            "container",
            CancellationToken.None,
            "Cosmos returned a container resource that does not match the requested name and partition-key path after container creation.",
            "Cosmos returned an empty container resource after container creation.");

        if (defaultTimeToLive.HasValue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var container = CosmosReadResponseGuard.ReadResponseResource(
                response,
                static containerResponse => containerResponse.Container,
                value => string.Equals(value.Id, containerName, StringComparison.Ordinal),
                "container handle",
                cancellationToken,
                "Cosmos returned a container handle that does not match the requested name for TTL configuration.",
                "Cosmos returned an empty container handle for TTL configuration.");
            var current = await CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync(
                () => container.ReadContainerAsync(cancellationToken: cancellationToken),
                cancellationToken);
            var currentProperties = CosmosReadResponseGuard.ReadResponseResource(
                current,
                static containerResponse => containerResponse.Resource,
                value => string.Equals(value.Id, containerName, StringComparison.Ordinal) &&
                    string.Equals(value.PartitionKeyPath, partitionKeyPath, StringComparison.Ordinal),
                "container",
                cancellationToken,
                "Cosmos returned a container resource that does not match the requested name and partition-key path for TTL configuration.",
                "Cosmos returned an empty container resource for TTL configuration.");

            if (currentProperties.DefaultTimeToLive != defaultTimeToLive)
            {
                currentProperties.DefaultTimeToLive = defaultTimeToLive;
                cancellationToken.ThrowIfCancellationRequested();
                await container.ReplaceContainerAsync(
                    currentProperties,
                    cancellationToken: cancellationToken);
            }
        }
    }
}
