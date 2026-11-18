namespace TurnOva.Infrastructure.Database;

public interface ICosmosMigration
{
    string Id { get; }
    string Description { get; }
    Task UpAsync(ICosmosMigrationDatabase database, CancellationToken cancellationToken);
}
