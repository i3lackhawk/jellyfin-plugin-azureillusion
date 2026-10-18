using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.AzureIllusion.Api;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using Jellyfin.Plugin.AzureIllusion.Matching;
using Jellyfin.Plugin.AzureIllusion.ScheduledTasks;
using Jellyfin.Plugin.AzureIllusion.State;
using Jellyfin.Plugin.AzureIllusion.Subtitles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class ReliabilityRegressionTests
{
    [Fact]
    public async Task QueuedMaintenance_WaitsAndCancelledWaitDoesNotLeakQueueCount()
    {
        using var gate = new SemaphoreSlim(0, 1);
        using var cancel = new CancellationTokenSource();
        var initial = MaintenanceProgress.Current.Queued;
        var waiting = MaintenanceProgress.WaitAsync(gate, CancellationToken.None);
        var cancelled = MaintenanceProgress.WaitAsync(gate, cancel.Token);
        Assert.False(waiting.IsCompleted);
        Assert.Equal(initial + 2, MaintenanceProgress.Current.Queued);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        gate.Release();
        await waiting;
        Assert.Equal(initial, MaintenanceProgress.Current.Queued);
        gate.Release();
    }

    [Fact]
    public async Task InvalidRegistryShape_IsNotAcceptedAsEmptyHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-shape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "downloads.json");
            await File.WriteAllTextAsync(path, "{}");
            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
            await Assert.ThrowsAsync<FileNotFoundException>(() => store.GetAllAsync(CancellationToken.None));
            Assert.Equal("{}", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RecoveryConflict_PreservesManualEditBackupAndJournal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "subtitle.ass");
            var backup = target + ".pna-rollback-test";
            await File.WriteAllTextAsync(target, "manual edit");
            await File.WriteAllTextAsync(backup, "old");
            var journalDirectory = Path.Combine(directory, "transactions");
            var journal = await SubtitleRecoveryJournal.BeginAsync(journalDirectory,
                new("media", "release", target, backup, target + ".pna-backup", "old-hash", "new-hash", "revision"), CancellationToken.None);
            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, Path.Combine(directory, "downloads.json"));
            await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleRecoveryJournal.RecoverAsync(journalDirectory, store, NullLogger.Instance, CancellationToken.None));
            Assert.Equal("manual edit", await File.ReadAllTextAsync(target));
            Assert.Equal("old", await File.ReadAllTextAsync(backup));
            Assert.True(File.Exists(journal));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ExhaustedStageBudget_AllowsNextStageButUserCancellationDoesNot()
    {
        var next = false;
        await DownloadMissingSubtitlesTask.RunMaintenanceStagesAsync(true, true,
            async () => Assert.False(await StageTimeBudget.RunAsync(token => Task.Delay(Timeout.Infinite, token), TimeSpan.FromMilliseconds(15), CancellationToken.None)),
            () => { next = true; return Task.CompletedTask; }, CancellationToken.None);
        Assert.True(next);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StageTimeBudget.RunAsync(token => Task.Delay(1, token), TimeSpan.FromSeconds(1), cancelled.Token));
    }

    [Fact]
    public async Task StalledBodyAfterSuccessfulHeaders_HasBoundedTimeout()
    {
        var handler = new BodyHandler();
        var client = new AzureIllusionApiClient(new HttpClient(handler), new ApiRequestGate(TimeProvider.System, TimeSpan.Zero), null!,
            NullLogger<AzureIllusionApiClient>.Instance, new PluginConfiguration { ApiKey = "test-only", RequestTimeoutSeconds = 5 });
        await using var source = await client.DownloadSubtitleAsync("release", CancellationToken.None);
        var error = await Assert.ThrowsAsync<AzureIllusionApiException>(() => AzureIllusionSubtitleProvider.BufferVerifiedSubtitleAsync(source, null, CancellationToken.None));
        Assert.Equal("API_TIMEOUT", error.Code);
    }

    private sealed class BodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
    }
    private sealed class StalledStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await Task.Delay(Timeout.Infinite, token); return 0; }
    }

    [Fact]
    public async Task UnreadableRegistry_IsNotQuarantinedOrReset()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-locked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "downloads.json");
            await File.WriteAllTextAsync(path, "{\"downloads\":[]}");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
                await Assert.ThrowsAnyAsync<IOException>(() => store.GetAllAsync(CancellationToken.None));
                Assert.Empty(Directory.GetFiles(directory, "*.corrupt-*"));
            }
            Assert.Contains("downloads", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PartialConfigurationEdit_PreservesUnrelatedFieldsAndChangesRevision()
    {
        var before = new PluginConfiguration { ApiKey = "test-only", IgnoredGroupSlugs = ["first"] };
        var revised = ConfigurationEdits.Apply(before, new Dictionary<string, JsonElement>
        { ["IgnoredGroupSlugs"] = JsonSerializer.SerializeToElement(new[] { "second" }) });
        Assert.Equal("test-only", revised.ApiKey);
        Assert.Equal(new[] { "first" }, before.IgnoredGroupSlugs);
        Assert.Equal(new[] { "second" }, revised.IgnoredGroupSlugs);
        Assert.NotEqual(ConfigurationEdits.Revision(before), ConfigurationEdits.Revision(revised));
        Assert.Throws<InvalidDataException>(() => ConfigurationEdits.Apply(before, new Dictionary<string, JsonElement>
        { ["RequestTimeoutSeconds"] = JsonSerializer.SerializeToElement(0) }));
    }

    [Fact]
    public void Mapping_TargetSeasonAndAbsoluteOffsetAreExplicitAndValidated()
    {
        using var json = JsonDocument.Parse("{\"anilistId\":\"111321\",\"episodeStart\":26,\"episodeEnd\":38,\"episodeOffset\":-25,\"targetSeason\":1}");
        var match = AniListResolver.ParseMappingValue(json.RootElement, 26);
        Assert.Equal(1, match!.Episode);
        Assert.Equal(1, match.Season);
        Assert.Null(AniListResolver.ParseMappingValue(json.RootElement, 25));
    }

    [Fact]
    public void SameGroupDifferentReleases_HaveDistinctStableFilenames()
    {
        var one = AzureIllusionSubtitleProvider.BuildStoredLanguage("pl", "INNE", "release-a");
        var two = AzureIllusionSubtitleProvider.BuildStoredLanguage("pl", "INNE", "release-b");
        Assert.NotEqual(one, two);
        Assert.Equal(one, AzureIllusionSubtitleProvider.BuildStoredLanguage("pl", "INNE", "release-a"));
        Assert.Equal("pol.INNE", AzureIllusionSubtitleProvider.BuildStoredLanguage("pl", "INNE"));
    }

    [Fact]
    public async Task SearchCheckpoint_RotatesAndSurvivesRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-checkpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "checkpoint.json");
            var store = new SearchCheckpointStore(path);
            store.Record("already-searched", false);
            store.Record("failed", true);
            await store.SaveAsync(CancellationToken.None);
            var reopened = new SearchCheckpointStore(path);
            await reopened.LoadAsync(CancellationToken.None);
            Assert.False(reopened.IsDue("already-searched"));
            Assert.False(reopened.IsDue("failed"));
            Assert.True(reopened.IsDue("not-yet-searched"));
            Assert.True(reopened.LastChecked("not-yet-searched") < reopened.LastChecked("already-searched"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedReplacement_RecoversOldFileOrKeepsCommittedNewFile(bool committed)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "subtitle.ass");
            var backup = target + ".pna-rollback-test";
            await File.WriteAllTextAsync(target, "new");
            await File.WriteAllTextAsync(backup, "old");
            var oldHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("old")));
            var newHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("new")));
            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, Path.Combine(directory, "downloads.json"));
            await store.MarkDownloadedAsync(new ManagedSubtitleDownload("media", "release", committed ? newHash : oldHash,
                DateTimeOffset.UtcNow, SourceRevision: committed ? "new-revision" : "old-revision"), CancellationToken.None);
            var journalDirectory = Path.Combine(directory, "transactions");
            await SubtitleRecoveryJournal.BeginAsync(journalDirectory, new("media", "release", target, backup, target + ".pna-backup", oldHash, newHash, "new-revision"), CancellationToken.None);
            await SubtitleRecoveryJournal.RecoverAsync(journalDirectory, store, NullLogger.Instance, CancellationToken.None);
            Assert.Equal(committed ? "new" : "old", await File.ReadAllTextAsync(target));
            Assert.Empty(Directory.GetFiles(journalDirectory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task NewerRegistryVersion_IsNeverDowngradedToOlderBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-future-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "downloads.json");
            await File.WriteAllTextAsync(path, "{\"schemaVersion\":99,\"downloads\":[]}");
            await File.WriteAllTextAsync(path + ".bak", "{\"downloads\":[]}");
            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, path);
            await Assert.ThrowsAsync<NotSupportedException>(() => store.GetAllAsync(CancellationToken.None));
            Assert.Contains("99", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
