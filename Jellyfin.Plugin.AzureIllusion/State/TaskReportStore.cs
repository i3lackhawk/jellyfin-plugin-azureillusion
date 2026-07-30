using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.State;

public sealed record PluginTaskReportItem(string Media, string Language, string? ReleaseId, string? Group, long? SizeBytes, string Result);

public sealed record PluginTaskReport(
    string TaskKey,
    string TaskName,
    bool Simulation,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string Status,
    IReadOnlyDictionary<string, int> Counters,
    IReadOnlyList<PluginTaskReportItem> Items,
    string? Message);

/// <summary>Stores only the latest bounded report for every plugin task.</summary>
public sealed class TaskReportStore
{
    public const int MaximumItems = 500;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<TaskReportStore> _logger;

    public TaskReportStore(ILogger<TaskReportStore> logger) => _logger = logger;

    public async Task SaveAsync(PluginTaskReport report, CancellationToken cancellationToken)
    {
        var path = ReportPath(report.TaskKey);
        var temporaryPath = string.Concat(path, ".tmp-", Guid.NewGuid().ToString("N"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
            _gate.Release();
        }
    }

    public async Task<PluginTaskReport?> ReadLatestAsync(string taskKey, CancellationToken cancellationToken)
    {
        var path = ReportPath(taskKey);
        if (!File.Exists(path))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<PluginTaskReport>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _logger.LogWarning(exception, "Nie udało się odczytać raportu zadania pluginu {TaskKey}.", taskKey);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string SafeTaskKey(string taskKey)
        => string.Concat(taskKey.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')).ToLowerInvariant();

    private static string ReportPath(string taskKey)
    {
        var stateDirectory = Plugin.Instance?.StateDirectory ?? throw new InvalidOperationException("Plugin Polskie Napisy Anime nie został zainicjalizowany.");
        return Path.Combine(stateDirectory, "reports", string.Concat(SafeTaskKey(taskKey), "-latest.json"));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A uniquely named temporary report can be removed during regular maintenance.
        }
    }
}
