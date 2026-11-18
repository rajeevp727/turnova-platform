using System.Net;
using Microsoft.Azure.Cosmos;

namespace TurnOva.Infrastructure.Database;

public sealed class CosmosMigrationLeaseDocument
{
    public string Id { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class CosmosMigrationLeaseStore(Container locks) : ICosmosMigrationLeaseStore
{
    private const string LeaseId = "schema-migrations";

    public async Task<CosmosMigrationLease?> TryCreateAsync(
        string owner,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var document = new CosmosMigrationLeaseDocument
        {
            Id = LeaseId,
            Owner = owner,
            ExpiresAtUtc = expiresAtUtc
        };

        try
        {
            var response = await locks.CreateItemAsync(
                document, new PartitionKey(document.Id), cancellationToken: cancellationToken);
            return ToLease(document, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    public async Task<CosmosMigrationLease?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await locks.ReadItemAsync<CosmosMigrationLeaseDocument>(
                LeaseId, new PartitionKey(LeaseId), cancellationToken: cancellationToken);
            return ToLease(response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CosmosMigrationLease?> TryReplaceAsync(
        CosmosMigrationLease expected,
        string newOwner,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(expected.Id, LeaseId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(expected.ETag) ||
            string.IsNullOrWhiteSpace(newOwner))
            throw new ArgumentException("A valid expected migration lease and owner are required.", nameof(expected));

        var replacement = new CosmosMigrationLeaseDocument
        {
            Id = expected.Id,
            Owner = newOwner,
            ExpiresAtUtc = expiresAtUtc
        };

        try
        {
            var response = await locks.ReplaceItemAsync(
                replacement,
                replacement.Id,
                new PartitionKey(replacement.Id),
                new ItemRequestOptions { IfMatchEtag = expected.ETag },
                cancellationToken);
            return ToLease(replacement, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return null;
        }
    }

    public async Task<bool> TryReleaseAsync(
        CosmosMigrationLease expected,
        CancellationToken cancellationToken)
    {
        try
        {
            await locks.DeleteItemAsync<CosmosMigrationLeaseDocument>(
                expected.Id,
                new PartitionKey(expected.Id),
                new ItemRequestOptions { IfMatchEtag = expected.ETag },
                cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (
            ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            // Never delete a lease that another API instance has replaced.
            return false;
        }
    }

    private static CosmosMigrationLease ToLease(CosmosMigrationLeaseDocument document, string etag) =>
        new(document.Id, document.Owner, etag, document.ExpiresAtUtc);
}
