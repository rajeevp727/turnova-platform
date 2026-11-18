using System.Text.Json;
using TurnOva.API;
using Xunit;

namespace TurnOva.Tests;

public sealed class UnionReconciliationProjectionTests
{
    [Fact]
    public void UnionReconciliationCosmosQueryNeverSelectsTheFullRideAggregate()
    {
        var sql = UnionReconciliationQuery.Sql;

        var selectClause = sql.Split(" FROM c ", StringSplitOptions.None)[0];
        Assert.Equal(
            "SELECT c.serviceClass, c.unionId, " +
            "c.paymentReconciliation.rideId AS rideId, " +
            "c.paymentReconciliation.agreementVersion AS agreementVersion, " +
            "c.paymentReconciliation.unionId AS reportUnionId, " +
            "c.paymentReconciliation.agreedAmount AS agreedAmount, " +
            "c.paymentReconciliation.amountReceived AS amountReceived, " +
            "c.paymentReconciliation.delta AS delta, " +
            "c.paymentReconciliation.currency AS currency, " +
            "c.paymentReconciliation.reportedAtUtc AS reportedAtUtc, " +
            "c.paymentReconciliation.reportedByDriverId AS reportedByDriverId, " +
            "c.paymentReconciliation.status AS status",
            selectClause);
        Assert.Contains("c.unionId = @unionId", sql, StringComparison.Ordinal);
        Assert.Contains("c.paymentReconciliation.unionId = @unionId", sql, StringComparison.Ordinal);
        Assert.Contains("AND c.rideId = c.paymentReconciliation.rideId", sql, StringComparison.Ordinal);
        Assert.Contains("AND LOWER(c.serviceClass) = 'cab'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT *", sql, StringComparison.OrdinalIgnoreCase);

        foreach (var privateField in new[]
        {
            "c.riderId",
            "c.pickup",
            "c.dropoff",
            "c.stops",
            "c.pickupLatitude",
            "c.pickupLongitude",
            "c.driverLatitude",
            "c.driverLongitude",
            "c.phoneNumber",
            "c.email",
            "c.privateChat"
        })
        {
            Assert.DoesNotContain(privateField, sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UnionReconciliationQueryProjectionContainsOnlyMinimumOversightFields()
    {
        var projection = new UnionReconciliationQueryProjection(
            "cab",
            "union-1",
            "ride-1",
            2,
            "union-1",
            500m,
            450m,
            50m,
            "INR",
            new DateTimeOffset(2026, 10, 8, 12, 30, 0, TimeSpan.Zero),
            "driver-1",
            "DriverReportedUnverified");

        var json = JsonSerializer.Serialize(
            projection,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var propertyNames = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "agreedAmount",
                "agreementVersion",
                "amountReceived",
                "currency",
                "delta",
                "reportUnionId",
                "reportedAtUtc",
                "reportedByDriverId",
                "rideId",
                "serviceClass",
                "status",
                "unionId"
            },
            propertyNames);
        Assert.DoesNotContain("riderId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pickup", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dropoff", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stops", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latitude", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void UnionReconciliationCosmosQueryFiltersCabAtStorageBoundaryCaseInsensitively()
    {
        var sql = UnionReconciliationQuery.Sql;

        Assert.Contains("LOWER(c.serviceClass) = 'cab'", sql, StringComparison.Ordinal);
        Assert.Contains("c.unionId = @unionId", sql, StringComparison.Ordinal);
        Assert.Contains("c.paymentReconciliation.unionId = @unionId", sql, StringComparison.Ordinal);
    }
}
