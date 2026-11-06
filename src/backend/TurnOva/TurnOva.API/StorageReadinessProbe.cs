using Microsoft.Azure.Cosmos;

namespace TurnOva.API;

/// <summary>
/// Performs a bounded dependency probe for the API readiness endpoint.
/// Request cancellation is propagated; only dependency failures and the probe's own timeout
/// are translated into a readiness failure.
/// </summary>
public sealed class StorageReadinessProbe
{
    private readonly Func<CancellationToken, Task> _probe;
    private readonly TimeSpan _timeout;

    public StorageReadinessProbe(Func<CancellationToken, Task> probe, TimeSpan timeout)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        _timeout = timeout;
    }

    public async Task CheckAsync(CancellationToken requestCancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            await _probe(timeoutSource.Token);
        }
        catch (CosmosException ex)
        {
            throw new StorageReadinessException("unavailable", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new StorageReadinessException("unavailable", ex);
        }
        catch (TimeoutException ex)
        {
            throw new StorageReadinessException("timeout", ex);
        }
        catch (OperationCanceledException ex) when (!requestCancellationToken.IsCancellationRequested)
        {
            throw new StorageReadinessException("timeout", ex);
        }
    }
}

public sealed class StorageReadinessException : Exception
{
    public StorageReadinessException(string failureKind, Exception innerException)
        : base("The storage readiness probe failed.", innerException)
    {
        FailureKind = failureKind;
    }

    public string FailureKind { get; }
}
