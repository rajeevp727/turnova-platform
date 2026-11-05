using System.Text.Json.Serialization;
namespace TurnOva.Domain.Dispatch;
public sealed record DriverRideReservation(string DriverId, string RideId, DateTimeOffset AcquiredAtUtc)
{
    [JsonPropertyName("id")]
    public string Id => DriverId;
}