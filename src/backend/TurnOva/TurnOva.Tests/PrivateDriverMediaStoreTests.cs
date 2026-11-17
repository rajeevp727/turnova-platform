using TurnOva.Application.Media;
using TurnOva.Infrastructure.Media;

namespace TurnOva.Tests;

public sealed class PrivateDriverMediaStoreTests
{
    [Fact]
    public async Task StoredReferenceIsOpaqueAndBoundToDriverAndMediaType()
    {
        var store = new InMemoryPrivateDriverMediaStore(markUploadsCleanForTests: true);
        var content = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };

        var upload = await store.StoreAsync(
            "driver-a", "DrivingLicence", "application/pdf", content, CancellationToken.None);

        Assert.NotNull(upload);
        Assert.StartsWith("drvmedia_", upload.Reference, StringComparison.Ordinal);
        Assert.DoesNotContain("driver-a", upload.Reference, StringComparison.Ordinal);
        Assert.Equal("Clean", upload.ScanStatus);
        Assert.True(await store.IsOwnedAndCleanAsync(
            "driver-a", "DrivingLicence", upload.Reference, CancellationToken.None));
        Assert.False(await store.IsOwnedAndCleanAsync(
            "driver-b", "DrivingLicence", upload.Reference, CancellationToken.None));
        Assert.False(await store.IsOwnedAndCleanAsync(
            "driver-a", "VehicleRegistration", upload.Reference, CancellationToken.None));

        Assert.Null(await store.ReadAsync("driver-b", upload.Reference, CancellationToken.None));
        var read = await store.ReadAsync("driver-a", upload.Reference, CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(content, read.Content);
    }

    [Fact]
    public async Task PendingScanCannotBeUsedForOnboardingOrReview()
    {
        var store = new InMemoryPrivateDriverMediaStore(markUploadsCleanForTests: false);
        var upload = await store.StoreAsync(
            "driver-a", "ProfilePhoto", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }, CancellationToken.None);

        Assert.NotNull(upload);
        Assert.Equal("PendingScan", upload.ScanStatus);
        Assert.False(await store.IsOwnedAndCleanAsync(
            "driver-a", "ProfilePhoto", upload.Reference, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRequiresTheOwningDriver()
    {
        var store = new InMemoryPrivateDriverMediaStore(markUploadsCleanForTests: true);
        var upload = await store.StoreAsync(
            "driver-a", "Insurance", "application/pdf", new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.NotNull(upload);
        Assert.False(await store.DeleteAsync("driver-b", upload.Reference, CancellationToken.None));
        Assert.NotNull(await store.ReadAsync("driver-a", upload.Reference, CancellationToken.None));
        Assert.True(await store.DeleteAsync("driver-a", upload.Reference, CancellationToken.None));
        Assert.Null(await store.ReadAsync("driver-a", upload.Reference, CancellationToken.None));
    }

    [Fact]
    public async Task UnconfiguredProviderFailsClosed()
    {
        var store = new UnavailablePrivateDriverMediaStore();

        Assert.False(store.IsConfigured);
        Assert.Null(await store.StoreAsync(
            "driver-a", "ProfilePhoto", "image/jpeg", new byte[] { 1 }, CancellationToken.None));
        Assert.False(await store.IsOwnedAndCleanAsync(
            "driver-a", "ProfilePhoto", "drvmedia_unknown", CancellationToken.None));
    }
}
