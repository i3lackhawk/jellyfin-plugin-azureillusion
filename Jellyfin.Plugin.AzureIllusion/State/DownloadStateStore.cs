using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.State;

/// <summary>Persists successfully downloaded releases to prevent duplicate automated downloads.</summary>
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

    /// <summary>Records a successful download atomically.</summary>
    public async Task MarkDownloadedAsync(string mediaKey, string releaseId, string? checksum, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            state.Downloads.RemoveAll(item =>
                string.Equals(item.MediaKey, mediaKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ReleaseId, releaseId, StringComparison.Ordinal));
            state.Downloads.Add(new DownloadRecord(mediaKey, releaseId, checksum, DateTimeOffset.UtcNow));
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

    private static string BuildKey(string mediaKey, string value)
        => string.Concat(mediaKey, "\u001f", value);

    private string GetPath()
        => _statePath
            ?? Path.Combine(
                Plugin.Instance?.StateDirectory ?? throw new InvalidOperationException("AzureIllusion plugin is not initialized."),
                "downloads.json");

    private sealed class DownloadState
    {
        public List<DownloadRecord> Downloads { get; set; } = [];
    }

    private sealed record DownloadRecord(string MediaKey, string ReleaseId, string? Checksum, DateTimeOffset DownloadedAtUtc);
}
