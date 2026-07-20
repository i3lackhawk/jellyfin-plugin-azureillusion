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
        var state = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return state.Downloads.Any(item =>
            string.Equals(item.MediaKey, mediaKey, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(item.ReleaseId, releaseId, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(checksum) && string.Equals(item.Checksum, checksum, StringComparison.OrdinalIgnoreCase))));
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

    private async Task<DownloadState> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DownloadState> ReadCoreAsync(CancellationToken cancellationToken)
    {
        var path = GetPath();
        if (!File.Exists(path))
        {
            return new DownloadState();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<DownloadState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new DownloadState();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _logger.LogWarning(exception, "Could not read AzureIllusion download state; a clean state will be used.");
            return new DownloadState();
        }
    }

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
