using Jellyfin.Plugin.AzureIllusion.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class DownloadStateStoreTests
{
    [Fact]
    public async Task CorruptedState_DoesNotPretendThatEarlierDownloadsNeverHappened()
    {
        var directory = Path.Combine(Path.GetTempPath(), "azureillusion-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "downloads.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, "{incomplete");
            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ContainsAsync("media", "release", null, CancellationToken.None));
            Assert.Equal("{incomplete", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MarkDownloaded_PersistsReleaseAcrossStoreInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "azureillusion-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "downloads.json");
        try
        {
            var first = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
            await first.MarkDownloadedAsync("media-1", "release-1", "checksum-1", CancellationToken.None);

            var second = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
            Assert.True(await second.ContainsAsync("media-1", "release-1", null, CancellationToken.None));
            Assert.True(await second.ContainsAsync("media-1", "other-release", "checksum-1", CancellationToken.None));
            Assert.False(await second.ContainsAsync("media-2", "release-1", "checksum-1", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task MarkAttempted_RotatesEntryWithoutChangingSourceMissingState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "azureillusion-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "downloads.json");
        try
        {
            var missingSince = DateTimeOffset.UtcNow.AddDays(-2);
            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
            await store.MarkDownloadedAsync(new ManagedSubtitleDownload(
                "media", "release", "checksum", DateTimeOffset.UtcNow,
                SourceMissingSinceUtc: missingSince), CancellationToken.None);

            await store.MarkAttemptedAsync("media", "release", CancellationToken.None);

            var record = Assert.Single(await store.GetAllAsync(CancellationToken.None));
            Assert.NotNull(record.LastCheckedAtUtc);
            Assert.Equal(missingSince, record.SourceMissingSinceUtc);
            Assert.Equal("checksum", record.Checksum);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
