using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class DriverRideReservationQueryTests
{
    [Fact]
    public void QueryFiltersToReservationDocumentsAndExcludesAppendOnlyAuditFacts()
    {
        Assert.Contains("ARRAY_CONTAINS(@driverIds, c.driverId)", DriverRideReservationQuery.Sql);
        Assert.Contains("c.id = c.driverId", DriverRideReservationQuery.Sql);
        Assert.DoesNotContain("documentType", DriverRideReservationQuery.Sql, StringComparison.OrdinalIgnoreCase);

        var query = DriverRideReservationQuery.Create(["driver-a"]);
        Assert.Equal(DriverRideReservationQuery.Sql, query.QueryText);
    }

    [Fact]
    public void QueryBuilderRejectsEmptyAndOversizedBatches()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DriverRideReservationQuery.Create([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => DriverRideReservationQuery.Create(
            Enumerable.Range(0, 101).Select(index => $"driver-{index}").ToArray()));
    }
}
