using Jellyfin.Plugin.AzureIllusion.Matching;

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
}
