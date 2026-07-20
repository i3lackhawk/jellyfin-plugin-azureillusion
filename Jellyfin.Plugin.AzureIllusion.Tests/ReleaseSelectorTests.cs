using Jellyfin.Plugin.AzureIllusion.Api;
using Jellyfin.Plugin.AzureIllusion.Subtitles;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class ReleaseSelectorTests
{
    [Fact]
    public void LimitGroups_PreservesRankingAndDistinctGroupLimit()
    {
        var releases = new[]
        {
            CreateRelease("1", 1, "grupa-a"),
            CreateRelease("2", 2, "grupa-a"),
            CreateRelease("3", 3, "grupa-b"),
            CreateRelease("4", 4, "grupa-c"),
        };

        var selected = ReleaseSelector.LimitGroups(releases, 2);

        Assert.Equal(new[] { "1", "2", "3" }, selected.Select(item => item.Id));
    }

    [Fact]
    public void LimitGroups_ZeroKeepsAllReleases()
    {
        var releases = new[] { CreateRelease("1", 1, "a"), CreateRelease("2", 2, "b") };

        Assert.Equal(releases, ReleaseSelector.LimitGroups(releases, 0));
    }

    private static SubtitleRelease CreateRelease(string id, int rank, string groupSlug)
        => new(
            id,
            rank,
            new SubtitleSeason(1, "Sezon 1", null),
            new SubtitleEpisode(1, "Odcinek 1", "EPISODE", 1, null),
            new GroupItem(groupSlug, groupSlug, groupSlug, null, "ACTIVE"),
            "pl",
            "ass",
            id + ".ass",
            100,
            id,
            "1.0",
            true,
            false,
            new SubtitleRating(8, 1, 8, 8, 8),
            0,
            DateTimeOffset.UtcNow,
            "/download/" + id);
}
