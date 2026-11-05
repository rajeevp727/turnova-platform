namespace TurnOva.API;

public sealed class DispatchOfferOptions
{
    private int _offerLifetimeSeconds = 30;
    public int OfferLifetimeSeconds { get => Math.Clamp(_offerLifetimeSeconds, 10, 120); set => _offerLifetimeSeconds = value; }
    public int NotificationPollIntervalSeconds { get; set; } = 2;
    public int NotificationBatchSize { get; set; } = 50;
    public int LifetimeSeconds => OfferLifetimeSeconds;
}