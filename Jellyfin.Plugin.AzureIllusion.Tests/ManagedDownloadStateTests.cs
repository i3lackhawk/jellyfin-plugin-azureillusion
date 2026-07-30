using Jellyfin.Plugin.AzureIllusion.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class ManagedDownloadStateTests
{
    [Fact]
    public async Task ManagedRecord_PersistsUpdateMetadataAcrossStoreInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-state-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "downloads.json");
        try
        {
            var source = new ManagedSubtitleDownload(
                "media-1",
                "release-1",
                "abc",
                DateTimeOffset.UtcNow,
                "/anime/episode.mkv",
                "pl",
                "pol.Frixy",
                "ass",
                "Frixy",
                "frixy",
                "123",
                1,
                2,
                "sha256:abc");
            var first = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
            await first.MarkDownloadedAsync(source, CancellationToken.None);

            var second = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
            var restored = Assert.Single(await second.GetAllAsync(CancellationToken.None));

            Assert.Equal(source.MediaPath, restored.MediaPath);
            Assert.Equal(source.StoredLanguage, restored.StoredLanguage);
            Assert.Equal(source.AniListId, restored.AniListId);
            Assert.Equal(source.SourceRevision, restored.SourceRevision);
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
    public async Task LegacyRecord_RemainsReadableAndIsNotInventedAsManaged()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-state-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "downloads.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "downloads": [
                    {
                      "mediaKey": "legacy-media",
                      "releaseId": "legacy-release",
                      "checksum": "legacy-checksum",
                      "downloadedAtUtc": "2026-07-01T00:00:00+00:00"
                    }
                  ]
                }
                """);

            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
            var legacy = Assert.Single(await store.GetAllAsync(CancellationToken.None));

            Assert.Equal("legacy-release", legacy.ReleaseId);
            Assert.Null(legacy.LocalPath);
            Assert.Null(legacy.AniListId);
            Assert.True(await store.ContainsAsync("legacy-media", "legacy-release", null, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
