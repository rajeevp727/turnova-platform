using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class RideOfferStateTests
{
    [Fact]
    public void PendingOffer_IsExpiredOnlyAtOrAfterDeadline()
    {
        var deadline = DateTimeOffset.Parse("2026-11-06T12:00:00Z");
        var state = CreateState(deadline);

        Assert.False(state.IsExpiredAt(deadline.AddTicks(-1)));
        Assert.True(state.IsExpiredAt(deadline));
        Assert.True(state.IsExpiredAt(deadline.AddMinutes(1)));
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Rejected")]
    [InlineData("Expired")]
    [InlineData("Cancelled")]
    public void ResolvedOffer_IsNeverConsideredExpired(string status)
    {
        var state = CreateState(DateTimeOffset.UtcNow) with { Status = status };

        Assert.False(state.IsExpiredAt(DateTimeOffset.UtcNow.AddDays(1)));
    }

    private static RideOfferState CreateState(DateTimeOffset deadline) => new(
        "ride-1", "zone-1", "auto", "offer-1", "driver-1",
        deadline.AddMinutes(-1), deadline, RideOfferStatuses.Pending,
        [new RideOfferAttempt("offer-1", "driver-1", deadline.AddMinutes(-1), deadline, RideOfferStatuses.Pending)]);
}
