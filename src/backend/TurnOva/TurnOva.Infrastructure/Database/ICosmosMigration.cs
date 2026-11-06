using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database;

public interface ICosmosMigration
{
    string Id { get; }
    string Description { get; }
    Task UpAsync(Microsoft.Azure.Cosmos.Database database, CancellationToken cancellationToken);
}
