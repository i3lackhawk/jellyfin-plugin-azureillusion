using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using Jellyfin.Plugin.AzureIllusion.State;
using Jellyfin.Plugin.AzureIllusion.Storage;
using Jellyfin.Plugin.AzureIllusion.Subtitles;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.ScheduledTasks;

/// <summary>Downloads missing subtitles from Polskie Napisy Anime for selected libraries.</summary>
public sealed class DownloadMissingSubtitlesTask : IScheduledTask
{
    private const string ProviderName = "Polskie Napisy Anime";

    private readonly ILibraryManager _libraryManager;
    private readonly ISubtitleManager _subtitleManager;
    private readonly DownloadStateStore _stateStore;
    private readonly TaskReportStore _reportStore;
    private readonly DiagnosticEventStore _diagnostics;
    private readonly ILogger<DownloadMissingSubtitlesTask> _logger;
    private readonly UpdateDownloadedSubtitlesTask? _updateTask;
    private int _totalWorkItems;

    /// <summary>Initializes the scheduled task.</summary>
    public DownloadMissingSubtitlesTask(
        ILibraryManager libraryManager,
        ISubtitleManager subtitleManager,
        DownloadStateStore stateStore,
        TaskReportStore reportStore,
        DiagnosticEventStore diagnostics,
        ILogger<DownloadMissingSubtitlesTask> logger,
        UpdateDownloadedSubtitlesTask? updateTask = null)
    {
        _libraryManager = libraryManager;
        _subtitleManager = subtitleManager;
        _stateStore = stateStore;
        _reportStore = reportStore;
        _diagnostics = diagnostics;
        _logger = logger;
        _updateTask = updateTask;
    }

    /// <inheritdoc />
    public string Name => "Polskie Napisy Anime — konserwacja napisów";

    /// <inheritdoc />
    public string Key => "PolskieNapisyAnimeDownloadMissingSubtitles";

    /// <inheritdoc />
    public string Description =>
        "Pobiera brakujące napisy z wybranych grup, a potem sprawdza, czy są nowsze wersje wcześniej pobranych plików.";

    /// <inheritdoc />
    public string Category => "Polskie Napisy Anime";

    /// <summary>Gets a value indicating whether the task is hidden.</summary>
    public bool IsHidden => false;

    /// <summary>Gets a value indicating whether the task is enabled.</summary>
    public bool IsEnabled => true;

    /// <summary>Gets a value indicating whether executions are logged.</summary>
    public bool IsLogged => true;

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => ExecuteWithModeAsync(progress, cancellationToken, simulation: false);

    internal Task ExecuteSimulationAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => ExecuteWithModeAsync(progress, cancellationToken, simulation: true);

    private async Task ExecuteWithModeAsync(IProgress<double> progress, CancellationToken cancellationToken, bool simulation)
    {
        await MaintenanceProgress.WaitAsync(SubtitleTaskExecutionGate.Instance, cancellationToken).ConfigureAwait(false);
        MaintenanceProgress.Report("download", 0);

        var maintenanceStarted = DateTimeOffset.UtcNow;
        try
        {
            var partial = false;
            if (simulation)
            {
                await ExecuteCoreAsync(progress, cancellationToken, simulation: true).ConfigureAwait(false);
                return;
            }

            var configuration = Plugin.Instance?.Configuration
                ?? throw new InvalidOperationException("Plugin nie został zainicjalizowany.");
            await RunMaintenanceStagesAsync(
                configuration.EnableAutomaticSearch,
                _updateTask is not null && configuration.EnableSubtitleUpdates,
                async () => { if (!await RunBudgetedAsync("download", token => ExecuteCoreAsync(new InlineProgress(value => { progress.Report(value * 0.7); MaintenanceProgress.Report("download", value); }), token, simulation: false), cancellationToken).ConfigureAwait(false)) partial = true; },
                async () => { if (!await RunBudgetedAsync("update", token => _updateTask!.ExecuteWithinMaintenanceAsync(new InlineProgress(value => { progress.Report(70 + value * 0.3); MaintenanceProgress.Report("update", value); }), token), cancellationToken).ConfigureAwait(false)) partial = true; },
                cancellationToken).ConfigureAwait(false);

            progress.Report(100);
            await _reportStore.SaveAsync(new PluginTaskReport("maintenance", "Konserwacja napisów", false,
                maintenanceStarted, DateTimeOffset.UtcNow,
                !configuration.EnableAutomaticSearch && !configuration.EnableSubtitleUpdates ? "disabled" : partial ? "partial" : "completed",
                new Dictionary<string, int>(), [], partial ? "Zachowano punkt wznowienia po wyczerpaniu budżetu etapu." : null), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!simulation) await _reportStore.SaveAsync(new PluginTaskReport("maintenance", "Konserwacja napisów", false,
                maintenanceStarted, DateTimeOffset.UtcNow, cancellationToken.IsCancellationRequested ? "cancelled" : "failed",
                new Dictionary<string, int>(), [], "Konserwacja przerwana; szczegóły w raportach etapów i logu Jellyfin."), CancellationToken.None).ConfigureAwait(false);
            _logger.LogWarning(exception, "Konserwacja nie została ukończona.");
            throw;
        }
        finally
        {
            MaintenanceProgress.Report("idle", 0);
            SubtitleTaskExecutionGate.Instance.Release();
        }
    }

    private async Task<bool> RunBudgetedAsync(string stage, Func<CancellationToken, Task> action, CancellationToken parent)
    {
        var started = DateTimeOffset.UtcNow;
        if (!await StageTimeBudget.RunAsync(action, TimeSpan.FromMinutes(90), parent).ConfigureAwait(false))
        {
            var key = stage == "download" ? "download-missing" : "update-downloaded";
            var report = await _reportStore.ReadLatestAsync(key, CancellationToken.None).ConfigureAwait(false);
            if (report is not null && report.StartedAtUtc >= started)
                await _reportStore.SaveAsync(report with { Status = "partial", Message = "Wyczerpano budżet 90 minut etapu; zachowano punkt wznowienia." }, CancellationToken.None).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    internal static async Task RunMaintenanceStagesAsync(
        bool downloadEnabled, bool updateEnabled, Func<Task> download, Func<Task> update, CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        foreach (var stage in new[] { (downloadEnabled, download), (updateEnabled, update) })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!stage.Item1) continue;
            try { await stage.Item2().ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { errors.Add(exception); }
        }
        if (errors.Count > 0) throw new AggregateException("Konserwacja zakończyła się z błędami etapów.", errors);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private async Task ExecuteCoreAsync(IProgress<double> progress, CancellationToken cancellationToken, bool simulation)
    {
        using var metrics = TaskOperationMetrics.Enter();
        var startedAt = DateTimeOffset.UtcNow;
        var configuration = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Plugin Polskie Napisy Anime nie został zainicjalizowany.");
        if (!configuration.EnableAutomaticSearch)
        {
            throw new InvalidOperationException(
                "Automatyczne wyszukiwanie jest wyłączone w ustawieniach pluginu Polskie Napisy Anime.");
        }

        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            throw new InvalidOperationException("W ustawieniach pluginu brakuje klucza API.");
        }

        var configuredLanguages = AzureIllusionSubtitleProvider.NormalizeConfiguredLanguages(configuration.Languages);
        if (configuredLanguages.Count == 0)
        {
            throw new InvalidOperationException("W ustawieniach pluginu nie wybrano żadnego języka.");
        }

        if (!simulation)
            await SubtitleRecoveryJournal.RecoverAsync(Path.Combine(Plugin.Instance!.StateDirectory, "transactions"), _stateStore, _logger, cancellationToken).ConfigureAwait(false);
        var reconciler = new LocalDownloadReconciler(_stateStore);
        var candidates = FindCandidates(configuration, configuredLanguages, cancellationToken);
        var checkpoint = new SearchCheckpointStore(Path.Combine(Plugin.Instance!.StateDirectory, "search-checkpoint.json"));
        await checkpoint.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!simulation)
            foreach (var pair in candidates.ToArray())
            {
                pair.Value.Languages.RemoveWhere(language => !checkpoint.IsDue(pair.Key + "|" + language));
                if (pair.Value.Languages.Count == 0) candidates.Remove(pair.Key);
            }
        var workItems = candidates.Sum(candidate => candidate.Value.Languages.Count);
        _totalWorkItems = workItems;
        var reportItems = new List<PluginTaskReportItem>();
        if (workItems == 0)
        {
            _logger.LogInformation(
                "Polskie Napisy Anime: nie znaleziono plików bez napisów w wybranych bibliotekach. Wybrane ścieżki: {Paths}",
                FormatSelectedPaths(configuration.SelectedLibraryPaths));
            await SaveReportAsync(simulation, startedAt, 0, 0, 0, 0, 0, 0, reportItems, "completed", "Brak plików wymagających pobrania.", cancellationToken).ConfigureAwait(false);
            progress.Report(100);
            return;
        }

        var checkedItems = 0;
        var downloadedFiles = 0;
        var plannedFiles = 0;
        var insufficientSpace = 0;
        var noResults = 0;
        var failedItems = 0;
        var searchOutcomes = new Dictionary<string, int>();

        try
        {
            foreach (var candidate in candidates.OrderBy(pair => pair.Value.Languages.Min(language => checkpoint.LastChecked(pair.Key + "|" + language))).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value))
            {
                foreach (var language in candidate.Languages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var failuresBefore = failedItems;
                    try
                    {
                    var request = CreateSearchRequest(candidate.Video, language);
                    using var automaticSearchScope = PluginAutomaticSearchContext.Enter(simulation);
                    var localBeforeSearch = await reconciler.ReconcileAsync(
                        LocalMedia.FromVideo(candidate.Video), cancellationToken, persist: !simulation).ConfigureAwait(false);
                    PluginAutomaticSearchContext.RecordLocal(localBeforeSearch.Items);
                    var results = await _subtitleManager.SearchSubtitles(request, cancellationToken).ConfigureAwait(false);
                    if (automaticSearchScope.AlreadyPresent > 0)
                        searchOutcomes["already-downloaded"] = searchOutcomes.GetValueOrDefault("already-downloaded") + automaticSearchScope.AlreadyPresent;
                    foreach (var item in automaticSearchScope.LocalItems)
                    {
                        searchOutcomes[item.Result] = searchOutcomes.GetValueOrDefault(item.Result) + 1;
                        if (reportItems.Count < TaskReportStore.MaximumItems)
                            reportItems.Add(new PluginTaskReportItem(candidate.Video.Path, language, item.Download.ReleaseId, item.Download.GroupName, null, item.Result));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (automaticSearchScope.Outcome == "searchFailed")
                        throw new InvalidOperationException("Dostawca zgłosił błąd wyszukiwania. To nie jest brak napisów.");
                    var selectedResults = configuration.ReleaseSelection == ReleaseSelectionMode.BestOnly
                        ? results.Take(1).ToArray()
                        : results;

                    if (selectedResults.Length == 0)
                    {
                        var outcome = automaticSearchScope.Outcome;
                        if (outcome == "noResults") noResults++;
                        else if (outcome != "alreadyDownloaded") searchOutcomes[outcome] = searchOutcomes.GetValueOrDefault(outcome) + 1;
                        if (reportItems.Count < TaskReportStore.MaximumItems)
                            reportItems.Add(new PluginTaskReportItem(candidate.Video.Path, language, null, null, null, outcome));
                        _logger.LogInformation(
                            "Polskie Napisy Anime: brak wyniku dla {Path}, język {Language}; przyczyna: {Outcome}.",
                            candidate.Video.Path,
                            language, outcome);
                    }
                    else
                    {
                        var downloadedForVideo = 0;
                        foreach (var result in selectedResults)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                var payload = SubtitleIdCodec.Decode(result.Id);
                                var space = DiskSpaceGuard.Check(
                                    candidate.Video.Path,
                                    payload.SizeBytes,
                                    configuration.MinimumFreeSpaceMegabytes);
                                if (space.WasChecked && !space.HasSpace)
                                {
                                    insufficientSpace++;
                                    AddReportItem(reportItems, candidate.Video.Path, language, payload, "insufficient-space");
                                    _logger.LogWarning(
                                        "Polskie Napisy Anime: pominięto {Path}. Dostępne miejsce: {AvailableBytes} B; wymagane: {RequiredBytes} B.",
                                        candidate.Video.Path,
                                        space.AvailableBytes,
                                        space.RequiredBytes);
                                    await _diagnostics.TryRecordAsync("warning", "INSUFFICIENT_SPACE", "Za mało wolnego miejsca na pobranie napisów.", payload.ReleaseId, cancellationToken).ConfigureAwait(false);
                                    continue;
                                }

                                if (simulation)
                                {
                                    plannedFiles++;
                                    AddReportItem(reportItems, candidate.Video.Path, language, payload, "would-download");
                                    continue;
                                }

                                var downloadResult = await reconciler.DownloadIfMissingAsync(
                                    LocalMedia.FromVideo(candidate.Video), payload,
                                    token => _subtitleManager.DownloadSubtitles(candidate.Video, result.Id, token), cancellationToken).ConfigureAwait(false);
                                if (downloadResult != "downloaded")
                                {
                                    searchOutcomes[downloadResult] = searchOutcomes.GetValueOrDefault(downloadResult) + 1;
                                    AddReportItem(reportItems, candidate.Video.Path, language, payload, downloadResult);
                                    continue;
                                }
                                downloadedFiles++;
                                downloadedForVideo++;
                                AddReportItem(reportItems, candidate.Video.Path, language, payload, "downloaded");
                            }
                            catch (Exception exception) when (exception is not OperationCanceledException)
                            {
                                failedItems++;
                                _logger.LogError(
                                    exception,
                                    "Polskie Napisy Anime: nie udało się zapisać {Subtitle} dla {Path}.",
                                    result.Name,
                                    candidate.Video.Path);
                                await _diagnostics.TryRecordAsync("error", "DOWNLOAD_FAILED", "Nie udało się pobrać lub zapisać napisu; szczegóły są w logu Jellyfin.", result.Name, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        if (downloadedForVideo > 0)
                        {
                            await candidate.Video.RefreshMetadata(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        failedItems++;
                        _logger.LogError(
                            exception,
                            "Polskie Napisy Anime: błąd wyszukiwania dla {Path}, język {Language}.",
                            candidate.Video.Path,
                            language);
                        await _diagnostics.TryRecordAsync("error", "SEARCH_FAILED", "Wyszukiwanie napisów nie powiodło się; szczegóły są w logu Jellyfin.", candidate.Video.Name, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (!simulation && !cancellationToken.IsCancellationRequested)
                        {
                            checkpoint.Record(AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(candidate.Video.Path) + "|" + language, failedItems > failuresBefore);
                        }
                        checkedItems++;
                        if (!simulation && checkedItems % 250 == 0) await checkpoint.SaveAsync(CancellationToken.None).ConfigureAwait(false);
                        progress.Report(100d * checkedItems / workItems);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (!simulation) await checkpoint.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            await SaveReportAsync(
                simulation, startedAt, checkedItems, downloadedFiles, plannedFiles, noResults,
                insufficientSpace, failedItems, reportItems, "cancelled",
                "Zadanie przerwano. Pobrane dotąd pliki i stan zostały zachowane.",
                CancellationToken.None, searchOutcomes).ConfigureAwait(false);
            throw;
        }

        if (!simulation) await checkpoint.SaveAsync(cancellationToken).ConfigureAwait(false);

        if (simulation)
        {
            _logger.LogInformation(
                "Polskie Napisy Anime zakończyło symulację. Sprawdzono: {Checked}; do pobrania: {Planned}; bez wyników: {NoResults}; za mało miejsca: {InsufficientSpace}; błędy: {Failed}.",
                checkedItems,
                plannedFiles,
                noResults,
                insufficientSpace,
                failedItems);
        }
        else
        {
            _logger.LogInformation(
                "Polskie Napisy Anime zakończyło zadanie. Sprawdzono: {Checked}; pobrano plików: {Downloaded}; bez wyników: {NoResults}; za mało miejsca: {InsufficientSpace}; błędy: {Failed}.",
                checkedItems,
                downloadedFiles,
                noResults,
                insufficientSpace,
                failedItems);
        }

        await SaveReportAsync(
            simulation,
            startedAt,
            checkedItems,
            downloadedFiles,
            plannedFiles,
            noResults,
            insufficientSpace,
            failedItems,
            reportItems,
            failedItems > 0 ? "completed-with-errors" : "completed",
            null,
            cancellationToken, searchOutcomes).ConfigureAwait(false);

        if (failedItems > 0)
        {
            throw new InvalidOperationException(
                $"Zadanie zakończyło się z błędami: {failedItems}. Szczegóły znajdują się w logu Jellyfin.");
        }
    }

    private Dictionary<string, Candidate> FindCandidates(
        PluginConfiguration configuration,
        IReadOnlyList<string> configuredLanguages,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in _libraryManager.RootFolder.Children.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var libraryOptions = _libraryManager.GetLibraryOptions(library);
            var jellyfinLanguages = configuredLanguages
                .Select(AzureIllusionSubtitleProvider.ToThreeLetterIso)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var language in jellyfinLanguages)
            {
                var query = new InternalItemsQuery
                {
                    MediaTypes = [MediaType.Video],
                    IsVirtualItem = false,
                    DtoOptions = new DtoOptions(true),
                    SourceTypes = [SourceType.Library],
                    Parent = library,
                    Recursive = true,
                };

                if (libraryOptions.SkipSubtitlesIfAudioTrackMatches)
                {
                    query.HasNoAudioTrackWithLanguage = language;
                }

                if (configuration.ReleaseSelection != ReleaseSelectionMode.All && libraryOptions.SkipSubtitlesIfEmbeddedSubtitlesPresent)
                {
                    query.HasNoSubtitleTrackWithLanguage = language;
                }
                else if (configuration.ReleaseSelection != ReleaseSelectionMode.All)
                {
                    query.HasNoExternalSubtitleTrackWithLanguage = language;
                }

                foreach (var item in LibraryPager.Read(_libraryManager, query, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item is not Video video
                        || video.VideoType != VideoType.VideoFile
                        || string.IsNullOrWhiteSpace(video.Path)
                        || !AzureIllusionSubtitleProvider.IsSelectedLibrary(video.Path, configuration.SelectedLibraryPaths))
                    {
                        continue;
                    }

                    if (!HasRequiredEpisodeNumber(video is MediaBrowser.Controller.Entities.TV.Episode, video.IndexNumber))
                    {
                        _logger.LogInformation(
                            "Polskie Napisy Anime pominęło {Path}: odcinek nie ma numeru w Jellyfin.",
                            video.Path);
                        continue;
                    }

                    var mediaKey = AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(video.Path);
                    if (!candidates.TryGetValue(mediaKey, out var candidate))
                    {
                        candidate = new Candidate(video);
                        candidates.Add(mediaKey, candidate);
                    }

                    candidate.Languages.Add(language);
                }
            }
        }

        return candidates;
    }

    private SubtitleSearchRequest CreateSearchRequest(Video video, string language)
    {
        var providerIds = BuildProviderIdsForSearch(video.ProviderIds);
        int? productionYear = video.ProductionYear;
        if (video is MediaBrowser.Controller.Entities.TV.Episode { Series: not null } episodeWithSeries)
        {
            productionYear = episodeWithSeries.Series.ProductionYear ?? productionYear;
        }

        var request = new SubtitleSearchRequest
        {
            ContentType = video is MediaBrowser.Controller.Entities.TV.Episode
                ? VideoContentType.Episode
                : VideoContentType.Movie,
            IndexNumber = video.IndexNumber,
            Language = language,
            MediaPath = video.Path,
            Name = video.Name,
            ParentIndexNumber = video.ParentIndexNumber,
            ProductionYear = productionYear,
            ProviderIds = providerIds,
            RuntimeTicks = video.RunTimeTicks,
            SearchAllProviders = false,
            IsPerfectMatch = false,
            IsAutomated = true,
            DisabledSubtitleFetchers = _subtitleManager
                .GetSupportedProviders(video)
                .Where(provider => !string.Equals(provider.Name, ProviderName, StringComparison.OrdinalIgnoreCase))
                .Select(provider => provider.Name)
                .ToArray(),
            SubtitleFetcherOrder = [ProviderName],
        };

        if (video is MediaBrowser.Controller.Entities.TV.Episode episode)
        {
            request.IndexNumberEnd = episode.IndexNumberEnd;
            request.SeriesName = episode.SeriesName;
        }

        return request;
    }

    internal static Dictionary<string, string> BuildProviderIdsForSearch(IReadOnlyDictionary<string, string>? videoProviderIds)
        => videoProviderIds is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(videoProviderIds, StringComparer.OrdinalIgnoreCase);

    internal static bool HasRequiredEpisodeNumber(bool isEpisode, int? indexNumber)
        => !isEpisode || indexNumber.HasValue;

    private static void AddReportItem(
        List<PluginTaskReportItem> items,
        string media,
        string language,
        SubtitleIdPayload payload,
        string result)
    {
        if (items.Count >= TaskReportStore.MaximumItems)
        {
            return;
        }

        items.Add(new PluginTaskReportItem(
            media,
            language,
            payload.ReleaseId,
            payload.GroupName ?? payload.GroupSlug,
            payload.SizeBytes,
            result));
    }

    private Task SaveReportAsync(
        bool simulation,
        DateTimeOffset startedAt,
        int checkedItems,
        int downloadedFiles,
        int plannedFiles,
        int noResults,
        int insufficientSpace,
        int failedItems,
        IReadOnlyList<PluginTaskReportItem> items,
        string status,
        string? message,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, int>? searchOutcomes = null)
    {
        var taskKey = simulation ? "simulate-missing" : "download-missing";
        var report = new PluginTaskReport(
            taskKey,
            simulation ? "Symulacja pobierania brakujących napisów" : "Pobieranie brakujących napisów",
            simulation,
            startedAt,
            DateTimeOffset.UtcNow,
            status,
            new Dictionary<string, int>
            {
                ["checked"] = checkedItems,
                ["downloaded"] = downloadedFiles,
                ["planned"] = plannedFiles,
                ["noResults"] = noResults,
                ["insufficientSpace"] = insufficientSpace,
                ["failed"] = failedItems,
            }.Concat(searchOutcomes ?? new Dictionary<string, int>()).Concat(TaskOperationMetrics.Snapshot()).ToDictionary(pair => pair.Key, pair => pair.Value),
            items.Take(TaskReportStore.MaximumItems).ToArray(),
            message, Total: _totalWorkItems);
        return _reportStore.SaveAsync(report, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Run once per night. This plugin understands WebSubs-specific anime,
        // season and release-group metadata that Jellyfin's built-in task lacks.
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(4).Ticks,
            },
        ];
    }

    private static string FormatSelectedPaths(IReadOnlyList<string>? selectedPaths)
        => selectedPaths is null || selectedPaths.Count == 0
            ? "wszystkie"
            : string.Join(", ", selectedPaths);

    private sealed class Candidate
    {
        public Candidate(Video video)
        {
            Video = video;
        }

        public Video Video { get; }

        public HashSet<string> Languages { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
