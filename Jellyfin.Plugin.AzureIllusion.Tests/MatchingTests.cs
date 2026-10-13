using Jellyfin.Plugin.AzureIllusion.Api;
using Jellyfin.Plugin.AzureIllusion.Matching;
using MediaBrowser.Controller.Subtitles;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class MatchingTests
{
    [Theory]
    [InlineData("Boku no Hero Academia: 6th Season", "boku no hero academia 6th season")]
    [InlineData("Kusuriya no Hitorigoto", "kusuriya no hitorigoto")]
    [InlineData("Pokémon: Żółty", "pokemon zołty")]
    public void NormalizeTitle_RemovesPunctuationAndDiacritics(string input, string expected)
    {
        Assert.Equal(expected, AniListResolver.NormalizeTitle(input));
    }

    [Fact]
    public void ManualMapping_UsesSeriesSeasonBeforeEpisodeAndAncestorPaths()
    {
        var seriesId = Guid.Parse("5fb44be8-2108-a340-a57a-c22f9cf4c6ac");
        var episodeId = Guid.Parse("dc02df98-3513-f07b-98e2-9687e5f7de3b");
        var request = new SubtitleSearchRequest
        {
            MediaPath = @"/anime/Yuru Camp/Season 02/Episode 01.mkv",
            ParentIndexNumber = 2,
        };
        const string mappings = """
            {
              "id:5fb44be82108a340a57ac22f9cf4c6ac#season:2": "104459",
              "id:dc02df983513f07b98e29687e5f7de3b": "999",
              "path:/anime/yuru camp#season:2": "888"
            }
            """;

        Assert.Equal("104459", AniListResolver.ResolveLocalMapping(request, mappings, episodeId, seriesId));
        Assert.Equal("888", AniListResolver.ResolveLocalMapping(request, """{"path:/anime/yuru camp#season:2":"888"}"""));
    }

    [Fact]
    public void TitleFallbacks_AddSeasonSpecificAniListNamesBeforeParentSeries()
    {
        var titles = AniListResolver.BuildTitleFallbacks(
            "The Rising of the Shield Hero",
            "Season 3",
            3);

        Assert.Equal(
            [
                "The Rising of the Shield Hero Season 3",
                "The Rising of the Shield Hero 3rd Season",
            ],
            titles);
    }

    [Fact]
    public void TitleFallbacks_KeepNamedSeasonMetadataFirst()
    {
        var titles = AniListResolver.BuildTitleFallbacks(
            "Tate no Yuusha no Nariagari",
            "Tate no Yuusha no Nariagari Season 2",
            2);

        Assert.Equal("Tate no Yuusha no Nariagari Season 2", titles[0]);
        Assert.DoesNotContain("Season 2", titles);
    }

    [Fact]
    public void ExactTitleFallback_CanIgnoreIncorrectJellyfinSeasonYearWhenUnique()
    {
        var candidate = new AnimeItem(
            "shield-hero-2",
            "shield-hero-2",
            "111321",
            new AnimeTitles("Tate no Yuusha no Nariagari Season 2", "The Rising of the Shield Hero Season 2", null),
            "TV",
            2022,
            13,
            null);

        Assert.Null(AniListResolver.SelectExactTitleCandidate(
            [candidate],
            "The Rising of the Shield Hero Season 2",
            2019,
            false));
        Assert.Same(candidate, AniListResolver.SelectExactTitleCandidate(
            [candidate],
            "The Rising of the Shield Hero Season 2",
            null,
            true));
    }

    [Fact]
    public void ExactTitleFallback_RejectsAmbiguousMatchesEvenWhenYearIsIgnored()
    {
        var title = new AnimeTitles("Example Season 2", null, null);
        var candidates = new[]
        {
            new AnimeItem("a", "a", "100", title, "TV", 2022, 12, null),
            new AnimeItem("b", "b", "200", title, "TV", 2023, 12, null),
        };

        Assert.Null(AniListResolver.SelectExactTitleCandidate(candidates, "Example Season 2", null, true));
    }
}
