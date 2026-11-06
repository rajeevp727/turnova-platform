using System.Collections.Concurrent;
using TurnOva.Application.Dispatch;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Infrastructure.Dispatch;

public sealed class InMemoryRideAgreementStore : IRideAgreementStore
{
    private readonly ConcurrentDictionary<string, RideAgreementVersion> _versions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RideAgreementConfirmation> _confirmations = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<RideAgreementVersion>> GetVersionsAsync(string rideId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RideAgreementVersion> result = _versions.Values
            .Where(item => item.RideId == rideId).OrderBy(item => item.Version).ToArray();
        return Task.FromResult(result);
    }

    public Task<bool> TryCreateVersionAsync(RideAgreementVersion version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_versions.TryAdd(version.Id, version));
    }

    public Task<IReadOnlyList<RideAgreementConfirmation>> GetConfirmationsAsync(string rideId, int version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RideAgreementConfirmation> result = _confirmations.Values
            .Where(item => item.RideId == rideId && item.Version == version).ToArray();
        return Task.FromResult(result);
    }

    public Task<bool> TryCreateConfirmationAsync(RideAgreementConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_confirmations.TryAdd(confirmation.Id, confirmation));
    }
}
