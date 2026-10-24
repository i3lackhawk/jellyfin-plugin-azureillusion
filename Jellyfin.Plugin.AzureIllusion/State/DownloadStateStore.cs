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
    DateTimeOffset? LastUpdatedAtUtc = null,
    bool Pending = false,
    Guid? MediaId = null);

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
    public sealed record CheckResult(string MediaKey, string ReleaseId, string? LocalPath, bool? SourceMissing);
    internal int WriteCount { get; private set; }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<DownloadStateStore> _logger;
    private readonly string? _statePath;
    private readonly Dictionary<string, ManagedSubtitleDownload> _releaseKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ManagedSubtitleDownload>> _checksumKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ManagedSubtitleDownload>> _mediaKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, List<ManagedSubtitleDownload>> _mediaIds = new();
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
            return (_releaseKeys.TryGetValue(BuildKey(mediaKey, releaseId), out var release) && IsLocalDownloadPresent(release))
                || (!string.IsNullOrWhiteSpace(checksum)
                    && _checksumKeys.TryGetValue(BuildKey(mediaKey, checksum), out var sameContent) && sameContent.Any(IsLocalDownloadPresent));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns a snapshot of all downloads known to the plugin.</summary>
    internal async Task<IReadOnlyList<ManagedSubtitleDownload>> GetForMediaAsync(string mediaKey, Guid? mediaId, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            _ = await ReadCoreAsync(token).ConfigureAwait(false);
            return (_mediaKeys.GetValueOrDefault(mediaKey) ?? [])
                .Concat(mediaId.HasValue ? _mediaIds.GetValueOrDefault(mediaId.Value) ?? [] : [])
                .Distinct().ToArray();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Commits a verified local file only if its source record is still current.</summary>
    internal async Task<bool> ConfirmLocalAsync(ManagedSubtitleDownload original, string mediaKey, string mediaPath,
        string localPath, Guid? mediaId, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = CloneState(await ReadCoreAsync(token).ConfigureAwait(false));
            var index = state.Downloads.FindIndex(x => SameIdentity(x, original.MediaKey, original.ReleaseId));
            if (index < 0 || state.Downloads[index] != original
                || state.Downloads.Where((_, i) => i != index).Any(x => SameIdentity(x, mediaKey, original.ReleaseId))) return false;
            state.Downloads[index] = original with { MediaKey = mediaKey, MediaPath = mediaPath,
                LocalPath = localPath, MediaId = mediaId ?? original.MediaId, Pending = false };
            await SaveCoreAsync(state, token).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
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
            var state = CloneState(await ReadCoreAsync(cancellationToken).ConfigureAwait(false));
            var previous = Find(state, download.MediaKey, download.ReleaseId);
            if (download.Pending && previous is { Pending: false } && IsLocalDownloadPresent(previous)) return;
            state.Downloads.RemoveAll(item => SameIdentity(item, download.MediaKey, download.ReleaseId));
            state.Downloads.Add(download with
            {
                DownloadedAtUtc = download.DownloadedAtUtc == default ? DateTimeOffset.UtcNow : download.DownloadedAtUtc,
                Checksum = download.Checksum ?? previous?.Checksum,
                SourceRevision = download.SourceRevision ?? previous?.SourceRevision,
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

    public async Task RemovePendingAsync(string mediaKey, string releaseId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = CloneState(await ReadCoreAsync(cancellationToken).ConfigureAwait(false));
            if (state.Downloads.RemoveAll(item => item.Pending && SameIdentity(item, mediaKey, releaseId)) > 0)
                await SaveCoreAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
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
            var state = CloneState(await ReadCoreAsync(cancellationToken).ConfigureAwait(false));
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

    /// <summary>Rotates an attempted entry without changing its file or source status.</summary>
    public async Task MarkAttemptedAsync(string mediaKey, string releaseId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = CloneState(await ReadCoreAsync(cancellationToken).ConfigureAwait(false));
            var index = state.Downloads.FindIndex(item => SameIdentity(item, mediaKey, releaseId));
            if (index < 0)
            {
                return;
            }

            state.Downloads[index] = state.Downloads[index] with { LastCheckedAtUtc = DateTimeOffset.UtcNow };
            await SaveCoreAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Merges check-only metadata with the latest state, preserving concurrent downloads.</summary>
    public async Task MarkCheckedBatchAsync(IReadOnlyList<CheckResult> results, CancellationToken cancellationToken)
    {
        if (results.Count == 0) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = CloneState(await ReadCoreAsync(cancellationToken).ConfigureAwait(false));
            var changes = new Dictionary<string, CheckResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var result in results) changes[BuildKey(result.MediaKey, result.ReleaseId)] = result;
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < state.Downloads.Count; i++)
            {
                var item = state.Downloads[i];
                if (!changes.TryGetValue(BuildKey(item.MediaKey, item.ReleaseId), out var change)) continue;
                state.Downloads[i] = item with
                {
                    LastCheckedAtUtc = now,
                    LocalPath = change.LocalPath ?? item.LocalPath,
                    SourceMissingSinceUtc = change.SourceMissing switch
                    {
                        true => item.SourceMissingSinceUtc ?? now,
                        false => null,
                        null => item.SourceMissingSinceUtc,
                    },
                };
            }
            await SaveCoreAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task RebindAsync(ManagedSubtitleDownload original, string mediaKey, string mediaPath, string localPath, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = CloneState(await ReadCoreAsync(token).ConfigureAwait(false));
            var index = state.Downloads.FindIndex(x => SameIdentity(x, original.MediaKey, original.ReleaseId));
            if (index < 0 || state.Downloads[index].Checksum != original.Checksum
                || state.Downloads.Any(x => SameIdentity(x, mediaKey, original.ReleaseId))) return;
            state.Downloads[index] = state.Downloads[index] with { MediaKey = mediaKey, MediaPath = mediaPath, LocalPath = localPath };
            await SaveCoreAsync(state, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
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
            var state = CloneState(await ReadCoreAsync(cancellationToken).ConfigureAwait(false));
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
        if (!ExistsStrict(path) && !ExistsStrict(path + ".bak"))
        {
            return CacheState(new DownloadState());
        }

        try
        {
            var state = await ReadValidatedAsync(path, cancellationToken).ConfigureAwait(false);
            return CacheState(state);
        }
        catch (Exception exception) when (exception is JsonException or FileNotFoundException)
        {
            // An unavailable disk is not evidence of corrupt JSON. Never silently reset history.
            var backup = await ReadValidatedAsync(path + ".bak", cancellationToken).ConfigureAwait(false);
            if (File.Exists(path)) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            _logger.LogError(exception, "Przywrócono rejestr z ostatniej poprawnej kopii. Oryginał zachowano.");
            File.Copy(path + ".bak", path, true);
            return CacheState(backup);
        }
    }

    private static async Task<DownloadState> ReadValidatedAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var state = await JsonSerializer.DeserializeAsync<DownloadState>(stream, JsonOptions, token).ConfigureAwait(false);
        if (state is { SchemaVersion: > 2 }) throw new NotSupportedException("Rejestr pochodzi z nowszej wersji dodatku; nie zostanie nadpisany.");
        if (state is null || state.Downloads is null || state.SchemaVersion < 1
            || state.Downloads.Any(x => x is null || string.IsNullOrWhiteSpace(x.MediaKey) || string.IsNullOrWhiteSpace(x.ReleaseId)))
            throw new JsonException("Nieprawidłowy lub nieobsługiwany rejestr napisów.");
        return state;
    }

    private static bool ExistsStrict(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private async Task SaveCoreAsync(DownloadState state, CancellationToken cancellationToken)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var path = GetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        state.SchemaVersion = 2;
        try
        {
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        if (File.Exists(path)) File.Replace(temporaryPath, path, path + ".bak", true);
        else File.Move(temporaryPath, path);
        WriteCount++;
        CacheState(state);
        TaskOperationMetrics.State(watch.ElapsedMilliseconds);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Preserve the committed state if temporary cleanup fails. */ }
        }
    }

    private static DownloadState CloneState(DownloadState state)
        => new() { Downloads = [.. state.Downloads] };

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
        _mediaKeys.Clear();
        _mediaIds.Clear();
        foreach (var item in state.Downloads)
        {
            if (!_mediaKeys.TryGetValue(item.MediaKey, out var media)) _mediaKeys[item.MediaKey] = media = [];
            media.Add(item);
            if (item.MediaId.HasValue)
            {
                if (!_mediaIds.TryGetValue(item.MediaId.Value, out var byId)) _mediaIds[item.MediaId.Value] = byId = [];
                byId.Add(item);
            }
            if (item.Pending) continue;
            _releaseKeys.TryAdd(BuildKey(item.MediaKey, item.ReleaseId), item);
            if (!string.IsNullOrWhiteSpace(item.Checksum))
            {
                var key = BuildKey(item.MediaKey, item.Checksum);
                if (!_checksumKeys.TryGetValue(key, out var sameContent)) _checksumKeys[key] = sameContent = [];
                sameContent.Add(item);
            }
        }
    }

    private static bool IsLocalDownloadPresent(ManagedSubtitleDownload item)
        => string.IsNullOrWhiteSpace(item.LocalPath) || LocalDownloadReconciler.ExistsStrict(item.LocalPath);

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
        public int SchemaVersion { get; set; } = 1;
        [System.Text.Json.Serialization.JsonRequired]
        public List<ManagedSubtitleDownload> Downloads { get; set; } = [];
    }
}
