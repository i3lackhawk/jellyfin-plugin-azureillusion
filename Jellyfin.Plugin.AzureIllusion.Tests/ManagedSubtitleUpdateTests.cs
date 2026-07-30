using Jellyfin.Plugin.AzureIllusion.ScheduledTasks;
using Jellyfin.Plugin.AzureIllusion.State;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class ManagedSubtitleUpdateTests
{
    [Fact]
    public void UpdateTask_IsDiscoverableAndHasDailyTrigger()
    {
        Assert.True(typeof(IScheduledTask).IsAssignableFrom(typeof(UpdateDownloadedSubtitlesTask)));
        var task = new UpdateDownloadedSubtitlesTask(
            null!,
            null!,
            null!,
            null!,
            NullLogger<UpdateDownloadedSubtitlesTask>.Instance);

        var trigger = Assert.Single(task.GetDefaultTriggers());
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, trigger.Type);
        Assert.Equal("PolskieNapisyAnimeUpdateDownloadedSubtitles", task.Key);
    }

    [Fact]
    public void ResolveLocalPath_UsesOnlyOneExactManagedSubtitle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var mediaPath = Path.Combine(directory, "Episode 01.mkv");
            var subtitlePath = Path.Combine(directory, "Episode 01.pol.Frixy.ass");
            File.WriteAllText(subtitlePath, "subtitle");
            var record = Record(mediaPath);

            var result = UpdateDownloadedSubtitlesTask.ResolveLocalPath(record, [subtitlePath]);

            Assert.Equal(subtitlePath, result);
            Assert.True(File.Exists(subtitlePath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ResolveLocalPath_RejectsAmbiguousFilesWithoutDeletingEither()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-update-tests", Guid.NewGuid().ToString("N"));
        var secondDirectory = Path.Combine(directory, "metadata");
        Directory.CreateDirectory(secondDirectory);
        try
        {
            var mediaPath = Path.Combine(directory, "Episode 01.mkv");
            var first = Path.Combine(directory, "Episode 01.pol.Frixy.ass");
            var second = Path.Combine(secondDirectory, "Episode 01.pol.Frixy.ass");
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");

            var result = UpdateDownloadedSubtitlesTask.ResolveLocalPath(Record(mediaPath), [first, second]);

            Assert.Null(result);
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static ManagedSubtitleDownload Record(string mediaPath)
        => new(
            "media-key",
            "release-1",
            "checksum",
            DateTimeOffset.UtcNow,
            mediaPath,
            "pl",
            "pol.Frixy",
            "ass",
            "Frixy",
            "frixy",
            "123",
            1,
            1,
            "sha256:checksum");
}
