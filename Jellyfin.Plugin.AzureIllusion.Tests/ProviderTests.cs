using Jellyfin.Plugin.AzureIllusion.Subtitles;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class ProviderTests
{
    [Fact]
    public void BuildMediaKey_PrefersNormalizedMediaPath()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            MediaPath = @"D:\Anime\Seria\S01E00.mkv",
            SeriesName = "Seria",
            ParentIndexNumber = 1,
            IndexNumber = 0,
        };

        Assert.Equal("d:/anime/seria/s01e00.mkv", AzureIllusionSubtitleProvider.BuildMediaKey(request));
    }

    [Fact]
    public void BuildMediaKey_FallsBackToEpisodeIdentity()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "Fate",
            ParentIndexNumber = 1,
            IndexNumber = 0,
        };

        Assert.Equal("episode|fate|1|0", AzureIllusionSubtitleProvider.BuildMediaKey(request));
    }
}
