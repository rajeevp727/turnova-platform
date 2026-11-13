using Microsoft.Azure.Cosmos;
using System.Diagnostics;
using TurnOva.API.Observability;

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
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "success";
        using var activity = TurnOvaDiagnostics.StartStorageReadinessCheck();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeoutSource.CancelAfter(_timeout);

        Task? probeTask = null;
        try
        {
            probeTask = _probe(timeoutSource.Token);
            // Enforce the deadline even if a provider implementation ignores its token.
            await probeTask.WaitAsync(timeoutSource.Token);
        }
        catch (CosmosException ex)
        {
            outcome = "unavailable";
            throw new StorageReadinessException("unavailable", ex);
        }
        catch (HttpRequestException ex)
        {
            outcome = "unavailable";
            throw new StorageReadinessException("unavailable", ex);
        }
        catch (OperationCanceledException) when (requestCancellationToken.IsCancellationRequested)
        {
            ObserveLateProbeFailure(probeTask);
            outcome = "cancelled";
            throw;
        }
        catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested)
        {
            ObserveLateProbeFailure(probeTask);
            outcome = "timeout";
            throw new StorageReadinessException("timeout", ex);
        }
        catch (OperationCanceledException ex)
        {
            // A provider may cancel independently of this request and the probe's own deadline.
            // That is a dependency failure, not proof that our readiness timeout elapsed.
            outcome = "unavailable";
            throw new StorageReadinessException("unavailable", ex);
        }
        catch (Exception ex)
        {
            outcome = "unavailable";
            throw new StorageReadinessException("unavailable", ex);
        }
        finally
        {
            activity?.SetTag("turnova.storage.outcome", outcome);
            if (outcome is "unavailable" or "timeout")
                activity?.SetStatus(ActivityStatusCode.Error);
            TurnOvaDiagnostics.RecordStorageReadiness(outcome, Stopwatch.GetElapsedTime(startedAt));
        }
    }

    private static void ObserveLateProbeFailure(Task? probeTask)
    {
        if (probeTask is null) return;

        // A provider may ignore cancellation and fault after the bounded wait has returned.
        // Observe that late fault without logging or exposing provider details.
        _ = probeTask.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
