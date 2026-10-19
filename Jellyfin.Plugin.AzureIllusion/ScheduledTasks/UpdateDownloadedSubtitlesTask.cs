using System.Security.Cryptography;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AzureIllusion.Api;
using Jellyfin.Plugin.AzureIllusion.State;
using Jellyfin.Plugin.AzureIllusion.Subtitles;
using Jellyfin.Plugin.AzureIllusion.Storage;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.ScheduledTasks;

/// <summary>Updates files previously downloaded by this plugin without deleting local subtitles.</summary>
public sealed class UpdateDownloadedSubtitlesTask : IScheduledTask
{
    private readonly AzureIllusionApiClient _apiClient;
    private readonly DownloadStateStore _stateStore;
    private readonly ILibraryManager _libraryManager;
    private readonly TaskReportStore _reportStore;
    private readonly DiagnosticEventStore _diagnostics;
    private readonly ILogger<UpdateDownloadedSubtitlesTask> _logger;
    private readonly List<PluginTaskReportItem> _reportItems = [];
    private int _totalRecords;

    public UpdateDownloadedSubtitlesTask(
        AzureIllusionApiClient apiClient,
        DownloadStateStore stateStore,
        ILibraryManager libraryManager,
        TaskReportStore reportStore,
        DiagnosticEventStore diagnostics,
        ILogger<UpdateDownloadedSubtitlesTask> logger)
    {
        _apiClient = apiClient;
        _stateStore = stateStore;
        _libraryManager = libraryManager;
        _reportStore = reportStore;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public string Name => "Polskie Napisy Anime — aktualizuj pobrane napisy";

    public string Key => "PolskieNapisyAnimeUpdateDownloadedSubtitles";

    public string Description =>
        "Sprawdza nowe wersje napisów pobranych przez wtyczkę. Zachowuje pliki zmienione ręcznie i nie usuwa napisów, które zniknęły ze strony.";

    public string Category => "Polskie Napisy Anime";

    public bool IsHidden => false;

    public bool IsEnabled => true;

    public bool IsLogged => true;

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await MaintenanceProgress.WaitAsync(SubtitleTaskExecutionGate.Instance, cancellationToken).ConfigureAwait(false);
        MaintenanceProgress.Report("update", 0);

        try
        {
            await ExecuteCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            MaintenanceProgress.Report("idle", 0);
            SubtitleTaskExecutionGate.Instance.Release();
        }
    }

    internal Task ExecuteWithinMaintenanceAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => ExecuteCoreAsync(progress, cancellationToken);

    private async Task ExecuteCoreAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var metrics = TaskOperationMetrics.Enter();
        var startedAt = DateTimeOffset.UtcNow;
        _reportItems.Clear();
        _totalRecords = 0;
        var configuration = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Plugin Polskie Napisy Anime nie został zainicjalizowany.");
        if (!configuration.EnableSubtitleUpdates)
        {
            throw new InvalidOperationException("Aktualizowanie pobranych napisów jest wyłączone w ustawieniach pluginu.");
        }

        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            throw new InvalidOperationException("W ustawieniach pluginu brakuje klucza API.");
        }

        var videos = FindVideos(cancellationToken);
        await SubtitleRecoveryJournal.RecoverAsync(Path.Combine(Plugin.Instance!.StateDirectory, "transactions"), _stateStore, _logger, cancellationToken).ConfigureAwait(false);
        var allRecords = await _stateStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var videosById = videos.Values.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
        foreach (var moved in allRecords.Where(x => !videos.ContainsKey(x.MediaKey) && x.MediaId.HasValue && !string.IsNullOrWhiteSpace(x.Checksum)))
        {
            if (!videosById.TryGetValue(moved.MediaId!.Value, out var video)) continue;
            var matches = new List<string>();
            foreach (var file in (video.SubtitleFiles ?? []).Where(File.Exists))
                if (await ComputeSha256Async(file, cancellationToken).ConfigureAwait(false) == moved.Checksum) matches.Add(file);
            if (matches.Count == 1)
                await _stateStore.RebindAsync(moved, AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(video.Path), video.Path, matches[0], cancellationToken).ConfigureAwait(false);
        }
        allRecords = await _stateStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var pending in allRecords.Where(item => item.Pending))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = videos.TryGetValue(pending.MediaKey, out var video) ? ResolveLocalPath(pending, video.SubtitleFiles) : null;
            try
            {
                if (path is not null && await CanConfirmPendingAsync(pending, path, cancellationToken).ConfigureAwait(false))
                    await _stateStore.MarkDownloadedAsync(pending with { Pending = false, LocalPath = path }, cancellationToken).ConfigureAwait(false);
                else if (pending.DownloadedAtUtc < DateTimeOffset.UtcNow.AddHours(-24))
                    await _stateStore.RemovePendingAsync(pending.MediaKey, pending.ReleaseId, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception) { _logger.LogWarning(exception, "Nie udało się potwierdzić ręcznego pobrania {ReleaseId}.", pending.ReleaseId); }
            catch (UnauthorizedAccessException exception) { _logger.LogWarning(exception, "Brak dostępu do ręcznie pobranego pliku {ReleaseId}.", pending.ReleaseId); }
        }
        allRecords = await _stateStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var ignoredRecords = allRecords.Count(record =>
            IsEligible(record) && ReleaseSelector.IsIgnoredGroup(record.GroupSlug, configuration.IgnoredGroupSlugs));
        var batchSize = Math.Clamp(configuration.UpdateBatchSize <= 0 ? 250 : configuration.UpdateBatchSize, 1, 250);
        var records = OrderForUpdate(allRecords
            .Where(IsEligible)
            .Where(record => AzureIllusionSubtitleProvider.IsSelectedLibrary(record.MediaPath, configuration.SelectedLibraryPaths))
            .Where(record => !ReleaseSelector.IsIgnoredGroup(record.GroupSlug, configuration.IgnoredGroupSlugs)))
            .ToArray();
        _totalRecords = records.Length;
        if (records.Length == 0)
        {
            _logger.LogInformation(
                "Polskie Napisy Anime: brak nowych wpisów z kompletem danych potrzebnych do bezpiecznej aktualizacji. Stare wpisy pozostają bez zmian.");
            await SaveReportAsync(startedAt, 0, 0, 0, ignoredRecords, 0, 0, 0, 0, 0, "completed", "Brak plików kwalifikujących się do aktualizacji.", cancellationToken).ConfigureAwait(false);
            progress.Report(100);
            return;
        }

        progress.Report(1);
        var checkedFiles = 0;
        var updatedFiles = 0;
        var unchangedFiles = 0;
        var sourceMissingFiles = 0;
        var localMissingFiles = 0;
        var conflicts = 0;
        var failedFiles = 0;
        var insufficientSpace = 0;
        try
        {
            foreach (var batch in BuildUpdateBatches(records, batchSize))
            {
            var revisions = await LoadRevisionsAsync(batch, cancellationToken).ConfigureAwait(false);
            var checks = new List<DownloadStateStore.CheckResult>();
            try
            {
            foreach (var record in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var checkRecorded = false;
                try
                {
                    if (ReleaseSelector.IsIgnoredGroup(record.GroupSlug, Plugin.Instance?.Configuration.IgnoredGroupSlugs)
                        || !AzureIllusionSubtitleProvider.IsSelectedLibrary(record.MediaPath, Plugin.Instance?.Configuration.SelectedLibraryPaths))
                    {
                        ignoredRecords++;
                        continue;
                    }
                    if (!videos.TryGetValue(record.MediaKey, out var video))
                    {
                        localMissingFiles++;
                        AddIssue(record, "media-not-found");
                        _logger.LogWarning(
                            "Polskie Napisy Anime: nie znaleziono pozycji Jellyfin dla zarządzanych napisów {ReleaseId}. Lokalny plik nie został usunięty.",
                            record.ReleaseId);
                        await _diagnostics.TryRecordAsync("warning", "MEDIA_NOT_FOUND", "Nie znaleziono filmu w bibliotece Jellyfin; napisy pozostają bez zmian.", record.ReleaseId, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                var localPath = ResolveLocalPath(record, video.SubtitleFiles);
                if (localPath is null)
                {
                    localMissingFiles++;
                    AddIssue(record, "local-file-missing");
                    _logger.LogWarning(
                        "Polskie Napisy Anime: nie udało się jednoznacznie odnaleźć lokalnego pliku dla {Path}, wydanie {ReleaseId}. Pominięto bez zmian.",
                        video.Path,
                        record.ReleaseId);
                    await _diagnostics.TryRecordAsync("warning", "LOCAL_FILE_MISSING", "Nie udało się jednoznacznie odnaleźć lokalnego pliku napisów.", record.ReleaseId, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!revisions.TryGetValue(record.ReleaseId, out var current))
                {
                    throw new InvalidDataException("API pominęło żądany identyfikator wydania; nie oznaczono go jako usunięte.");
                }

                if (!current.IsAvailable)
                {
                    sourceMissingFiles++;
                    AddIssue(record, "source-missing");
                    checks.Add(new(
                        record.MediaKey,
                        record.ReleaseId,
                        localPath,
                        true));
                    checkRecorded = true;
                    _logger.LogInformation(
                        "Polskie Napisy Anime: wydanie {ReleaseId} nie jest już dostępne na stronie. Lokalny plik {LocalPath} pozostaje bez zmian.",
                        record.ReleaseId,
                        localPath);
                    await _diagnostics.TryRecordAsync("warning", "SOURCE_RELEASE_MISSING", "Wydanie nie jest już dostępne w WebSubs; plik lokalny pozostaje bez zmian.", record.ReleaseId, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var currentRevision = SubtitleRevision.Build(
                    current.ChecksumSha256,
                    current.SizeBytes,
                    current.PublishedAt);
                var previousRevision = record.SourceRevision
                    ?? SubtitleRevision.Build(record.Checksum, null, null);
                if (currentRevision is null || string.Equals(currentRevision, previousRevision, StringComparison.Ordinal))
                {
                    unchangedFiles++;
                    checks.Add(new(
                        record.MediaKey,
                        record.ReleaseId,
                        localPath,
                        false));
                    checkRecorded = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(record.Checksum))
                {
                    conflicts++;
                    AddIssue(record, "conflict");
                    _logger.LogWarning(
                        "Polskie Napisy Anime: wydanie {ReleaseId} ma nową wersję, ale stary wpis nie zawiera sumy kontrolnej. Pominięto, aby nie nadpisać ręcznych zmian.",
                        record.ReleaseId);
                    await _diagnostics.TryRecordAsync("warning", "LOCAL_FILE_CONFLICT", "Brak starej sumy kontrolnej; plik nie został nadpisany.", record.ReleaseId, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var localChecksum = await ComputeSha256Async(localPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(localChecksum, record.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts++;
                    AddIssue(record, "conflict");
                    _logger.LogWarning(
                        "Polskie Napisy Anime: lokalny plik {LocalPath} został zmieniony poza pluginem. Aktualizacja {ReleaseId} została pominięta.",
                        localPath,
                        record.ReleaseId);
                    await _diagnostics.TryRecordAsync("warning", "LOCAL_FILE_CONFLICT", "Plik lokalny został zmieniony poza dodatkiem; aktualizacja pominięta.", record.ReleaseId, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var existingSize = new FileInfo(localPath).Length;
                var updateWorkingSize = Math.Max(existingSize, current.SizeBytes ?? 0);
                var space = DiskSpaceGuard.Check(localPath, updateWorkingSize, configuration.MinimumFreeSpaceMegabytes);
                if (space.WasChecked && !space.HasSpace)
                {
                    insufficientSpace++;
                    AddIssue(record, "insufficient-space");
                    _logger.LogWarning(
                        "Polskie Napisy Anime: aktualizacja {ReleaseId} pominięta z powodu miejsca. Dostępne: {AvailableBytes} B; wymagane: {RequiredBytes} B.",
                        record.ReleaseId,
                        space.AvailableBytes,
                        space.RequiredBytes);
                    await _diagnostics.TryRecordAsync("warning", "INSUFFICIENT_SPACE", "Za mało wolnego miejsca na bezpieczną aktualizację.", record.ReleaseId, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var downloaded = await DownloadToTemporaryFileAsync(current, localPath, cancellationToken).ConfigureAwait(false);
                try
                {
                    var checksumBeforeReplace = await ComputeSha256Async(localPath, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(checksumBeforeReplace, record.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts++;
                        AddIssue(record, "conflict");
                        _logger.LogWarning(
                            "Polskie Napisy Anime: plik {LocalPath} zmienił się w czasie aktualizacji. Bezpiecznie przerwano podmianę.",
                            localPath);
                        await _diagnostics.TryRecordAsync("warning", "LOCAL_FILE_CONFLICT", "Plik zmienił się w czasie aktualizacji; podmiana została przerwana.", record.ReleaseId, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await ReplaceAndCommitAsync(
                        record,
                        current,
                        currentRevision,
                        downloaded,
                        localPath,
                        configuration.KeepPreviousSubtitleBackup,
                        cancellationToken).ConfigureAwait(false);
                    checkRecorded = true;
                    updatedFiles++;
                    AddIssue(record, "updated");
                    await video.RefreshMetadata(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    DeletePluginTemporaryFile(downloaded.Path);
                }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failedFiles++;
                    AddIssue(record, "failed");
                    _logger.LogError(
                        exception,
                        "Polskie Napisy Anime: błąd aktualizacji wydania {ReleaseId}. Istniejący lokalny plik nie został usunięty.",
                        record.ReleaseId);
                    await _diagnostics.TryRecordAsync("error", "UPDATE_FAILED", "Aktualizacja napisu nie powiodła się; szczegóły są w logu Jellyfin.", record.ReleaseId, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (!checkRecorded && !cancellationToken.IsCancellationRequested)
                    {
                        checks.Add(new(record.MediaKey, record.ReleaseId, null, null));
                    }

                    checkedFiles++;
                    progress.Report(100d * checkedFiles / records.Length);
                    MaintenanceProgress.Report("update", 100d * checkedFiles / records.Length);
                }
            }
            }
            finally
            {
                // Commit completed records even when the scheduler cancels the next record.
                await _stateStore.MarkCheckedBatchAsync(checks, CancellationToken.None).ConfigureAwait(false);
            }
            }
        }
        catch (OperationCanceledException)
        {
            await SaveReportAsync(
                startedAt, checkedFiles, updatedFiles, unchangedFiles, ignoredRecords,
                sourceMissingFiles, localMissingFiles, conflicts, failedFiles, insufficientSpace,
                "cancelled", "Zadanie przerwano. Kolejny przebieg wznowi pracę od najdawniej sprawdzonych pozycji.",
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await SaveReportAsync(
                startedAt, checkedFiles, updatedFiles, unchangedFiles, ignoredRecords,
                sourceMissingFiles, localMissingFiles, conflicts, failedFiles + 1, insufficientSpace,
                "failed", exception.Message,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "Polskie Napisy Anime zakończyło aktualizację. Sprawdzono: {Checked}; zaktualizowano: {Updated}; bez zmian: {Unchanged}; pominięto przez ignorowane grupy: {Ignored}; za mało miejsca: {InsufficientSpace}; niedostępne na stronie: {SourceMissing}; brak lokalnego pliku: {LocalMissing}; konflikty ręcznych zmian: {Conflicts}; błędy: {Failed}. Żaden lokalny plik nie został usunięty z powodu braku na stronie.",
            checkedFiles,
            updatedFiles,
            unchangedFiles,
            ignoredRecords,
            insufficientSpace,
            sourceMissingFiles,
            localMissingFiles,
            conflicts,
            failedFiles);

        await SaveReportAsync(
            startedAt,
            checkedFiles,
            updatedFiles,
            unchangedFiles,
            ignoredRecords,
            sourceMissingFiles,
            localMissingFiles,
            conflicts,
            failedFiles,
            insufficientSpace,
            failedFiles > 0 ? "completed-with-errors" : "completed",
            null,
            cancellationToken).ConfigureAwait(false);

        if (failedFiles > 0)
        {
            throw new InvalidOperationException(
                $"Aktualizacja zakończyła się z błędami: {failedFiles}. Szczegóły znajdują się w logu Jellyfin.");
        }
    }

    private Dictionary<string, Video> FindVideos(CancellationToken cancellationToken)
    {
        var videos = new Dictionary<string, Video>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in _libraryManager.RootFolder.Children.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new InternalItemsQuery
            {
                MediaTypes = [MediaType.Video],
                IsVirtualItem = false,
                DtoOptions = new DtoOptions(true),
                SourceTypes = [SourceType.Library],
                Parent = library,
                Recursive = true,
            };

            foreach (var item in LibraryPager.Read(_libraryManager, query, cancellationToken))
            {
                if (item is Video video
                    && video.VideoType == VideoType.VideoFile
                    && !string.IsNullOrWhiteSpace(video.Path))
                {
                    var key = AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(video.Path);
                    videos.TryAdd(key, video);
                }
            }
        }

        return videos;
    }

    internal static IEnumerable<ManagedSubtitleDownload> OrderForUpdate(IEnumerable<ManagedSubtitleDownload> records)
        => records
            .OrderBy(record => record.LastCheckedAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(record => record.MediaKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.ReleaseId, StringComparer.Ordinal);

    internal static IEnumerable<ManagedSubtitleDownload[]> BuildUpdateBatches(IEnumerable<ManagedSubtitleDownload> records, int batchSize)
        => OrderForUpdate(records).Chunk(Math.Clamp(batchSize, 1, 250));

    internal static Dictionary<string, SubtitleRevisionItem> ValidateRevisionBatch(IEnumerable<string> requestedIds, IReadOnlyList<SubtitleRevisionItem> response)
    {
        var requested = requestedIds.ToHashSet(StringComparer.Ordinal);
        if (response.Count != requested.Count || response.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != requested.Count
            || response.Any(item => !requested.Contains(item.Id) || item.Status is not ("available" or "missing")))
            throw new InvalidDataException("Niepełna lub nieprawidłowa odpowiedź API wersji. Nie oznaczono wydań jako usunięte.");
        return response.ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    internal static async Task<bool> CanConfirmPendingAsync(ManagedSubtitleDownload pending, string localPath, CancellationToken cancellationToken)
        => !string.IsNullOrWhiteSpace(pending.Checksum) && File.Exists(localPath)
            && string.Equals(pending.Checksum, await ComputeSha256Async(localPath, cancellationToken).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase);

    private async Task<Dictionary<string, SubtitleRevisionItem>> LoadRevisionsAsync(
        IReadOnlyList<ManagedSubtitleDownload> records,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _apiClient.GetSubtitleRevisionsAsync(
                    records.Select(record => record.ReleaseId).Distinct(StringComparer.Ordinal).ToArray(),
                    cancellationToken).ConfigureAwait(false);
            return ValidateRevisionBatch(records.Select(record => record.ReleaseId), response);
        }
        catch (AzureIllusionApiException exception) when (exception.IsEndpointUnavailable)
        {
            _logger.LogWarning(
                exception,
                "Zbiorczy endpoint wersji nie jest jeszcze dostępny. Wtyczka użyje zgodnego trybu pojedynczych wyszukiwań dla tej porcji.");
            await _diagnostics.TryRecordAsync(
                "warning",
                "REVISION_BATCH_FALLBACK",
                "Zbiorcze sprawdzanie wersji jest niedostępne; użyto wolniejszego trybu zgodności.",
                null,
                cancellationToken).ConfigureAwait(false);

            var result = new Dictionary<string, SubtitleRevisionItem>(StringComparer.Ordinal);
            var releaseCache = new Dictionary<(string AniListId, double? Season, double? Episode, string Language, string Group), IReadOnlyList<SubtitleRelease>>();
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var release = await FindCurrentReleaseAsync(record, releaseCache, cancellationToken).ConfigureAwait(false);
                result[record.ReleaseId] = release is null
                    ? new SubtitleRevisionItem(record.ReleaseId, "missing", null, null, null, null)
                    : new SubtitleRevisionItem(
                        release.Id,
                        "available",
                        release.ChecksumSha256,
                        release.SizeBytes,
                        release.PublishedAt,
                        release.Version);
            }

            return result;
        }
    }

    private async Task<SubtitleRelease?> FindCurrentReleaseAsync(
        ManagedSubtitleDownload record,
        Dictionary<(string AniListId, double? Season, double? Episode, string Language, string Group), IReadOnlyList<SubtitleRelease>> releaseCache,
        CancellationToken cancellationToken)
    {
        var groups = string.IsNullOrWhiteSpace(record.GroupSlug) ? Array.Empty<string>() : [record.GroupSlug];
        var query = new SubtitleQuery(
            record.AniListId!, record.Season, record.Episode, [record.Language!], groups,
            VerifiedOnly: false, MinimumRating: 0, Limit: 100);
        var releases = await SearchCachedAsync(query, releaseCache, cancellationToken).ConfigureAwait(false);
        var release = releases.FirstOrDefault(item => string.Equals(item.Id, record.ReleaseId, StringComparison.Ordinal));
        if (release is not null || groups.Length == 0)
        {
            return release;
        }

        releases = await SearchCachedAsync(query with { Groups = [] }, releaseCache, cancellationToken).ConfigureAwait(false);
        return releases.FirstOrDefault(item => string.Equals(item.Id, record.ReleaseId, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<SubtitleRelease>> SearchCachedAsync(
        SubtitleQuery query,
        Dictionary<(string AniListId, double? Season, double? Episode, string Language, string Group), IReadOnlyList<SubtitleRelease>> releaseCache,
        CancellationToken cancellationToken)
    {
        var key = (query.AniListId, query.Season, query.Episode, string.Join(',', query.Languages), string.Join(',', query.Groups));
        if (releaseCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = await _apiClient.SearchSubtitlesAsync(query, cancellationToken).ConfigureAwait(false);
        releaseCache[key] = result.Releases;
        return result.Releases;
    }

    internal static string? ResolveLocalPath(
        ManagedSubtitleDownload record,
        IReadOnlyList<string>? subtitleFiles)
    {
        if (!string.IsNullOrWhiteSpace(record.LocalPath) && File.Exists(record.LocalPath))
        {
            return record.LocalPath;
        }

        if (string.IsNullOrWhiteSpace(record.StoredLanguage) || string.IsNullOrWhiteSpace(record.Format))
        {
            return null;
        }

        var suffix = string.Concat(
            ".",
            record.StoredLanguage,
            ".",
            record.Format.TrimStart('.')).ToLowerInvariant();
        var candidates = (subtitleFiles ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Where(path => path.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        if (candidates.Length > 1 || string.IsNullOrWhiteSpace(record.MediaPath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(record.MediaPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var predicted = Path.Combine(
            directory,
            string.Concat(
                Path.GetFileNameWithoutExtension(record.MediaPath),
                ".",
                record.StoredLanguage,
                ".",
                record.Format.TrimStart('.').ToLowerInvariant()));
        return File.Exists(predicted) ? predicted : null;
    }

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private async Task<DownloadedFile> DownloadToTemporaryFileAsync(
        SubtitleRevisionItem release,
        string localPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = string.Concat(localPath, ".pna-update-", Guid.NewGuid().ToString("N"), ".tmp");
        try
        {
            await using (var source = await _apiClient.DownloadSubtitleAsync(release.Id, cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 131072,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await using var verified = (await AzureIllusionSubtitleProvider.BufferVerifiedSubtitleAsync(
                    source, release.ChecksumSha256, cancellationToken).ConfigureAwait(false)).Stream;
                await verified.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var checksum = await ComputeSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(release.ChecksumSha256)
                && !string.Equals(checksum, release.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Pobrany plik ma inną sumę SHA-256 niż metadane na stronie.");
            }

            return new DownloadedFile(temporaryPath, checksum);
        }
        catch
        {
            DeletePluginTemporaryFile(temporaryPath);
            throw;
        }
    }

    private async Task ReplaceAndCommitAsync(
        ManagedSubtitleDownload record,
        SubtitleRevisionItem current,
        string? currentRevision,
        DownloadedFile downloaded,
        string localPath,
        bool keepPreviousBackup,
        CancellationToken cancellationToken)
    {
        var rollbackBackupPath = string.Concat(localPath, ".pna-rollback-", Guid.NewGuid().ToString("N"));
        var retainedBackupPath = string.Concat(localPath, ".pna-backup");
        var recoveryBackupPath = rollbackBackupPath;
        var replaced = false;
        var committedOrRestored = false;
        var journal = await SubtitleRecoveryJournal.BeginAsync(Path.Combine(Plugin.Instance!.StateDirectory, "transactions"),
            new SubtitleTransaction(record.MediaKey, record.ReleaseId, localPath, rollbackBackupPath, retainedBackupPath,
                record.Checksum!, downloaded.Checksum, currentRevision), cancellationToken).ConfigureAwait(false);
        try
        {
            File.Replace(downloaded.Path, localPath, rollbackBackupPath, ignoreMetadataErrors: true);
            replaced = true;
            if (keepPreviousBackup)
            {
                File.Move(rollbackBackupPath, retainedBackupPath, overwrite: true);
                recoveryBackupPath = retainedBackupPath;
            }

            await _stateStore.MarkUpdatedAsync(
                record.MediaKey,
                record.ReleaseId,
                current.ChecksumSha256 ?? downloaded.Checksum,
                currentRevision,
                localPath,
                cancellationToken).ConfigureAwait(false);
            committedOrRestored = true;
        }
        catch
        {
            if (replaced && File.Exists(recoveryBackupPath))
            {
                File.Replace(recoveryBackupPath, localPath, null, ignoreMetadataErrors: true);
                committedOrRestored = true;
            }

            throw;
        }
        finally
        {
            // A failed restore must never destroy the only recovery copy.
            if (!replaced || committedOrRestored) DeletePluginTemporaryFile(rollbackBackupPath);
            else _logger.LogCritical("Nie udało się przywrócić napisów. Zachowano kopię ratunkową: {Path}", recoveryBackupPath);
            if (!replaced || committedOrRestored) DeletePluginTemporaryFile(journal);
        }
    }

    private static bool IsEligible(ManagedSubtitleDownload record)
        => !record.Pending && !string.IsNullOrWhiteSpace(record.MediaPath)
            && !string.IsNullOrWhiteSpace(record.Language)
            && !string.IsNullOrWhiteSpace(record.StoredLanguage)
            && !string.IsNullOrWhiteSpace(record.Format)
            && !string.IsNullOrWhiteSpace(record.AniListId);

    private static void DeletePluginTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A uniquely named plugin temporary file can be cleaned on the next maintenance pass.
        }
        catch (UnauthorizedAccessException)
        {
            // Do not turn a successful subtitle update into a failure because backup cleanup was denied.
        }
    }

    private Task SaveReportAsync(
        DateTimeOffset startedAt,
        int checkedFiles,
        int updatedFiles,
        int unchangedFiles,
        int ignoredRecords,
        int sourceMissingFiles,
        int localMissingFiles,
        int conflicts,
        int failedFiles,
        int insufficientSpace,
        string status,
        string? message,
        CancellationToken cancellationToken)
    {
        var report = new PluginTaskReport(
            "update-downloaded",
            "Aktualizacja pobranych napisów",
            false,
            startedAt,
            DateTimeOffset.UtcNow,
            status,
            new Dictionary<string, int>
            {
                ["checked"] = checkedFiles,
                ["updated"] = updatedFiles,
                ["unchanged"] = unchangedFiles,
                ["ignored"] = ignoredRecords,
                ["sourceMissing"] = sourceMissingFiles,
                ["localMissing"] = localMissingFiles,
                ["conflicts"] = conflicts,
                ["failed"] = failedFiles,
                ["insufficientSpace"] = insufficientSpace,
            }.Concat(TaskOperationMetrics.Snapshot()).ToDictionary(x => x.Key, x => x.Value),
            _reportItems.ToArray(),
            message, Total: _totalRecords);
        return _reportStore.SaveAsync(report, cancellationToken);
    }

    private void AddIssue(ManagedSubtitleDownload record, string result)
    {
        if (_reportItems.Count >= TaskReportStore.MaximumItems)
        {
            var successful = _reportItems.FindIndex(x => x.Result == "updated");
            if (successful < 0 || result == "updated") return;
            _reportItems.RemoveAt(successful);
        }
        _reportItems.Add(new(record.MediaPath ?? record.MediaKey, record.Language ?? "", record.ReleaseId,
            record.GroupName ?? record.GroupSlug, null, result));
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    private sealed record DownloadedFile(string Path, string Checksum);
}
