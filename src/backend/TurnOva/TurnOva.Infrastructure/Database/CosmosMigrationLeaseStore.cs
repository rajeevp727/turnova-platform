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
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

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
            return ToLease(response.Resource, response.ETag, owner);
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
        ArgumentNullException.ThrowIfNull(expected);
        if (!string.Equals(expected.Id, LeaseId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(expected.Owner) ||
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
            return ToLease(response.Resource, response.ETag, newOwner);
        }
        catch (CosmosException ex) when (
            ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            // The lease disappeared or another runner replaced it after the read.
            // Both outcomes mean this acquisition/renewal attempt lost its race.
            return null;
        }
    }

    public async Task<bool> TryReleaseAsync(
        CosmosMigrationLease expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!string.Equals(expected.Id, LeaseId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(expected.Owner) ||
            string.IsNullOrWhiteSpace(expected.ETag))
            throw new ArgumentException("A valid expected migration lease is required.", nameof(expected));

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

    private static CosmosMigrationLease ToLease(
        CosmosMigrationLeaseDocument? document,
        string etag,
        string? expectedOwner = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(document.Id, LeaseId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(document.Owner) ||
            (expectedOwner is not null &&
                !string.Equals(document.Owner, expectedOwner, StringComparison.Ordinal)) ||
            string.IsNullOrWhiteSpace(etag))
            throw new InvalidOperationException(
                "Cosmos returned migration lease metadata that does not match the expected ID, owner and ETag.");

        return new(document.Id, document.Owner, etag, document.ExpiresAtUtc);
    }
}
