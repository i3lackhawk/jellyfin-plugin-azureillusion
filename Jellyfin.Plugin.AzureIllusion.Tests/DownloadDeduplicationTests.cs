using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.AzureIllusion.Api;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using Jellyfin.Plugin.AzureIllusion.Matching;
using Jellyfin.Plugin.AzureIllusion.State;
using Jellyfin.Plugin.AzureIllusion.Subtitles;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class DownloadDeduplicationTests
{
    [Fact]
    public async Task TwoRunsAndRestart_TransferAndWriteOnlyOnce()
    {
        using var fixture = new Fixture();
        Assert.Equal(1, await fixture.Run());
        Assert.Equal(0, await fixture.Run());
        fixture.Restart();
        Assert.Equal(0, await fixture.Run());
        Assert.Equal(1, fixture.Api.Downloads);
        Assert.Equal(1, fixture.Writes);
        var record = Assert.Single(await fixture.Store.GetAllAsync(default));
        Assert.False(record.Pending);
        Assert.Equal(fixture.LocalPath(), record.LocalPath);
        Assert.Equal(fixture.Hash, await ScheduledTasks.UpdateDownloadedSubtitlesTask.ComputeSha256Async(record.LocalPath!, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualDownload_IsRecoveredBeforeSearch_EvenWithUpdatesDisabled(bool updates)
    {
        using var fixture = new Fixture();
        fixture.Configuration.EnableSubtitleUpdates = updates;
        var result = Assert.Single(await fixture.Provider.Search(fixture.Request(false), default));
        await fixture.Save(result.Id, default);
        Assert.True(Assert.Single(await fixture.Store.GetAllAsync(default)).Pending);
        Assert.Equal(0, await fixture.Run());
        Assert.Equal(1, fixture.Api.Downloads);
        Assert.Contains(fixture.LastLocalItems, x => x.Result == "confirmed-local");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedAfterDiskWrite_NextRunDoesNotDownloadAgain(bool cancellation)
    {
        using var fixture = new Fixture();
        var result = Assert.Single(await fixture.Provider.Search(fixture.Request(false), default));
        using var cancel = new CancellationTokenSource();
        var payload = SubtitleIdCodec.Decode(result.Id);
        async Task Interrupted(CancellationToken token)
        {
            await fixture.Save(result.Id, token);
            if (cancellation) { cancel.Cancel(); token.ThrowIfCancellationRequested(); }
            throw new IOException("simulated stop after Jellyfin saved the file");
        }
        await Assert.ThrowsAnyAsync<Exception>(() => new LocalDownloadReconciler(fixture.Store)
            .DownloadIfMissingAsync(fixture.Media, payload, Interrupted, cancel.Token));
        fixture.Restart();
        Assert.Equal(0, await fixture.Run());
        Assert.Equal(1, fixture.Api.Downloads);
        Assert.Equal(1, fixture.Writes);
    }

    [Fact]
    public async Task MovedVideo_RebindsByIdAndHashBeforeTransfer()
    {
        using var fixture = new Fixture();
        Assert.Equal(1, await fixture.Run());
        var oldPath = fixture.LocalPath();
        fixture.Media = fixture.Media with { Path = Path.Combine(fixture.Root, "renamed.mkv") };
        File.Move(oldPath, fixture.LocalPath());
        var recovered = await new LocalDownloadReconciler(fixture.Store).ReconcileAsync(fixture.Media, default);
        Assert.Contains(recovered.Items, x => x.Result == "rebound-local");
        Assert.Equal(0, await fixture.Run());
        Assert.Equal(1, fixture.Api.Downloads);
        Assert.Equal(fixture.Media.Path, Assert.Single(await fixture.Store.GetAllAsync(default)).MediaPath);
    }

    [Fact]
    public async Task ChecksumIndex_TriesExistingSecondCopyAfterRestart()
    {
        using var fixture = new Fixture();
        await File.WriteAllBytesAsync(fixture.LocalPath(), fixture.Api.Content);
        var original = fixture.Record() with { Pending = false };
        await fixture.Store.MarkDownloadedAsync(original with { ReleaseId = "missing", LocalPath = Path.Combine(fixture.Root, "missing.ass") }, default);
        await fixture.Store.MarkDownloadedAsync(original with { ReleaseId = "present", LocalPath = fixture.LocalPath() }, default);
        fixture.Restart();
        Assert.True(await fixture.Store.ContainsAsync(original.MediaKey, "third-id", fixture.Hash, default));
        Assert.Equal(0, await fixture.Run());
        Assert.Equal(0, fixture.Api.Downloads);
    }

    [Fact]
    public async Task MissingFileAndPartialSave_CanRetryWithoutOverwritingThePartialFile()
    {
        using var fixture = new Fixture();
        await fixture.Store.MarkDownloadedAsync(fixture.Record(), default);
        Assert.Equal(1, await fixture.Run());
        File.Delete(fixture.LocalPath());
        Assert.Equal(1, await fixture.Run());
        Assert.Equal(2, fixture.Api.Downloads);
        // Preserve the unconfirmed file; Jellyfin writes the complete retry with a numbered name.
        fixture.Api.Content = Encoding.UTF8.GetBytes("complete source for partial retry");
        await fixture.Store.MarkDownloadedAsync(fixture.Record() with { ReleaseId = "partial", StoredLanguage = fixture.StoredLanguage("partial") }, default);
        await File.WriteAllTextAsync(fixture.LocalPath("partial"), "partial");
        fixture.Api.ReleaseId = "partial";
        Assert.Equal(1, await fixture.Run());
        Assert.Equal("partial", await File.ReadAllTextAsync(fixture.LocalPath("partial")));
        Assert.Contains(fixture.LastLocalItems, x => x.Result == "incomplete-local");
        var retried = Assert.Single(await fixture.Store.GetAllAsync(default), x => x.ReleaseId == "partial");
        Assert.False(retried.Pending);
        Assert.NotEqual(fixture.LocalPath("partial"), retried.LocalPath);
        Assert.Equal(fixture.Hash, await ScheduledTasks.UpdateDownloadedSubtitlesTask.ComputeSha256Async(retried.LocalPath!, default));
        Assert.Equal(0, await fixture.Run());
        Assert.Equal(3, fixture.Api.Downloads);
    }

    [Fact]
    public async Task AmbiguousMovedCopies_AreNotAdoptedOrOverwritten()
    {
        using var fixture = new Fixture();
        var one = Path.Combine(fixture.Root, "one." + fixture.StoredLanguage() + ".ass");
        var two = Path.Combine(fixture.Root, "two." + fixture.StoredLanguage() + ".ass");
        await File.WriteAllBytesAsync(one, fixture.Api.Content);
        await File.WriteAllBytesAsync(two, fixture.Api.Content);
        await fixture.Store.MarkDownloadedAsync(fixture.Record() with { MediaKey = "old", MediaPath = "/old/movie.mkv" }, default);
        fixture.Media = fixture.Media with { SubtitleFiles = [one, two] };
        var local = await new LocalDownloadReconciler(fixture.Store).ReconcileAsync(fixture.Media, default);
        Assert.Contains("release", local.BlockedReleases);
        Assert.Equal("local-conflict", await new LocalDownloadReconciler(fixture.Store).DownloadIfMissingAsync(
            fixture.Media, fixture.Payload(), _ => throw new Exception("must not download"), default));
        Assert.Equal(0, fixture.Api.Downloads);
    }

    [Fact]
    public async Task DisconnectedStorage_BlocksInsteadOfTreatingFileAsMissing()
    {
        using var fixture = new Fixture();
        await fixture.Store.MarkDownloadedAsync(fixture.Record(), default);
        var missingDirectory = Path.Combine(fixture.Root, "offline");
        fixture.Media = fixture.Media with { Path = Path.Combine(missingDirectory, "movie.mkv") };
        var result = await new LocalDownloadReconciler(fixture.Store).DownloadIfMissingAsync(fixture.Media,
            fixture.Payload(), _ => throw new Exception("must not download"), default);
        Assert.Equal("local-unavailable", result);
        Assert.True(Assert.Single(await fixture.Store.GetAllAsync(default)).Pending);
    }

    [Fact]
    public async Task Simulation_DoesNotChangeRegistry_AndManualSearchStillAllowsChoice()
    {
        using var fixture = new Fixture();
        await fixture.Store.MarkDownloadedAsync(fixture.Record(), default);
        await File.WriteAllBytesAsync(fixture.LocalPath(), fixture.Api.Content);
        var writesBefore = fixture.Store.WriteCount;
        using (PluginAutomaticSearchContext.Enter(simulation: true))
            Assert.Empty(await fixture.Provider.Search(fixture.Request(true), default));
        Assert.Equal(writesBefore, fixture.Store.WriteCount);
        Assert.True(Assert.Single(await fixture.Store.GetAllAsync(default)).Pending);
        Assert.Single(await fixture.Provider.Search(fixture.Request(false), default));
    }

    [Fact]
    public async Task ConcurrentStaleResults_OnlyOneTransferAndSave()
    {
        using var fixture = new Fixture();
        var result = Assert.Single(await fixture.Provider.Search(fixture.Request(false), default));
        var payload = SubtitleIdCodec.Decode(result.Id);
        var one = new LocalDownloadReconciler(fixture.Store);
        var two = new LocalDownloadReconciler(fixture.Store);
        var results = await Task.WhenAll(one.DownloadIfMissingAsync(fixture.Media, payload, t => fixture.Save(result.Id, t), default),
            two.DownloadIfMissingAsync(fixture.Media, payload, t => fixture.Save(result.Id, t), default));
        Assert.Single(results, x => x == "downloaded");
        Assert.Single(results, x => x == "already-downloaded");
        Assert.Equal(1, fixture.Api.Downloads);
        Assert.Equal(1, fixture.Writes);
    }

    [Fact]
    public async Task DifferentEditionIsAllowed_AndChangedConfirmedFileIsNotOverwritten()
    {
        using var fixture = new Fixture();
        Assert.Equal(1, await fixture.Run());
        var first = fixture.LocalPath();
        await File.WriteAllTextAsync(first, "manual edits");
        Assert.Equal(0, await fixture.Run());
        fixture.Api.ReleaseId = "second";
        fixture.Api.Content = Encoding.UTF8.GetBytes("another translation");
        Assert.Equal(1, await fixture.Run());
        Assert.Equal("manual edits", await File.ReadAllTextAsync(first));
        Assert.Equal(2, fixture.Api.Downloads);
    }

    [Fact]
    public async Task MissingConfirmedFile_CanFetchChangedSourceWithoutKeepingOldChecksum()
    {
        using var fixture = new Fixture();
        Assert.Equal(1, await fixture.Run());
        File.Delete(fixture.LocalPath());
        fixture.Api.Content = Encoding.UTF8.GetBytes("new source version after local deletion");
        Assert.Equal(1, await fixture.Run());
        Assert.Equal(0, await fixture.Run());
        Assert.Equal(fixture.Hash, Assert.Single(await fixture.Store.GetAllAsync(default)).Checksum);
        Assert.Equal(2, fixture.Api.Downloads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualUpdate_DownloadsChangedVersionOnceAndPreservesBackup(bool editDuringTransfer)
    {
        using var fixture = new Fixture();
        Assert.Equal(1, await fixture.Run());
        var original = await File.ReadAllBytesAsync(fixture.LocalPath());
        var record = Assert.Single(await fixture.Store.GetAllAsync(default));
        fixture.Api.Content = Encoding.UTF8.GetBytes("updated subtitle version");
        if (editDuringTransfer) fixture.Api.BeforeFileResponse = () => File.WriteAllText(fixture.LocalPath(), "manual edit during transfer");
        var task = new ScheduledTasks.UpdateDownloadedSubtitlesTask(fixture.Client, fixture.Store, null!, null!, null!,
            NullLogger<ScheduledTasks.UpdateDownloadedSubtitlesTask>.Instance, fixture.Root);
        var current = new SubtitleRevisionItem("release", "available", fixture.Hash, fixture.Api.Content.Length, DateTimeOffset.UtcNow, "2");
        Assert.Equal(!editDuringTransfer, await task.UpdateLocalFileAsync(record, current, "sha256:" + fixture.Hash, fixture.LocalPath(), true, default));
        if (editDuringTransfer)
        {
            Assert.Equal("manual edit during transfer", await File.ReadAllTextAsync(fixture.LocalPath()));
            Assert.Equal(record.Checksum, Assert.Single(await fixture.Store.GetAllAsync(default)).Checksum);
        }
        else
        {
            Assert.Equal(original, await File.ReadAllBytesAsync(fixture.LocalPath() + ".pna-backup"));
            Assert.Equal(fixture.Hash, await ScheduledTasks.UpdateDownloadedSubtitlesTask.ComputeSha256Async(fixture.LocalPath(), default));
            Assert.Equal("sha256:" + fixture.Hash, Assert.Single(await fixture.Store.GetAllAsync(default)).SourceRevision);
            Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root, "transactions")));
        }
        Assert.Equal(0, await fixture.Run());
        Assert.Equal(2, fixture.Api.Downloads);
    }

    [Fact]
    public async Task LockedPendingFile_IsReportedUnavailableWithoutDownloading()
    {
        using var fixture = new Fixture();
        await File.WriteAllBytesAsync(fixture.LocalPath(), fixture.Api.Content);
        await fixture.Store.MarkDownloadedAsync(fixture.Record(), default);
        using var locked = new FileStream(fixture.LocalPath(), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var local = await new LocalDownloadReconciler(fixture.Store).ReconcileAsync(fixture.Media, default);
        Assert.Contains(local.Items, x => x.Result == "local-unavailable");
        Assert.Equal("local-unavailable", await new LocalDownloadReconciler(fixture.Store).DownloadIfMissingAsync(
            fixture.Media, fixture.Payload(), _ => throw new Exception("must not download"), default));
    }

    [Fact]
    public async Task StaleReconciliation_CannotReplaceMoreRecentState()
    {
        using var fixture = new Fixture();
        var old = fixture.Record();
        await fixture.Store.MarkDownloadedAsync(old, default);
        await fixture.Store.MarkDownloadedAsync(old with { Checksum = "newer-state" }, default);
        Assert.False(await fixture.Store.ConfirmLocalAsync(old, old.MediaKey, fixture.Media.Path, fixture.LocalPath(), fixture.Media.Id, default));
        Assert.Equal("newer-state", Assert.Single(await fixture.Store.GetAllAsync(default)).Checksum);
    }

    [Fact]
    public async Task WaitingDownloadCancellation_DoesNotLeakMediaLock()
    {
        using var fixture = new Fixture();
        var result = Assert.Single(await fixture.Provider.Search(fixture.Request(false), default));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconciler = new LocalDownloadReconciler(fixture.Store);
        var first = reconciler.DownloadIfMissingAsync(fixture.Media, fixture.Payload(), async token =>
        {
            entered.SetResult();
            await resume.Task.WaitAsync(token);
            await fixture.Save(result.Id, token);
        }, default);
        await entered.Task;
        using var cancel = new CancellationTokenSource();
        var second = reconciler.DownloadIfMissingAsync(fixture.Media, fixture.Payload(), _ => throw new Exception("must not download"), cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        resume.SetResult();
        Assert.Equal("downloaded", await first);
        Assert.Equal("already-downloaded", await reconciler.DownloadIfMissingAsync(fixture.Media, fixture.Payload(), _ => throw new Exception("must not download"), default));
        Assert.Equal(1, fixture.Api.Downloads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task JellyfinLowercaseAndNumberedNames_AreFoundWithoutRefreshedSubtitleList(bool metadata, bool numbered)
    {
        using var fixture = new Fixture();
        fixture.UseMetadataDirectory = metadata;
        fixture.NumberedSave = numbered;
        fixture.Media = fixture.Media with { MetadataDirectory = Path.Combine(fixture.Root, "metadata") };
        Assert.Equal(1, await fixture.Run());
        fixture.Restart();
        Assert.Equal(0, await fixture.Run());
        var record = Assert.Single(await fixture.Store.GetAllAsync(default));
        Assert.Equal(fixture.LocalPath(), record.LocalPath);
        Assert.Equal(1, fixture.Api.Downloads);
        Assert.Equal(1, fixture.Writes);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "pna-dedup-" + Guid.NewGuid().ToString("N"));
        internal PluginConfiguration Configuration { get; } = new() { ApiKey = "isolated-test", ApiBaseUrl = "https://test.invalid", AllowJellyfinAutomaticSearch = true };
        internal ApiHandler Api { get; } = new();
        internal LocalMedia Media { get; set; }
        internal DownloadStateStore Store { get; private set; } = null!;
        internal AzureIllusionSubtitleProvider Provider { get; private set; } = null!;
        internal AzureIllusionApiClient Client { get; private set; } = null!;
        internal IReadOnlyList<LocalReconciliationItem> LastLocalItems { get; private set; } = [];
        internal int Writes { get; private set; }
        internal bool UseMetadataDirectory;
        internal bool NumberedSave;
        private readonly HttpClient _http;
        internal string Hash => Convert.ToHexStringLower(SHA256.HashData(Api.Content));
        internal Fixture()
        {
            Directory.CreateDirectory(Root);
            Media = new(Path.Combine(Root, "movie.mkv"), Guid.NewGuid(), []);
            _http = new HttpClient(Api);
            Restart();
        }
        internal void Restart()
        {
            Store = new(NullLogger<DownloadStateStore>.Instance, Path.Combine(Root, "downloads.json"));
            var diagnostics = new DiagnosticEventStore(NullLogger<DiagnosticEventStore>.Instance);
            var client = new AzureIllusionApiClient(_http, new ApiRequestGate(), diagnostics, NullLogger<AzureIllusionApiClient>.Instance, Configuration);
            Client = client;
            var resolver = new AniListResolver(client, new AnimeMatchCache(), null!, NullLogger<AniListResolver>.Instance, Configuration);
            Provider = new(client, resolver, Store, diagnostics, NullLogger<AzureIllusionSubtitleProvider>.Instance, Configuration);
        }
        internal string StoredLanguage(string id = "release") => AzureIllusionSubtitleProvider.BuildStoredLanguage("pl", "Test", id);
        internal string LocalPath(string id = "release") => Path.Combine(UseMetadataDirectory ? Media.MetadataDirectory! : Path.GetDirectoryName(Media.Path)!,
            Path.GetFileNameWithoutExtension(Media.Path) + "." + StoredLanguage(id).ToLowerInvariant() + (NumberedSave ? ".0" : "") + ".ass");
        internal ManagedSubtitleDownload Record() => new(AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(Media.Path), "release", Hash, DateTimeOffset.UtcNow,
            Media.Path, "pl", StoredLanguage(), "ass", "Test", "test", "1", null, null, "sha256:" + Hash, Pending: true, MediaId: Media.Id);
        internal SubtitleIdPayload Payload() => new("release", AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(Media.Path), "pl", "ass", Hash, "Test", Media.Path, "test", "1");
        internal SubtitleSearchRequest Request(bool automatic) => new() { ContentType = VideoContentType.Movie, MediaPath = Media.Path, Name = "Test", Language = "pol", IsAutomated = automatic, ProviderIds = new Dictionary<string, string> { ["AniList"] = "1" } };
        internal async Task Save(string id, CancellationToken token)
        {
            var response = await Provider.GetSubtitles(id, token);
            await using var stream = response.Stream;
            var payload = SubtitleIdCodec.Decode(id);
            Directory.CreateDirectory(Path.GetDirectoryName(LocalPath(payload.ReleaseId))!);
            var path = LocalPath(payload.ReleaseId);
            var baseName = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
            var counter = 0;
            while (File.Exists(path)) path = baseName + "." + counter++ + ".ass";
            await using var target = new FileStream(path, FileMode.CreateNew);
            await stream.CopyToAsync(target, token);
            Writes++;
        }
        internal async Task<int> Run()
        {
            using var scope = PluginAutomaticSearchContext.Enter();
            // This is also performed by the task before each save, using the stable Jellyfin ID.
            var local = await new LocalDownloadReconciler(Store).ReconcileAsync(Media, default);
            PluginAutomaticSearchContext.RecordLocal(local.Items);
            var results = await Provider.Search(Request(true), default);
            LastLocalItems = scope.LocalItems.ToArray();
            var count = 0;
            foreach (var result in results)
                if (await new LocalDownloadReconciler(Store).DownloadIfMissingAsync(Media, SubtitleIdCodec.Decode(result.Id), t => Save(result.Id, t), default) == "downloaded") count++;
            return count;
        }
        public void Dispose() { _http.Dispose(); Directory.Delete(Root, true); }
    }

    private sealed class ApiHandler : HttpMessageHandler
    {
        internal int Downloads;
        internal string ReleaseId = "release";
        internal byte[] Content = Encoding.UTF8.GetBytes("[Script Info]\nTitle: controlled subtitle\n");
        internal Action? BeforeFileResponse;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/file", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Downloads);
                BeforeFileResponse?.Invoke();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Content) });
            }
            var release = new SubtitleRelease(ReleaseId, 1, null, null, new("g", "test", "Test", null, "active"), "pl", "ass", "test.ass", Content.Length,
                Convert.ToHexStringLower(SHA256.HashData(Content)), "1", true, true, new(10, 1, 10, 10, 10), 0, DateTimeOffset.UtcNow, "/file");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { ok = true,
                data = new SubtitleSearchResult(new("a", "a", "1", new("Test", null, null), 2020, "MOVIE", 1), [release]) }), Encoding.UTF8, "application/json") });
        }
    }
}
