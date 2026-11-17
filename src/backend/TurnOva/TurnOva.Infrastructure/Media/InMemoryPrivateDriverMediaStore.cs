using System.Collections.Concurrent;
using System.Security.Cryptography;
using TurnOva.Application.Media;

namespace TurnOva.Infrastructure.Media;

/// <summary>
/// Development/test-only media adapter. It never writes media bytes to a Driver profile.
/// Tests may opt into a deterministic Clean scan result; Development defaults to PendingScan.
/// </summary>
public sealed class InMemoryPrivateDriverMediaStore(bool markUploadsCleanForTests) : IPrivateDriverMediaStore
{
    private readonly ConcurrentDictionary<string, PrivateDriverMediaAsset> _assets =
        new(StringComparer.Ordinal);

    public bool IsConfigured => true;

    public Task<PrivateDriverMediaMetadata?> StoreAsync(
        string driverId,
        string mediaType,
        string contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(driverId) ||
            string.IsNullOrWhiteSpace(mediaType) ||
            string.IsNullOrWhiteSpace(contentType) ||
            content.IsEmpty)
            return Task.FromResult<PrivateDriverMediaMetadata?>(null);

        var reference = "drvmedia_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var metadata = new PrivateDriverMediaMetadata(
            reference,
            driverId,
            mediaType,
            contentType,
            content.Length,
            markUploadsCleanForTests ? "Clean" : "PendingScan",
            DateTimeOffset.UtcNow);
        var asset = new PrivateDriverMediaAsset(metadata, content.ToArray());
        if (!_assets.TryAdd(reference, asset))
            return Task.FromResult<PrivateDriverMediaMetadata?>(null);

        return Task.FromResult<PrivateDriverMediaMetadata?>(metadata);
    }

    public Task<PrivateDriverMediaAsset?> ReadAsync(
        string driverId,
        string reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_assets.TryGetValue(reference, out var asset) &&
            string.Equals(asset.Metadata.DriverId, driverId, StringComparison.Ordinal))
        {
            return Task.FromResult<PrivateDriverMediaAsset?>(
                asset with { Content = asset.Content.ToArray() });
        }

        return Task.FromResult<PrivateDriverMediaAsset?>(null);
    }

    public Task<bool> IsOwnedAndCleanAsync(
        string driverId,
        string mediaType,
        string reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var valid = _assets.TryGetValue(reference, out var asset) &&
            string.Equals(asset.Metadata.DriverId, driverId, StringComparison.Ordinal) &&
            string.Equals(asset.Metadata.MediaType, mediaType, StringComparison.Ordinal) &&
            string.Equals(asset.Metadata.ScanStatus, "Clean", StringComparison.Ordinal);
        return Task.FromResult(valid);
    }

    public Task<bool> DeleteAsync(
        string driverId,
        string reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_assets.TryGetValue(reference, out var asset) ||
            !string.Equals(asset.Metadata.DriverId, driverId, StringComparison.Ordinal))
            return Task.FromResult(false);

        return Task.FromResult(_assets.TryRemove(reference, out _));
    }
}
