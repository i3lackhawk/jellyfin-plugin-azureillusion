using System.Reflection;
using Jellyfin.Plugin.AzureIllusion.State;
using Jellyfin.Plugin.AzureIllusion.Subtitles;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class SubtitleLabelTests
{
    private static readonly string MediaPath = Path.Combine(Path.GetTempPath(), "Episode.mkv");
    private static ManagedSubtitleDownload Record(string? group = "Mioro-Subs", bool legacy = false)
    {
        var stored = AzureIllusionSubtitleProvider.BuildStoredLanguage("pl", group, legacy ? null : "release");
        return new(AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(MediaPath), "release", "hash", DateTimeOffset.UtcNow,
            MediaPath, "pl", stored, "ass", group, LocalPath: Path.ChangeExtension(MediaPath, stored.ToLowerInvariant() + ".ass"));
    }
    private static MediaStream Stream(ManagedSubtitleDownload record, string? title = null) => new()
    {
        Type = MediaStreamType.Subtitle, IsExternal = true, Path = record.LocalPath!, Language = "pol",
        Title = title ?? record.StoredLanguage![4..].ToLowerInvariant(), Codec = "ass", Index = 5,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HashAndLegacyNames_GetHumanLabelWithoutChangingPathOrFlags(bool legacy)
    {
        var record = Record(legacy: legacy);
        var stream = Stream(record);
        var path = stream.Path;
        stream.IsForced = true;
        stream.IsHearingImpaired = true;
        stream.IsDefault = true;
        Assert.Equal(1, ManagedSubtitleLabels.Apply(MediaPath, [stream], [record]));
        Assert.Equal("Mioro-Subs", stream.Title);
        Assert.Equal("pol", stream.Language);
        Assert.Contains("Mioro-Subs", stream.DisplayTitle);
        Assert.DoesNotContain(".pna-", stream.DisplayTitle);
        Assert.Contains("ASS", stream.DisplayTitle);
        Assert.Equal(path, stream.Path);
        Assert.Equal("ass", stream.Codec);
        Assert.Equal(5, stream.Index);
        Assert.True(stream.IsForced && stream.IsHearingImpaired && stream.IsDefault);
        Assert.Equal(0, ManagedSubtitleLabels.Apply(MediaPath, [stream], [record]));
    }

    [Fact]
    public void PendingNumberedMetadataFile_IsLabeledWithoutChangingDownloadState()
    {
        var record = Record() with { Pending = true, LocalPath = null };
        var path = Path.Combine(Path.GetTempPath(), "metadata", "Episode." + record.StoredLanguage!.ToLowerInvariant() + ".0.ass");
        var stream = new MediaStream { Type = MediaStreamType.Subtitle, IsExternal = true, Path = path,
            Title = record.StoredLanguage[4..].ToLowerInvariant() + ".0", Language = record.StoredLanguage };
        Assert.Equal(1, ManagedSubtitleLabels.Apply(MediaPath, [stream], [record]));
        Assert.Equal("Mioro-Subs", stream.Title);
        Assert.Equal("pol", stream.Language);
        Assert.True(record.Pending);
        Assert.Null(record.LocalPath);
        Assert.Equal(path, stream.Path);
    }

    [Theory]
    [InlineData("Żółw Subs")]
    [InlineData("帰還者 🐉")]
    [InlineData(null)]
    public void LabelsPreserveGroupSpellingAndUseReadableFallback(string? group)
    {
        var record = Record(group);
        var stream = Stream(record);
        stream.Title = null;
        Assert.Equal(1, ManagedSubtitleLabels.Apply(MediaPath, [stream], [record]));
        Assert.Equal(group ?? "Polskie Napisy Anime", stream.Title);
    }

    [Fact]
    public void ManualTitle_IsNotOverwritten()
    {
        var record = Record();
        var stream = Stream(record, "Moja poprawiona wersja");
        Assert.Equal(0, ManagedSubtitleLabels.Apply(MediaPath, [stream], [record]));
        Assert.Equal("Moja poprawiona wersja", stream.Title);
    }

    [Fact]
    public void EmbeddedOtherProviderAndAmbiguousStreams_AreUntouched()
    {
        var record = Record();
        var embedded = Stream(record); embedded.IsExternal = false;
        var audio = Stream(record); audio.Type = MediaStreamType.Audio;
        var other = Stream(record); other.Path = Path.Combine(Path.GetTempPath(), "other.pol.ass");
        var unknown = Stream(record); unknown.Path = Path.Combine(Path.GetTempPath(), "other.pna-123456789012.ass");
        foreach (var stream in new[] { embedded, audio, other, unknown })
            Assert.Equal(0, ManagedSubtitleLabels.Apply(MediaPath, [stream], [record]));
        var ambiguous = Stream(record);
        Assert.Equal(0, ManagedSubtitleLabels.Apply(MediaPath, [ambiguous], [record, record with { ReleaseId = "other" }]));
    }

    [Fact]
    public void ConflictingLanguage_IsNotGuessed()
    {
        var record = Record();
        var stream = Stream(record); stream.Language = "spa";
        Assert.Equal(0, ManagedSubtitleLabels.Apply(MediaPath, [stream], [record]));
        Assert.Equal("spa", stream.Language);
    }

    [Fact]
    public async Task RegisteredService_SavesOnlyChangedMetadataAndReappliesAfterRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "pna-label-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, Path.Combine(root, "downloads.json"));
            var video = new Video { Path = MediaPath, Id = Guid.NewGuid() };
            var record = Record() with { MediaId = video.Id };
            await store.MarkDownloadedAsync(record, default);
            var writes = store.WriteCount;
            var repository = new StreamRepository { Streams = [Stream(record), new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "hevc" }] };
            var library = DispatchProxy.Create<ILibraryManager, LibraryProxy>();
            using var service = new SubtitleLabelService(library, repository, store, NullLogger<SubtitleLabelService>.Instance);
            Assert.Equal(1, await service.RefreshAsync(video, default));
            Assert.Equal(1, repository.Saves);
            Assert.Equal("Mioro-Subs", repository.Streams[0].Title);
            Assert.Equal("hevc", repository.Streams[1].Codec);
            Assert.Equal(0, await service.RefreshAsync(video, default));
            Assert.Equal(1, repository.Saves);
            repository.Streams[0].Title = record.StoredLanguage![4..].ToLowerInvariant();
            Assert.Equal(1, await service.RefreshAsync(video, default));
            Assert.Equal(2, repository.Saves);
            Assert.Equal(writes, store.WriteCount);
            ((LibraryProxy)(object)library).ScanRunning = true;
            repository.Streams[0].Title = record.StoredLanguage[4..];
            Assert.Equal(0, await service.RefreshAsync(video, default));
            Assert.Equal(2, repository.Saves);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LabelService_IsRegisteredAsHostedService()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(SubtitleLabelService));
    }

    [Fact]
    public async Task LibraryEvent_IsProcessedInBackgroundAndUnsubscribedOnStop()
    {
        var root = Path.Combine(Path.GetTempPath(), "pna-label-events-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, Path.Combine(root, "downloads.json"));
            var video = new Video { Path = MediaPath, Id = Guid.NewGuid() };
            var record = Record() with { MediaId = video.Id };
            await store.MarkDownloadedAsync(record, default);
            var repository = new StreamRepository { Streams = [Stream(record)] };
            var library = DispatchProxy.Create<ILibraryManager, LibraryProxy>();
            var proxy = (LibraryProxy)(object)library;
            using var service = new SubtitleLabelService(library, repository, store, NullLogger<SubtitleLabelService>.Instance);
            await service.StartAsync(default);
            try
            {
                Assert.Equal(2, proxy.Subscriptions.Count);
                proxy.Subscriptions["ItemUpdated"]!(null, new ItemChangeEventArgs { Item = video });
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                while (Volatile.Read(ref repository.Saves) == 0) await Task.Delay(50, timeout.Token);
                Assert.Equal("Mioro-Subs", repository.Streams[0].Title);
                Assert.Equal(1, repository.Saves);
            }
            finally { await service.StopAsync(default); }
            Assert.Empty(proxy.Subscriptions);
        }
        finally { Directory.Delete(root, true); }
    }

    public class LibraryProxy : DispatchProxy
    {
        internal bool ScanRunning;
        internal Dictionary<string, EventHandler<ItemChangeEventArgs>?> Subscriptions = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name == "get_IsScanRunning") return ScanRunning;
            if (name.StartsWith("add_", StringComparison.Ordinal))
                Subscriptions[name[4..]] = (EventHandler<ItemChangeEventArgs>?)args![0];
            if (name.StartsWith("remove_", StringComparison.Ordinal)) Subscriptions.Remove(name[7..]);
            return null;
        }
    }
    private sealed class StreamRepository : IMediaStreamRepository
    {
        internal IReadOnlyList<MediaStream> Streams = [];
        internal int Saves;
        public IReadOnlyList<MediaStream> GetMediaStreams(MediaStreamQuery query) => Streams;
        public IReadOnlyList<string> GetMediaStreamLanguages(MediaStreamType mediaStreamType) => [];
        public void SaveMediaStreams(Guid id, IReadOnlyList<MediaStream> streams, CancellationToken cancellationToken)
        { Streams = streams; Saves++; }
    }
}
