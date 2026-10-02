using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.State;

/// <summary>A bounded, administrator-only record of plugin warnings and errors.</summary>
public sealed record PluginDiagnosticEvent(
    DateTimeOffset AtUtc,
    string Severity,
    string Code,
    string Message,
    string? Reference);

/// <summary>Recent diagnostic events and their seven-day aggregate counts.</summary>
public sealed record PluginDiagnosticsSnapshot(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyDictionary<string, int> CountsLastSevenDays,
    IReadOnlyList<PluginDiagnosticEvent> RecentEvents);

/// <summary>Stores aggregated issues without reading or exposing the full Jellyfin log.</summary>
public sealed class DiagnosticEventStore
{
    public const int MaximumEvents = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<DiagnosticEventStore> _logger;
    private readonly string? _explicitPath;
    private DiagnosticState? _cached;
    private int _writeWarningLogged;

    public DiagnosticEventStore(ILogger<DiagnosticEventStore> logger)
        : this(logger, null)
    {
    }

    internal DiagnosticEventStore(ILogger<DiagnosticEventStore> logger, string? path)
    {
        _logger = logger;
        _explicitPath = path;
    }

    /// <summary>Records an issue; diagnostics never interrupt subtitle operations.</summary>
    public async Task TryRecordAsync(
        string severity,
        string code,
        string message,
        string? reference,
        CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var state = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                var day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                if (!state.CountsByDay.TryGetValue(day, out var counts))
                {
                    counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    state.CountsByDay[day] = counts;
                }

                counts[code] = counts.GetValueOrDefault(code) + 1;
                state.Events.Add(new PluginDiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    severity,
                    code,
                    message,
                    reference is { Length: > 240 } ? reference[..240] : reference));
                if (state.Events.Count > MaximumEvents)
                {
                    state.Events.RemoveRange(0, state.Events.Count - MaximumEvents);
                }

                var oldestDay = DateTimeOffset.UtcNow.AddDays(-6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                foreach (var oldDay in state.CountsByDay.Keys.Where(key => string.CompareOrdinal(key, oldestDay) < 0).ToArray())
                {
                    state.CountsByDay.Remove(oldDay);
                }

                await SaveCoreAsync(state, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Diagnostics are best-effort during task cancellation.
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (Interlocked.Exchange(ref _writeWarningLogged, 1) == 0)
            {
                _logger.LogWarning(exception, "Nie udało się zapisać diagnostyki dodatku; operacje napisów działają dalej.");
            }
        }
    }

    /// <summary>Returns seven-day counters and up to 500 latest issues.</summary>
    public async Task<PluginDiagnosticsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            var oldestDay = DateTimeOffset.UtcNow.AddDays(-6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var day in state.CountsByDay.Where(pair => string.CompareOrdinal(pair.Key, oldestDay) >= 0))
            {
                foreach (var pair in day.Value)
                {
                    counts[pair.Key] = counts.GetValueOrDefault(pair.Key) + pair.Value;
                }
            }

            return new PluginDiagnosticsSnapshot(
                DateTimeOffset.UtcNow,
                counts,
                state.Events.Where(item => item.AtUtc >= DateTimeOffset.UtcNow.AddDays(-7)).Reverse().ToArray());
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DiagnosticState> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var path = GetPath();
        if (!File.Exists(path))
        {
            return _cached = new DiagnosticState();
        }

        await using var stream = File.OpenRead(path);
        return _cached = await JsonSerializer.DeserializeAsync<DiagnosticState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Plik diagnostyki jest pusty.");
    }

    private async Task SaveCoreAsync(DiagnosticState state, CancellationToken cancellationToken)
    {
        var path = GetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = string.Concat(path, ".tmp-", Guid.NewGuid().ToString("N"));
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private string GetPath()
        => _explicitPath ?? Path.Combine(
            Plugin.Instance?.StateDirectory ?? throw new InvalidOperationException("Plugin nie został zainicjalizowany."),
            "diagnostics.json");

    private sealed class DiagnosticState
    {
        public Dictionary<string, Dictionary<string, int>> CountsByDay { get; set; } = new(StringComparer.Ordinal);

        public List<PluginDiagnosticEvent> Events { get; set; } = [];
    }
}
