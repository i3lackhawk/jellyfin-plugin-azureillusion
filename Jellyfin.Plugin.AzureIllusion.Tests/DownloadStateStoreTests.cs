using Jellyfin.Plugin.AzureIllusion.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class DownloadStateStoreTests
{
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
}
