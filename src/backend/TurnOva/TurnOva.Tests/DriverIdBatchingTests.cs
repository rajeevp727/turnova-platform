using TurnOva.API;
using TurnOva.Application.Dispatch;
using TurnOva.Infrastructure.Dispatch;

namespace TurnOva.Tests;

public sealed class DriverIdBatchingTests
{
    [Fact]
    public void ChunkCreatesOneBoundedRideAndReservationQueryPerDistinctDriverBatch()
    {
        var driverIds = Enumerable.Range(0, 205)
            .Select(index => $"driver-{index:D3}")
            .Concat(["driver-000", "", "  "])
            .ToArray();

        var batches = DriverIdBatching.Chunk(driverIds);
        Assert.Equal(new[] { 100, 100, 5 }, batches.Select(batch => batch.Length));
        Assert.Equal(205, batches.SelectMany(batch => batch).Count());
        Assert.Equal(205, batches.SelectMany(batch => batch).Distinct(StringComparer.Ordinal).Count());

        var rideQueries = batches.Select(DriverActiveRideQuery.Create).ToArray();
        var reservationQueries = batches.Select(DriverRideReservationQuery.Create).ToArray();
        Assert.Equal(3, rideQueries.Length);
        Assert.Equal(3, reservationQueries.Length);
        Assert.All(rideQueries, query => Assert.Equal(DriverActiveRideQuery.Sql, query.QueryText));
        Assert.All(reservationQueries, query => Assert.Equal(DriverRideReservationQuery.Sql, query.QueryText));
    }

    [Fact]
    public void ChunkUsesOrdinalIdentityAndReturnsNoEmptyBatches()
    {
        var batches = DriverIdBatching.Chunk(["driver-a", "Driver-A", "driver-a", null!, " "]);

        Assert.Equal(new[] { "driver-a", "Driver-A" }, batches.SelectMany(batch => batch));
        Assert.Empty(DriverIdBatching.Chunk(["", " ", null!]));
    }
}
