using System.Net;
using Microsoft.Azure.Cosmos;

namespace TurnOva.API;

/// <summary>
/// Preserves the Driver reservation cleanup when the conditional ride-accept write
/// definitely loses its compare-and-swap, while still giving request cancellation
/// precedence over provider failures. A successful committed write remains authoritative.
/// </summary>
public static class RideAcceptWriteGuard
{
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> write,
        Func<CancellationToken, Task> releaseReservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(releaseReservation);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await write();
        }
        catch (CosmosException exception) when (
            exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            try
            {
                await releaseReservation(CancellationToken.None);
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }
}
