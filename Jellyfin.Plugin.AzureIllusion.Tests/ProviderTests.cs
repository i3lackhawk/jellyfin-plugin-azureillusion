using Jellyfin.Plugin.AzureIllusion.Subtitles;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class ProviderTests
{
    [Fact]
    public void LibrarySelection_UsesDirectoryBoundaries()
    {
        Assert.True(AzureIllusionSubtitleProvider.IsSelectedLibrary(@"D:\Anime\Show\episode.mkv", [@"D:\Anime"]));
        Assert.False(AzureIllusionSubtitleProvider.IsSelectedLibrary(@"D:\Anime-old\Show\episode.mkv", [@"D:\Anime"]));
        Assert.True(AzureIllusionSubtitleProvider.IsSelectedLibrary(@"D:\Anywhere\episode.mkv", []));
    }

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

    [Fact]
    public void RequestedLanguages_UsesOnlyConfiguredVariantForJellyfinLanguage()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            Language = "pol",
        };

        var result = AzureIllusionSubtitleProvider.ResolveRequestedLanguages(request, ["pl", "pl2", "en"]);

        Assert.Equal(["pl", "pl2"], result);
    }

    [Fact]
    public void SeasonFallback_RequiresResultsFromAtMostOneSeason()
    {
        Assert.False(AzureIllusionSubtitleProvider.CanUseSeasonFallback([]));
        Assert.True(AzureIllusionSubtitleProvider.CanUseSeasonFallback([Release("a", 3), Release("b", 3)]));
        Assert.False(AzureIllusionSubtitleProvider.CanUseSeasonFallback([Release("a", 1), Release("b", 3)]));
    }

    [Theory]
    [InlineData("pl", "Frixy", "pol.Frixy")]
    [InlineData("pl", "Demo Subs", "pol.Demo-Subs")]
    [InlineData("pl", null, "pol.Inne")]
    [InlineData("pl", "Grupa / Test", "pol.Grupa-Test")]
    public void StoredLanguage_IncludesSafeGroupName(string language, string? group, string expected)
    {
        Assert.Equal(expected, AzureIllusionSubtitleProvider.BuildStoredLanguage(language, group));
    }
    [Fact]
    public void DiagnosticLogging_IsDisabledByDefault()
    {
        Assert.False(new Configuration.PluginConfiguration().EnableDiagnosticLogging);
        Assert.Empty(new Configuration.PluginConfiguration().IgnoredGroupSlugs);
    }

    [Fact]
    public void DiagnosticRequestPath_DropsQueryParametersAndSecrets()
    {
        var uri = new Uri("https://subs.example/api/public/v1/subtitles/search?q=One%20Piece&apiKey=secret");
        var value = Api.AzureIllusionApiClient.DiagnosticRequestPath(uri);

        Assert.Equal("/api/public/v1/subtitles/search", value);
        Assert.DoesNotContain("secret", value, StringComparison.Ordinal);
        Assert.DoesNotContain("?", value, StringComparison.Ordinal);
    }

    private static Api.SubtitleRelease Release(string id, double season)
    {
        return new Api.SubtitleRelease(
            id,
            1,
            new Api.SubtitleSeason(season, $"Sezon {season}", null),
            new Api.SubtitleEpisode(1, "1", "EPISODE", 1, null),
            null,
            "PL",
            "ASS",
            $"{id}.ass",
            10,
            null,
            "1",
            false,
            false,
            new Api.SubtitleRating(0, 0, 0, 0, 0),
            0,
            DateTimeOffset.UtcNow,
            $"https://example.invalid/{id}");
    }
}
