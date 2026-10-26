using System.Threading.Channels;
using Jellyfin.Plugin.AzureIllusion.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.Subtitles;

/// <summary>Refreshes labels after Jellyfin scans, without renaming or downloading files.</summary>
public sealed class SubtitleLabelService(
    ILibraryManager library, IMediaStreamRepository streams, DownloadStateStore store,
    ILogger<SubtitleLabelService> logger) : BackgroundService
{
    private readonly Channel<Video> _pending = Channel.CreateBounded<Video>(new BoundedChannelOptions(512)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private int _rescanNeeded;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        library.ItemAdded += OnItemChanged;
        library.ItemUpdated += OnItemChanged;
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        library.ItemAdded -= OnItemChanged;
        library.ItemUpdated -= OnItemChanged;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs args)
    {
        if (args.Item is Video video && !_pending.Writer.TryWrite(video))
            Interlocked.Exchange(ref _rescanNeeded, 1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Do not hold up startup; sweep only registered downloads, never the
            // entire video library. Existing .pna files need no rename/migration.
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _rescanNeeded, 1);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (library.IsScanRunning)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                    continue;
                }
                if (Interlocked.Exchange(ref _rescanNeeded, 0) != 0)
                {
                    try
                    {
                        var records = await store.GetAllAsync(stoppingToken).ConfigureAwait(false);
                        foreach (var group in records.Where(x => !string.IsNullOrWhiteSpace(x.MediaPath))
                            .GroupBy(x => x.MediaPath!, StringComparer.Ordinal))
                        {
                            stoppingToken.ThrowIfCancellationRequested();
                            try
                            {
                                var first = group.First();
                                var item = first.MediaId.HasValue ? library.GetItemById(first.MediaId.Value) : null;
                                var video = item as Video ?? library.FindByPath(group.Key, false) as Video;
                                if (video is not null) await RefreshAsync(video, stoppingToken).ConfigureAwait(false);
                            }
                            catch (Exception error) when (error is not OperationCanceledException)
                            { logger.LogWarning(error, "Nie udało się odświeżyć nazw zarządzanych napisów."); }
                            // Yield to newly scanned/downloaded items during a large startup sweep.
                            await DrainAsync(stoppingToken).ConfigureAwait(false);
                            await Task.Delay(10, stoppingToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        logger.LogWarning(error, "Nie udało się odczytać historii do odświeżenia nazw napisów.");
                        Interlocked.Exchange(ref _rescanNeeded, 1);
                    }
                }
                await DrainAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task DrainAsync(CancellationToken token)
    {
        for (var count = 0; count < 32 && _pending.Reader.TryRead(out var video); count++)
        {
            try { await RefreshAsync(video, token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.LogWarning(error, "Nie udało się odświeżyć nazw zarządzanych napisów."); }
        }
    }

    internal async Task<int> RefreshAsync(Video video, CancellationToken token)
    {
        // Jellyfin may replace the complete stream list during a library scan.
        // Wait for the scan to finish rather than write an older snapshot.
        if (library.IsScanRunning) { Interlocked.Exchange(ref _rescanNeeded, 1); return 0; }
        if (string.IsNullOrWhiteSpace(video.Path)) return 0;
        var records = await store.GetForMediaAsync(AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(video.Path), video.Id, token).ConfigureAwait(false);
        if (records.Count == 0) return 0;
        var metadata = streams.GetMediaStreams(new MediaStreamQuery { ItemId = video.Id });
        var changed = ManagedSubtitleLabels.Apply(video.Path, metadata, records);
        if (changed > 0)
        {
            token.ThrowIfCancellationRequested();
            streams.SaveMediaStreams(video.Id, metadata, token);
            logger.LogDebug("Zaktualizowano czytelne nazwy {Count} ścieżek napisów.", changed);
        }
        return changed;
    }
}
