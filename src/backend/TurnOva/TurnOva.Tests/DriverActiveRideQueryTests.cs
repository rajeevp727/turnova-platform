using TurnOva.API;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class DriverActiveRideQueryTests
{
    [Fact]
    public void QueryUsesMinimalProjectionAndIncludesEveryActiveLifecycleState()
    {
        Assert.Contains(
            "SELECT VALUE { rideId: c.rideId, driverId: c.driverId, status: c.status, negotiationExpiresAtUtc: c.negotiationExpiresAtUtc }",
            DriverActiveRideQuery.Sql);
        Assert.Contains("ARRAY_CONTAINS(@driverIds, c.driverId)", DriverActiveRideQuery.Sql);
        Assert.DoesNotContain("SELECT *", DriverActiveRideQuery.Sql, StringComparison.OrdinalIgnoreCase);

        foreach (var status in RideLifecycle.ActiveStatuses)
            Assert.Contains($"'{status}'", DriverActiveRideQuery.Sql);
    }

    [Fact]
    public void QueryBuilderParameterizesAndBoundsEachDriverBatch()
    {
        var query = DriverActiveRideQuery.Create(["driver-a", "driver-b"]);
        Assert.Equal(DriverActiveRideQuery.Sql, query.QueryText);

        Assert.Throws<ArgumentOutOfRangeException>(() => DriverActiveRideQuery.Create([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => DriverActiveRideQuery.Create(
            Enumerable.Range(0, 101).Select(index => $"driver-{index}").ToArray()));
    }
}
