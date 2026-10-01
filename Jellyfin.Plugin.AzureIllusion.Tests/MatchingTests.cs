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
}
