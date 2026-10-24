using Jellyfin.Plugin.AzureIllusion.Api;
using System.Security.Cryptography;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using Jellyfin.Plugin.AzureIllusion.Matching;
using Jellyfin.Plugin.AzureIllusion.State;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.Subtitles;

/// <summary>Native Jellyfin subtitle provider backed by the AzureIllusion public API.</summary>
public sealed class AzureIllusionSubtitleProvider : ISubtitleProvider
{
    private readonly AzureIllusionApiClient _apiClient;
    private readonly AniListResolver _resolver;
    private readonly DownloadStateStore _stateStore;
    private readonly DiagnosticEventStore _diagnostics;
    private readonly ILogger<AzureIllusionSubtitleProvider> _logger;
    private readonly MediaBrowser.Controller.Library.ILibraryManager? _library;
    private readonly PluginConfiguration? _configurationOverride;

    internal AzureIllusionSubtitleProvider(AzureIllusionApiClient client, AniListResolver resolver, DownloadStateStore store,
        DiagnosticEventStore diagnostics, ILogger<AzureIllusionSubtitleProvider> logger, PluginConfiguration configuration)
        : this(client, resolver, store, diagnostics, logger) => _configurationOverride = configuration;

    /// <summary>Initializes the provider.</summary>
    public AzureIllusionSubtitleProvider(
        AzureIllusionApiClient apiClient,
        AniListResolver resolver,
        DownloadStateStore stateStore,
        DiagnosticEventStore diagnostics,
        ILogger<AzureIllusionSubtitleProvider> logger,
        MediaBrowser.Controller.Library.ILibraryManager? library = null)
    {
        _apiClient = apiClient;
        _resolver = resolver;
        _stateStore = stateStore;
        _diagnostics = diagnostics;
        _logger = logger;
        _library = library;
    }

    /// <inheritdoc />
    public string Name => "Polskie Napisy Anime";

    /// <inheritdoc />
    public IEnumerable<VideoContentType> SupportedMediaTypes => [VideoContentType.Episode, VideoContentType.Movie];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        var configuration = GetConfiguration();
        if (request.IsAutomated
            && (!configuration.EnableAutomaticSearch
                || (!configuration.AllowJellyfinAutomaticSearch && !PluginAutomaticSearchContext.IsActive)))
        {
            return [];
        }

        if (request.IsAutomated
            && request.ContentType == VideoContentType.Episode
            && !request.IndexNumber.HasValue)
        {
            _logger.LogInformation(
                "Polskie Napisy Anime pominęło {Title}: automatyczne wyszukiwanie wymaga numeru odcinka.",
                request.SeriesName ?? request.Name);
            return [];
        }

        if (!IsSelectedLibrary(request.MediaPath, configuration.SelectedLibraryPaths))
        {
            return [];
        }

        if (!IsConfiguredLanguage(request, configuration.Languages))
        {
            return [];
        }

        var operationId = Guid.NewGuid().ToString("N")[..8];
        if (configuration.EnableDiagnosticLogging)
        {
            _logger.LogInformation(
                "Polskie Napisy Anime [diagnostyka:{OperationId}]: rozpoczynam wyszukiwanie. Tytuł: {Title}; typ: {ContentType}; sezon: {Season}; odcinek: {Episode}; automatyczne: {Automated}.",
                operationId,
                request.SeriesName ?? request.Name,
                request.ContentType,
                request.ParentIndexNumber,
                request.IndexNumber,
                request.IsAutomated);
        }

        try
        {
            var local = new LocalReconciliation([], new HashSet<string>(), new HashSet<string>());
            if (request.IsAutomated && !string.IsNullOrWhiteSpace(request.MediaPath))
            {
                var video = _library?.FindByPath(request.MediaPath, false) as MediaBrowser.Controller.Entities.Video;
                local = await new LocalDownloadReconciler(_stateStore).ReconcileAsync(
                    video is null ? new LocalMedia(request.MediaPath, null, []) : LocalMedia.FromVideo(video), cancellationToken,
                    persist: !PluginAutomaticSearchContext.IsSimulation).ConfigureAwait(false);
                PluginAutomaticSearchContext.RecordLocal(local.Items);
                foreach (var item in local.Items)
                    _logger.LogInformation("Polskie Napisy Anime: lokalny zapis {ReleaseId}, wynik {Result}, plik {Path}.", item.Download.ReleaseId, item.Result, request.MediaPath);
            }
            if (request.IsAutomated && request.ContentType == VideoContentType.Episode && request.IndexNumberEnd > request.IndexNumber)
            {
                PluginAutomaticSearchContext.SetOutcome("multiEpisode");
                await _diagnostics.TryRecordAsync("warning", "MULTI_EPISODE_MAPPING_REQUIRED", "Plik obejmuje kilka odcinków; wymagany ręczny wybór napisów do całego pliku.", request.MediaPath, cancellationToken).ConfigureAwait(false);
                return [];
            }
            var match = await _resolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
            if (match is null || !match.IsConfident)
            {
                PluginAutomaticSearchContext.SetOutcome("unresolved");
                _logger.LogInformation("AzureIllusion skipped {Title}: no unambiguous AniList match.", request.SeriesName ?? request.Name);
                await _diagnostics.TryRecordAsync(
                    "warning",
                    "ANILIST_SEASON_UNRESOLVED",
                    $"Nie udało się jednoznacznie dopasować anime: {request.SeriesName ?? request.Name}, sezon {request.ParentIndexNumber?.ToString() ?? "?"}.",
                    request.MediaPath,
                    cancellationToken).ConfigureAwait(false);
                return [];
            }

            var groups = configuration.GroupSelection == GroupSelectionMode.SelectedGroups
                ? (configuration.SelectedGroupSlugs ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
                : [];
            if (configuration.GroupSelection == GroupSelectionMode.SelectedGroups && groups.Length == 0)
            {
                PluginAutomaticSearchContext.SetOutcome("filteredGroups");
                _logger.LogWarning(
                    "AzureIllusion skipped {Title}: selected-groups mode is enabled, but no group is selected.",
                    request.SeriesName ?? request.Name);
                return [];
            }

            var requestedLanguages = ResolveRequestedLanguages(request, configuration.Languages);
            var mediaKey = BuildMediaKey(request);
            var query = new SubtitleQuery(
                match.AniListId,
                request.ContentType == VideoContentType.Episode ? match.Season ?? request.ParentIndexNumber : null,
                request.ContentType == VideoContentType.Episode ? match.Episode ?? request.IndexNumber : null,
                requestedLanguages,
                groups,
                configuration.VerifiedOnly,
                Math.Clamp(configuration.MinimumRating, 0, 10),
                100);
            var result = await _apiClient.SearchSubtitlesAsync(query, cancellationToken).ConfigureAwait(false);
            if (result.Releases.Count > 0 && ReleaseSelector.ExcludeGroups(result.Releases, configuration.IgnoredGroupSlugs).Count == 0)
                PluginAutomaticSearchContext.SetOutcome("filteredGroups");
            result = result with { Releases = ReleaseSelector.ExcludeGroups(result.Releases, configuration.IgnoredGroupSlugs) };
            if (result.Releases.Count == 0
                && query.Season.HasValue
                && query.Episode.HasValue)
            {
                var fallback = await _apiClient.SearchSubtitlesAsync(
                    query with { Season = null },
                    cancellationToken).ConfigureAwait(false);
                fallback = fallback with { Releases = ReleaseSelector.ExcludeGroups(fallback.Releases, configuration.IgnoredGroupSlugs) };
                if (CanUseSeasonFallback(fallback.Releases))
                {
                    result = fallback;
                    _logger.LogInformation(
                        "AzureIllusion used an unambiguous season fallback for {Title}, Jellyfin season {Season}, episode {Episode}.",
                        request.SeriesName ?? request.Name,
                        query.Season,
                        query.Episode);
                }
            }

            var prioritizedReleases = ReleaseSelector.ApplyGroupPriority(result.Releases, configuration.PriorityGroupSlugs);
            var releases = ReleaseSelector.LimitGroups(prioritizedReleases, Math.Max(configuration.MaximumGroups, 0));
            if (configuration.ReleaseSelection == ReleaseSelectionMode.BestOnly)
            {
                releases = releases.Take(1).ToArray();
            }
            var output = new List<RemoteSubtitleInfo>(releases.Count);

            foreach (var release in releases)
            {
                if (request.IsAutomated && local.BlockedReleases.Contains(release.Id)) continue;
                if (request.IsAutomated && local.PresentReleases.Contains(release.Id))
                {
                    PluginAutomaticSearchContext.RecordAlreadyPresent();
                    continue;
                }
                if (request.IsAutomated
                    && await _stateStore.ContainsAsync(mediaKey, release.Id, release.ChecksumSha256, cancellationToken).ConfigureAwait(false))
                {
                    PluginAutomaticSearchContext.RecordAlreadyPresent();
                    continue;
                }

                var language = NormalizeLanguage(release.Language);
                var payload = new SubtitleIdPayload(
                    release.Id,
                    mediaKey,
                    language,
                    release.Format,
                    release.ChecksumSha256,
                    release.Group?.Name,
                    request.MediaPath,
                    release.Group?.Slug,
                    match.AniListId,
                    release.Season?.Number,
                    release.Episode?.Number,
                    release.SizeBytes,
                    release.PublishedAt);
                output.Add(new RemoteSubtitleInfo
                {
                    Id = SubtitleIdCodec.Encode(payload),
                    ProviderName = Name,
                    ThreeLetterISOLanguageName = ToThreeLetterIso(language),
                    Name = BuildDisplayName(release),
                    Format = release.Format.ToLowerInvariant(),
                    Author = release.Group?.Name ?? "AzureIllusion",
                    Comment = BuildComment(release),
                    DateCreated = release.PublishedAt.UtcDateTime,
                    CommunityRating = (float)release.Rating.Average,
                    DownloadCount = release.Downloads,
                    IsHashMatch = false,
                    AiTranslated = false,
                    MachineTranslated = false,
                    Forced = false,
                    HearingImpaired = false,
                });
            }

            if (configuration.EnableDiagnosticLogging)
            {
                _logger.LogInformation(
                    "Polskie Napisy Anime [diagnostyka:{OperationId}]: wyszukiwanie zakończone. AniList: {AniListId}; zwrócono {ResultCount} wyników.",
                    operationId,
                    match.AniListId,
                    output.Count);
            }

            if (output.Count == 0 && releases.Count > 0)
                PluginAutomaticSearchContext.SetOutcome(releases.Any(x => local.BlockedReleases.Contains(x.Id)) ? "localConflict" : "alreadyDownloaded");
            return output;
        }
        catch (Exception exception) when (exception is AzureIllusionApiException or HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            PluginAutomaticSearchContext.SetOutcome("searchFailed");
            _logger.LogWarning(exception, "Polskie Napisy Anime [{OperationId}]: wyszukiwanie napisów nie powiodło się dla {Title}.", operationId, request.SeriesName ?? request.Name);
            if (request.IsAutomated || cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            await _diagnostics.TryRecordAsync("error", "SEARCH_FAILED", "Nie udało się połączyć z API napisów. To nie oznacza braku napisów.", request.MediaPath, cancellationToken).ConfigureAwait(false);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        var payload = SubtitleIdCodec.Decode(id);
        var configuration = GetConfiguration();
        if (ReleaseSelector.IsIgnoredGroup(payload.GroupSlug, configuration.IgnoredGroupSlugs))
            throw new InvalidOperationException("Ta grupa została zignorowana po wyszukiwaniu; pobieranie zablokowano.");
        var operationId = Guid.NewGuid().ToString("N")[..8];
        if (configuration.EnableDiagnosticLogging)
        {
            _logger.LogInformation(
                "Polskie Napisy Anime [diagnostyka:{OperationId}]: pobieranie wydania {ReleaseId}; grupa: {Group}; format: {Format}.",
                operationId,
                payload.ReleaseId,
                payload.GroupSlug ?? payload.GroupName ?? "brak",
                payload.Format);
        }
        var storedLanguage = BuildStoredLanguage(payload.Language, payload.GroupName, payload.ReleaseId);
        await using var source = await _apiClient.DownloadSubtitleAsync(payload.ReleaseId, cancellationToken).ConfigureAwait(false);
        var (stream, checksum) = await BufferVerifiedSubtitleAsync(source, payload.Checksum, cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(payload.MediaPath))
                await _stateStore.MarkDownloadedAsync(new ManagedSubtitleDownload(
                    payload.MediaKey, payload.ReleaseId, checksum, DateTimeOffset.UtcNow,
                    payload.MediaPath, payload.Language, storedLanguage, payload.Format.ToLowerInvariant(),
                    payload.GroupName, payload.GroupSlug, payload.AniListId, payload.Season, payload.Episode,
                    SubtitleRevision.Build(payload.Checksum, payload.SizeBytes, payload.PublishedAt), Pending: true,
                    MediaId: _library?.FindByPath(payload.MediaPath, false)?.Id), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (configuration.EnableDiagnosticLogging)
        {
            _logger.LogInformation(
                "Polskie Napisy Anime [diagnostyka:{OperationId}]: przekazano strumień wydania {ReleaseId} do Jellyfin; zadeklarowany rozmiar: {SizeBytes} B.",
                operationId,
                payload.ReleaseId,
                payload.SizeBytes);
        }

        return new SubtitleResponse
        {
            Language = storedLanguage,
            Format = payload.Format.ToLowerInvariant(),
            IsForced = false,
            IsHearingImpaired = false,
            Stream = stream,
        };
    }

    internal static async Task<(MemoryStream Stream, string Checksum)> BufferVerifiedSubtitleAsync(Stream source, string? expectedChecksum, CancellationToken cancellationToken)
    {
        const int maximumBytes = 32 * 1024 * 1024;
        var target = new MemoryStream();
        try
        {
            var buffer = new byte[65536];
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (target.Length + count > maximumBytes) throw new InvalidDataException("Plik napisów przekracza limit 32 MB.");
                await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            if (target.Length == 0) throw new InvalidDataException("Pobrany plik napisów jest pusty.");
            target.Position = 0;
            var checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(target, cancellationToken).ConfigureAwait(false));
            if (!string.IsNullOrWhiteSpace(expectedChecksum) && !string.Equals(checksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Suma SHA-256 pobranych napisów nie zgadza się z metadanymi.");
            target.Position = 0;
            return (target, checksum);
        }
        catch { await target.DisposeAsync().ConfigureAwait(false); throw; }
    }

    /// <summary>Builds a stable key for automatic-download deduplication.</summary>
    public static string BuildMediaKey(SubtitleSearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.MediaPath))
        {
            return BuildMediaKeyFromPath(request.MediaPath);
        }

        return string.Join(
            '|',
            request.ContentType,
            request.SeriesName ?? request.Name,
            request.ParentIndexNumber,
            request.IndexNumber).ToLowerInvariant();
    }

    internal static string BuildMediaKeyFromPath(string mediaPath)
        => mediaPath.Replace('\\', '/').Trim().ToLowerInvariant();

    public static bool IsSelectedLibrary(string? mediaPath, IReadOnlyList<string>? selectedPaths)
    {
        if (selectedPaths is null || selectedPaths.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(mediaPath)) return false;
        var candidate = NormalizeMediaPath(mediaPath);
        return selectedPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Any(path =>
        {
            var root = NormalizeMediaPath(path);
            return candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string NormalizeMediaPath(string path)
        => path.Trim().Replace('\\', '/').TrimEnd('/');

    internal static IReadOnlyList<string> NormalizeConfiguredLanguages(IReadOnlyList<string>? languages)
        => (languages ?? [])
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Select(language => language.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static IReadOnlyList<string> ResolveRequestedLanguages(
        SubtitleSearchRequest request,
        IReadOnlyList<string>? configuredLanguages)
    {
        var configured = NormalizeConfiguredLanguages(configuredLanguages);
        var requested = new[] { request.Language, request.TwoLetterISOLanguageName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => CanonicalLanguage(value!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0)
        {
            return configured;
        }

        return configured
            .Where(language => requested.Contains(CanonicalLanguage(language)))
            .ToArray();
    }

    internal static bool CanUseSeasonFallback(IReadOnlyList<SubtitleRelease> releases)
    {
        if (releases.Count == 0)
        {
            return false;
        }

        var seasons = releases
            .Where(release => release.Season is not null)
            .Select(release => release.Season!.Number)
            .Distinct()
            .Take(2)
            .Count();
        return seasons <= 1;
    }

    private static bool IsConfiguredLanguage(SubtitleSearchRequest request, IReadOnlyList<string>? languages)
    {
        return ResolveRequestedLanguages(request, languages).Count > 0;
    }

    private static string NormalizeLanguage(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "pl2" => "pl",
            var language => language,
        };

    internal static string ToThreeLetterIso(string language) => CanonicalLanguage(language) switch
    {
        "pl" => "pol",
        "en" => "eng",
        "ja" => "jpn",
        "de" => "deu",
        "fr" => "fra",
        "es" => "spa",
        "it" => "ita",
        "pt" => "por",
        "uk" => "ukr",
        "ru" => "rus",
        _ => language,
    };

    private static string CanonicalLanguage(string language) => language.Trim().ToLowerInvariant() switch
    {
        "pl2" or "pol" or "polish" or "polski" => "pl",
        var value when value.StartsWith("pl-", StringComparison.OrdinalIgnoreCase) => "pl",
        "eng" => "en",
        "jpn" => "ja",
        "deu" or "ger" => "de",
        "fra" or "fre" => "fr",
        "spa" => "es",
        "ita" => "it",
        "por" => "pt",
        "ukr" => "uk",
        "rus" => "ru",
        var value => value,
    };

    internal static string BuildStoredLanguage(string language, string? groupName, string? releaseId = null)
    {
        var iso = ToThreeLetterIso(language);
        var safeCharacters = string.Concat((groupName ?? "Inne")
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : ' '));
        var safeGroup = string.Join('-', safeCharacters.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var suffix = string.IsNullOrWhiteSpace(releaseId) ? "" : ".pna-" + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(releaseId)))[..12];
        return (string.IsNullOrWhiteSpace(safeGroup) ? $"{iso}.Inne" : $"{iso}.{safeGroup}") + suffix;
    }

    private static string BuildDisplayName(SubtitleRelease release)
    {
        var group = release.Group?.Name ?? "AzureIllusion";
        return $"[{release.Language.ToUpperInvariant()}] {group} - {release.Filename}";
    }

    private static string BuildComment(SubtitleRelease release)
    {
        var flags = new List<string>();
        if (release.IsVerified)
        {
            flags.Add("zweryfikowane");
        }

        if (release.IsRecommended)
        {
            flags.Add("polecane");
        }

        flags.Add($"ocena {release.Rating.Average:0.0}/10");
        flags.Add($"wersja {release.Version}");
        return string.Join(" | ", flags);
    }

    private PluginConfiguration GetConfiguration()
        => _configurationOverride ?? Plugin.Instance?.Configuration ?? throw new AzureIllusionApiException("AzureIllusion plugin is not initialized.");
}
