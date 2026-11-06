using TurnOva.Domain.Dispatch;

namespace TurnOva.Application.Dispatch;

public interface IRideAgreementStore
{
    Task<IReadOnlyList<RideAgreementVersion>> GetVersionsAsync(string rideId, CancellationToken cancellationToken = default);
    Task<bool> TryCreateVersionAsync(RideAgreementVersion version, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RideAgreementConfirmation>> GetConfirmationsAsync(string rideId, int version, CancellationToken cancellationToken = default);
    Task<bool> TryCreateConfirmationAsync(RideAgreementConfirmation confirmation, CancellationToken cancellationToken = default);
}
