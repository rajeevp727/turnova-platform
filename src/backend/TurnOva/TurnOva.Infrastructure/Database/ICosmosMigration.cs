using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database;

public interface ICosmosMigration
{
    string Id { get; }
    string Description { get; }
    Task UpAsync(Database database, CancellationToken cancellationToken);
}
