using Jellyfin.Plugin.AzureIllusion.ScheduledTasks;
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
}
