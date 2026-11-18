namespace TurnOva.Application.Media;

public sealed record PrivateDriverMediaMetadata(
    string Reference,
    string DriverId,
    string MediaType,
    string ContentType,
    long Length,
    string ScanStatus,
    DateTimeOffset UploadedAtUtc);

public sealed record PrivateDriverMediaAsset(
    PrivateDriverMediaMetadata Metadata,
    byte[] Content);

public interface IPrivateDriverMediaStore
{
    bool IsConfigured { get; }

    Task<PrivateDriverMediaMetadata?> StoreAsync(
        string driverId,
        string mediaType,
        string contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    Task<PrivateDriverMediaAsset?> ReadAsync(
        string driverId,
        string reference,
        CancellationToken cancellationToken);

    Task<bool> IsOwnedAndCleanAsync(
        string driverId,
        string mediaType,
        string reference,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        string driverId,
        string reference,
        CancellationToken cancellationToken);
}
