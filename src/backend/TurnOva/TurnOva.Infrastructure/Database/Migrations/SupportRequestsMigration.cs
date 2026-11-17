namespace TurnOva.Infrastructure.Database.Migrations;

public sealed class SupportRequestsMigration : ICosmosMigration
{
    public string Id => "202611170001_SupportRequests";
    public string Description => "Create reporter-partitioned neutral support request intake storage.";

    public Task UpAsync(ICosmosMigrationDatabase database, CancellationToken cancellationToken) =>
        database.CreateContainerIfNotExistsAsync("SupportRequests", "/reporterId", cancellationToken);
}
