using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AzureIllusion.Configuration;
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
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);
    private readonly ILibraryManager _libraryManager;
    private readonly ISubtitleManager _subtitleManager;
    private readonly ILogger<DownloadMissingSubtitlesTask> _logger;

    /// <summary>Initializes the scheduled task.</summary>
    public DownloadMissingSubtitlesTask(
        ILibraryManager libraryManager,
        ISubtitleManager subtitleManager,
        ILogger<DownloadMissingSubtitlesTask> logger)
    {
        _libraryManager = libraryManager;
        _subtitleManager = subtitleManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Polskie Napisy Anime — pobierz brakujące napisy";

    /// <inheritdoc />
    public string Key => "PolskieNapisyAnimeDownloadMissingSubtitles";

    /// <inheritdoc />
    public string Description =>
        "Wyszukuje i pobiera brakujące napisy wyłącznie z Polskie Napisy Anime dla bibliotek wybranych w ustawieniach pluginu.";

    /// <inheritdoc />
    public string Category => "Polskie Napisy Anime";

    /// <summary>Gets a value indicating whether the task is hidden.</summary>
    public bool IsHidden => false;

    /// <summary>Gets a value indicating whether the task is enabled.</summary>
    public bool IsEnabled => true;

    /// <summary>Gets a value indicating whether executions are logged.</summary>
    public bool IsLogged => true;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!await ExecutionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Zadanie Polskie Napisy Anime jest już uruchomione.");
        }

        try
        {
            await ExecuteCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private async Task ExecuteCoreAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
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

        var candidates = FindCandidates(configuration, configuredLanguages, cancellationToken);
        var workItems = candidates.Sum(candidate => candidate.Value.Languages.Count);
        if (workItems == 0)
        {
            _logger.LogInformation(
                "Polskie Napisy Anime: nie znaleziono plików bez napisów w wybranych bibliotekach. Wybrane ścieżki: {Paths}",
                FormatSelectedPaths(configuration.SelectedLibraryPaths));
            progress.Report(100);
            return;
        }

        var checkedItems = 0;
        var downloadedFiles = 0;
        var noResults = 0;
        var failedItems = 0;

        foreach (var candidate in candidates.Values)
        {
            foreach (var language in candidate.Languages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var request = CreateSearchRequest(candidate.Video, language);
                    var results = await _subtitleManager.SearchSubtitles(request, cancellationToken).ConfigureAwait(false);
                    var selectedResults = configuration.ReleaseSelection == ReleaseSelectionMode.BestOnly
                        ? results.Take(1).ToArray()
                        : results;

                    if (selectedResults.Length == 0)
                    {
                        noResults++;
                        _logger.LogInformation(
                            "Polskie Napisy Anime: brak wyniku dla {Path}, język {Language}.",
                            candidate.Video.Path,
                            language);
                    }
                    else
                    {
                        var downloadedForVideo = 0;
                        foreach (var result in selectedResults)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                await _subtitleManager.DownloadSubtitles(
                                    candidate.Video,
                                    result.Id,
                                    cancellationToken).ConfigureAwait(false);
                                downloadedFiles++;
                                downloadedForVideo++;
                            }
                            catch (Exception exception) when (exception is not OperationCanceledException)
                            {
                                failedItems++;
                                _logger.LogError(
                                    exception,
                                    "Polskie Napisy Anime: nie udało się zapisać {Subtitle} dla {Path}.",
                                    result.Name,
                                    candidate.Video.Path);
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
                }
                finally
                {
                    checkedItems++;
                    progress.Report(100d * checkedItems / workItems);
                }
            }
        }

        _logger.LogInformation(
            "Polskie Napisy Anime zakończyło zadanie. Sprawdzono: {Checked}; pobrano plików: {Downloaded}; bez wyników: {NoResults}; błędy: {Failed}.",
            checkedItems,
            downloadedFiles,
            noResults,
            failedItems);

        if (failedItems > 0)
        {
            throw new InvalidOperationException(
                $"Zadanie zakończyło się z błędami: {failedItems}. Szczegóły znajdują się w logu Jellyfin.");
        }
    }

    private Dictionary<Guid, Candidate> FindCandidates(
        PluginConfiguration configuration,
        IReadOnlyList<string> configuredLanguages,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<Guid, Candidate>();
        var itemTypes = new[] { BaseItemKind.Episode, BaseItemKind.Movie };

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
                    IncludeItemTypes = itemTypes,
                    DtoOptions = new DtoOptions(true),
                    SourceTypes = [SourceType.Library],
                    Parent = library,
                    Recursive = true,
                };

                if (libraryOptions.SkipSubtitlesIfAudioTrackMatches)
                {
                    query.HasNoAudioTrackWithLanguage = language;
                }

                if (libraryOptions.SkipSubtitlesIfEmbeddedSubtitlesPresent)
                {
                    query.HasNoSubtitleTrackWithLanguage = language;
                }
                else
                {
                    query.HasNoExternalSubtitleTrackWithLanguage = language;
                }

                foreach (var item in _libraryManager.GetItemList(query))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item is not Video video
                        || video.VideoType != VideoType.VideoFile
                        || !video.IsCompleteMedia
                        || !AzureIllusionSubtitleProvider.IsSelectedLibrary(video.Path, configuration.SelectedLibraryPaths))
                    {
                        continue;
                    }

                    if (!candidates.TryGetValue(video.Id, out var candidate))
                    {
                        candidate = new Candidate(video);
                        candidates.Add(video.Id, candidate);
                    }

                    candidate.Languages.Add(language);
                }
            }
        }

        return candidates;
    }

    private SubtitleSearchRequest CreateSearchRequest(Video video, string language)
    {
        var providerIds = new Dictionary<string, string>(video.ProviderIds, StringComparer.OrdinalIgnoreCase);
        int? productionYear = video.ProductionYear;
        if (video is MediaBrowser.Controller.Entities.TV.Episode { Series: not null } episodeWithSeries)
        {
            foreach (var providerId in episodeWithSeries.Series.ProviderIds)
            {
                providerIds[providerId.Key] = providerId.Value;
            }

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

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // No default trigger: the built-in Jellyfin subtitle task may still be scheduled.
        // The administrator can add a trigger after verifying the first manual run.
        return [];
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
