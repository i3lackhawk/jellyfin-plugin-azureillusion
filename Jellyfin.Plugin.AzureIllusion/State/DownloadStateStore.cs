using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.State;

/// <summary>One subtitle file downloaded and managed by Polskie Napisy Anime.</summary>
public sealed record ManagedSubtitleDownload(
    string MediaKey,
    string ReleaseId,
    string? Checksum,
    DateTimeOffset DownloadedAtUtc,
    string? MediaPath = null,
    string? Language = null,
    string? StoredLanguage = null,
    string? Format = null,
    string? GroupName = null,
    string? GroupSlug = null,
    string? AniListId = null,
    double? Season = null,
    double? Episode = null,
    string? SourceRevision = null,
    string? LocalPath = null,
    DateTimeOffset? LastCheckedAtUtc = null,
    DateTimeOffset? SourceMissingSinceUtc = null,
    DateTimeOffset? LastUpdatedAtUtc = null);

/// <summary>Builds a stable upstream revision even when an old release has no checksum.</summary>
public static class SubtitleRevision
{
    public static string? Build(string? checksum, long? sizeBytes, DateTimeOffset? publishedAt)
    {
        if (!string.IsNullOrWhiteSpace(checksum))
        {
            return string.Concat("sha256:", checksum.Trim().ToLowerInvariant());
        }

        return sizeBytes.HasValue || publishedAt.HasValue
            ? string.Concat(
                "metadata:",
                sizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                ":",
                publishedAt?.UtcDateTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")
            : null;
    }
}

/// <summary>Persists successfully downloaded releases and their safe update metadata.</summary>
public sealed class DownloadStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<DownloadStateStore> _logger;
    private readonly string? _statePath;
    private readonly HashSet<string> _releaseKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _checksumKeys = new(StringComparer.OrdinalIgnoreCase);
    private DownloadState? _cachedState;

    /// <summary>Initializes the store.</summary>
    public DownloadStateStore(ILogger<DownloadStateStore> logger)
        : this(logger, null)
    {
    }

    internal DownloadStateStore(ILogger<DownloadStateStore> logger, string? statePath)
    {
        _logger = logger;
        _statePath = statePath;
    }

    /// <summary>Returns whether this media item already received the same release.</summary>
    public async Task<bool> ContainsAsync(string mediaKey, string releaseId, string? checksum, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            return _releaseKeys.Contains(BuildKey(mediaKey, releaseId))
                || (!string.IsNullOrWhiteSpace(checksum)
                    && _checksumKeys.Contains(BuildKey(mediaKey, checksum)));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns a snapshot of all downloads known to the plugin.</summary>
    public async Task<IReadOnlyList<ManagedSubtitleDownload>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            return state.Downloads.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Records a successful legacy download atomically.</summary>
    public Task MarkDownloadedAsync(
        string mediaKey,
        string releaseId,
        string? checksum,
        CancellationToken cancellationToken)
        => MarkDownloadedAsync(
            new ManagedSubtitleDownload(mediaKey, releaseId, checksum, DateTimeOffset.UtcNow),
            cancellationToken);

    /// <summary>Records a successful managed download atomically.</summary>
    public async Task MarkDownloadedAsync(ManagedSubtitleDownload download, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            var previous = Find(state, download.MediaKey, download.ReleaseId);
            state.Downloads.RemoveAll(item => SameIdentity(item, download.MediaKey, download.ReleaseId));
            state.Downloads.Add(download with
            {
                DownloadedAtUtc = download.DownloadedAtUtc == default ? DateTimeOffset.UtcNow : download.DownloadedAtUtc,
                LocalPath = download.LocalPath ?? previous?.LocalPath,
                LastCheckedAtUtc = download.LastCheckedAtUtc ?? previous?.LastCheckedAtUtc,
                SourceMissingSinceUtc = download.SourceMissingSinceUtc,
                LastUpdatedAtUtc = download.LastUpdatedAtUtc ?? previous?.LastUpdatedAtUtc,
            });
            await SaveCoreAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Updates check status without ever removing the managed local file.</summary>
    public async Task MarkCheckedAsync(
        string mediaKey,
        string releaseId,
        string? localPath,
        bool sourceMissing,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Downloads.FindIndex(item => SameIdentity(item, mediaKey, releaseId));
            if (index < 0)
            {
                return;
            }

            var previous = state.Downloads[index];
            state.Downloads[index] = previous with
            {
                LocalPath = localPath ?? previous.LocalPath,
                LastCheckedAtUtc = DateTimeOffset.UtcNow,
                SourceMissingSinceUtc = sourceMissing
                    ? previous.SourceMissingSinceUtc ?? DateTimeOffset.UtcNow
                    : null,
            };
            await SaveCoreAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Commits metadata after a successfully replaced local file.</summary>
    public async Task MarkUpdatedAsync(
        string mediaKey,
        string releaseId,
        string? checksum,
        string? sourceRevision,
        string localPath,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Downloads.FindIndex(item => SameIdentity(item, mediaKey, releaseId));
            if (index < 0)
            {
                throw new InvalidOperationException("Managed subtitle state disappeared during update.");
            }

            var previous = state.Downloads[index];
            state.Downloads[index] = previous with
            {
                Checksum = checksum,
                SourceRevision = sourceRevision,
                LocalPath = localPath,
                LastCheckedAtUtc = DateTimeOffset.UtcNow,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow,
                SourceMissingSinceUtc = null,
            };
            await SaveCoreAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DownloadState> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (_cachedState is not null)
        {
            return _cachedState;
        }

        var path = GetPath();
        if (!File.Exists(path))
        {
            return CacheState(new DownloadState());
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<DownloadState>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? new DownloadState();
            return CacheState(state);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _logger.LogWarning(exception, "Could not read AzureIllusion download state; a clean state will be used.");
            return CacheState(new DownloadState());
        }
    }

    private async Task SaveCoreAsync(DownloadState state, CancellationToken cancellationToken)
    {
        RebuildIndexes(state);
        var path = GetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, true);
    }

    private DownloadState CacheState(DownloadState state)
    {
        _cachedState = state;
        RebuildIndexes(state);
        return state;
    }

    private void RebuildIndexes(DownloadState state)
    {
        _releaseKeys.Clear();
        _checksumKeys.Clear();
        foreach (var item in state.Downloads)
        {
            _releaseKeys.Add(BuildKey(item.MediaKey, item.ReleaseId));
            if (!string.IsNullOrWhiteSpace(item.Checksum))
            {
                _checksumKeys.Add(BuildKey(item.MediaKey, item.Checksum));
            }
        }
    }

    private static ManagedSubtitleDownload? Find(DownloadState state, string mediaKey, string releaseId)
        => state.Downloads.FirstOrDefault(item => SameIdentity(item, mediaKey, releaseId));

    private static bool SameIdentity(ManagedSubtitleDownload item, string mediaKey, string releaseId)
        => string.Equals(item.MediaKey, mediaKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.ReleaseId, releaseId, StringComparison.Ordinal);

    private static string BuildKey(string mediaKey, string value)
        => string.Concat(mediaKey, "\u001f", value);

    private string GetPath()
        => _statePath
            ?? Path.Combine(
                Plugin.Instance?.StateDirectory ?? throw new InvalidOperationException("AzureIllusion plugin is not initialized."),
                "downloads.json");

    private sealed class DownloadState
    {
        public List<ManagedSubtitleDownload> Downloads { get; set; } = [];
    }
}
