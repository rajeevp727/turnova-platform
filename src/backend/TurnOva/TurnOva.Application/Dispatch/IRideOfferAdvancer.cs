namespace TurnOva.Application.Dispatch;

public interface IRideOfferAdvancer
{
    Task<bool> AdvanceAsync(string rideId, CancellationToken cancellationToken = default);
}
