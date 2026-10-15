using Jellyfin.Plugin.AzureIllusion.ScheduledTasks;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class ScheduledTaskTests
{
    [Fact]
    public void DownloadTask_IsDiscoverableAndRunsDailyAtFour()
    {
        Assert.True(typeof(IScheduledTask).IsAssignableFrom(typeof(DownloadMissingSubtitlesTask)));
        var task = new DownloadMissingSubtitlesTask(null!, null!, null!, null!, null!, NullLogger<DownloadMissingSubtitlesTask>.Instance);
        var trigger = Assert.Single(task.GetDefaultTriggers());
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromHours(4).Ticks, trigger.TimeOfDayTicks);
        Assert.Equal("PolskieNapisyAnimeDownloadMissingSubtitles", task.Key);
    }

    [Fact]
    public void SimulationTask_IsDiscoverableAndHasNoDefaultSchedule()
    {
        Assert.True(typeof(IScheduledTask).IsAssignableFrom(typeof(SimulateMissingSubtitlesTask)));
        var task = new SimulateMissingSubtitlesTask(null!, null!, null!, null!, null!, NullLogger<DownloadMissingSubtitlesTask>.Instance);

        Assert.Empty(task.GetDefaultTriggers());
        Assert.Equal("PolskieNapisyAnimeSimulateMissingSubtitles", task.Key);
    }

    [Fact]
    public void AutomaticRequest_PreservesSeasonIdentifierAndNeverMergesParentSeriesIdentifier()
    {
        var seasonIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AniList"] = "season-2",
            ["Tvdb"] = "episode-tvdb",
        };

        var result = DownloadMissingSubtitlesTask.BuildProviderIdsForSearch(seasonIds);

        Assert.Equal("season-2", result["AniList"]);
        Assert.Equal("episode-tvdb", result["Tvdb"]);
        Assert.NotSame(seasonIds, result);
    }

    [Theory]
    [InlineData(true, null, false)]
    [InlineData(true, 1, true)]
    [InlineData(false, null, true)]
    public void AutomaticRequest_RequiresNumberOnlyForEpisodes(bool isEpisode, int? number, bool expected)
    {
        Assert.Equal(expected, DownloadMissingSubtitlesTask.HasRequiredEpisodeNumber(isEpisode, number));
    }

    [Fact]
    public void UpdateTask_HasNoCompetingDefaultSchedule()
    {
        var task = new UpdateDownloadedSubtitlesTask(null!, null!, null!, null!, null!, NullLogger<UpdateDownloadedSubtitlesTask>.Instance);

        Assert.Empty(task.GetDefaultTriggers());
    }

    [Fact]
    public void Configuration_DefaultsToPluginOwnedAutomationAndBoundedBatches()
    {
        var configuration = new PluginConfiguration();

        Assert.True(configuration.EnableAutomaticSearch);
        Assert.False(configuration.AllowJellyfinAutomaticSearch);
        Assert.Equal(250, configuration.UpdateBatchSize);
    }
}
