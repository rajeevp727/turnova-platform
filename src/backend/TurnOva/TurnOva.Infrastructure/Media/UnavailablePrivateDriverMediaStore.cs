using TurnOva.Application.Media;

namespace TurnOva.Infrastructure.Media;

/// <summary>
/// Fail-closed adapter used until a private object-storage and malware-scanning provider is configured.
/// </summary>
public sealed class UnavailablePrivateDriverMediaStore : IPrivateDriverMediaStore
{
    public bool IsConfigured => false;

    public Task<PrivateDriverMediaMetadata?> StoreAsync(
        string driverId,
        string mediaType,
        string contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        Task.FromResult<PrivateDriverMediaMetadata?>(null);

    public Task<PrivateDriverMediaAsset?> ReadAsync(
        string driverId,
        string reference,
        CancellationToken cancellationToken) =>
        Task.FromResult<PrivateDriverMediaAsset?>(null);

    public Task<bool> IsOwnedAndCleanAsync(
        string driverId,
        string mediaType,
        string reference,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> DeleteAsync(
        string driverId,
        string reference,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
