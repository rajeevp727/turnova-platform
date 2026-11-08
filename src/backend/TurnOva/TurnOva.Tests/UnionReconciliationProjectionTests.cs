using System.Text.Json;
using Xunit;

namespace TurnOva.Tests;

public sealed class UnionReconciliationProjectionTests
{
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
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "agreementVersion",
                "agreedAmount",
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
}
