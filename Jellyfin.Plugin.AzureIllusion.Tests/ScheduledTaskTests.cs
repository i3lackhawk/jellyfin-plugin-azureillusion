using Jellyfin.Plugin.AzureIllusion.ScheduledTasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class ScheduledTaskTests
{
    [Fact]
    public void DownloadTask_IsDiscoverableAndHasNoConflictingDefaultSchedule()
    {
        Assert.True(typeof(IScheduledTask).IsAssignableFrom(typeof(DownloadMissingSubtitlesTask)));
        var task = new DownloadMissingSubtitlesTask(null!, null!, NullLogger<DownloadMissingSubtitlesTask>.Instance);
        Assert.Empty(task.GetDefaultTriggers());
        Assert.Equal("PolskieNapisyAnimeDownloadMissingSubtitles", task.Key);
    }
}
