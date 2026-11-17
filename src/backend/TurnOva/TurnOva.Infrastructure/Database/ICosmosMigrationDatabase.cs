namespace TurnOva.Infrastructure.Database;

public interface ICosmosMigrationDatabase
{
    Task CreateContainerIfNotExistsAsync(
        string containerName,
        string partitionKeyPath,
        CancellationToken cancellationToken);
}
