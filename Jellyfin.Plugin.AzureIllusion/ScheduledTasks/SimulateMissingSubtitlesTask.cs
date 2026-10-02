using Jellyfin.Plugin.AzureIllusion.State;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.ScheduledTasks;

/// <summary>Runs the exact missing-subtitle scan without writing subtitle files.</summary>
public sealed class SimulateMissingSubtitlesTask : IScheduledTask
{
    private readonly DownloadMissingSubtitlesTask _runner;

    public SimulateMissingSubtitlesTask(
        ILibraryManager libraryManager,
        ISubtitleManager subtitleManager,
        DownloadStateStore stateStore,
        TaskReportStore reportStore,
        DiagnosticEventStore diagnostics,
        ILogger<DownloadMissingSubtitlesTask> logger)
    {
        _runner = new DownloadMissingSubtitlesTask(libraryManager, subtitleManager, stateStore, reportStore, diagnostics, logger);
    }

    public string Name => "Polskie Napisy Anime — symuluj pobieranie";

    public string Key => "PolskieNapisyAnimeSimulateMissingSubtitles";

    public string Description =>
        "Sprawdza te same biblioteki, filtry, priorytety grup i miejsce na dysku co prawdziwe zadanie, ale nie pobiera ani nie zapisuje żadnego pliku.";

    public string Category => "Polskie Napisy Anime";

    public bool IsHidden => false;

    public bool IsEnabled => true;

    public bool IsLogged => true;

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _runner.ExecuteSimulationAsync(progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
