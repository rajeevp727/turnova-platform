using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using TurnOva.Domain.Dispatch;
using TurnOva.Infrastructure.Database;

namespace TurnOva.API;

/// <summary>
/// Saves one Driver-to-Union assignment only when the snapshot observed by the
/// caller is still current. The durable per-Driver reservation serializes the
/// normal transfer flow; this compare-and-swap also fences a stale request if
/// its temporary reservation expires while the request is still running.
/// </summary>
public static class UnionAssignmentWriter
{
    public static async Task<bool> TrySaveAsync(
        Container? assignmentContainer,
        ConcurrentDictionary<string, DriverUnionAssignment> inMemoryAssignments,
        DriverUnionAssignment assignment,
        DriverUnionAssignment? expectedCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inMemoryAssignments);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignment.DriverId);

        if (expectedCurrent is not null &&
            !string.Equals(expectedCurrent.DriverId, assignment.DriverId, StringComparison.Ordinal))
            throw new ArgumentException("The expected Union assignment must belong to the same Driver.", nameof(expectedCurrent));

        cancellationToken.ThrowIfCancellationRequested();

        if (assignmentContainer is null)
        {
            return expectedCurrent is null
                ? inMemoryAssignments.TryAdd(assignment.DriverId, assignment)
                : inMemoryAssignments.TryUpdate(assignment.DriverId, assignment, expectedCurrent);
        }

        var partitionKey = new PartitionKey(assignment.DriverId);
        if (expectedCurrent is null)
        {
            try
            {
                await assignmentContainer.CreateItemAsync(
                    assignment, partitionKey, cancellationToken: cancellationToken);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A competing create must never be converted into an overwrite.
                return false;
            }
        }

        ItemResponse<DriverUnionAssignment> currentResponse;
        try
        {
            currentResponse = await assignmentContainer.ReadItemAsync<DriverUnionAssignment>(
                assignment.DriverId, partitionKey, cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (currentResponse.Resource is null ||
            currentResponse.Resource != expectedCurrent ||
            !CosmosEtagPolicy.HasObservedEtag(currentResponse.ETag))
            return false;

        try
        {
            await assignmentContainer.ReplaceItemAsync(
                assignment,
                assignment.DriverId,
                partitionKey,
                new ItemRequestOptions { IfMatchEtag = currentResponse.ETag },
                cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (
            ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Missing, concurrently changed or stale records are retryable conflicts.
            return false;
        }
    }
}
