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
    private readonly ILogger<UpdateDownloadedSubtitlesTask> _logger;

    public UpdateDownloadedSubtitlesTask(
        AzureIllusionApiClient apiClient,
        DownloadStateStore stateStore,
        ILibraryManager libraryManager,
        TaskReportStore reportStore,
        ILogger<UpdateDownloadedSubtitlesTask> logger)
    {
        _apiClient = apiClient;
        _stateStore = stateStore;
        _libraryManager = libraryManager;
        _reportStore = reportStore;
        _logger = logger;
    }

    public string Name => "Polskie Napisy Anime — aktualizuj pobrane napisy";

    public string Key => "PolskieNapisyAnimeUpdateDownloadedSubtitles";

    public string Description =>
        "Aktualizuje wyłącznie pliki wcześniej pobrane przez plugin. Nie usuwa napisów, które zniknęły ze strony, i nie nadpisuje plików zmienionych ręcznie.";

    public string Category => "Polskie Napisy Anime";

    public bool IsHidden => false;

    public bool IsEnabled => true;

    public bool IsLogged => true;

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!await SubtitleTaskExecutionGate.Instance.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Inne zadanie napisów Polskie Napisy Anime jest już uruchomione.");
        }

        try
        {
            await ExecuteCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SubtitleTaskExecutionGate.Instance.Release();
        }
    }

    private async Task ExecuteCoreAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
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

        var allRecords = await _stateStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var ignoredRecords = allRecords.Count(record =>
            IsEligible(record) && ReleaseSelector.IsIgnoredGroup(record.GroupSlug, configuration.IgnoredGroupSlugs));
        var records = allRecords
            .Where(IsEligible)
            .Where(record => !ReleaseSelector.IsIgnoredGroup(record.GroupSlug, configuration.IgnoredGroupSlugs))
            .ToArray();
        if (records.Length == 0)
        {
            _logger.LogInformation(
                "Polskie Napisy Anime: brak nowych wpisów z kompletem danych potrzebnych do bezpiecznej aktualizacji. Stare wpisy pozostają bez zmian.");
            await SaveReportAsync(startedAt, 0, 0, 0, ignoredRecords, 0, 0, 0, 0, 0, "completed", "Brak plików kwalifikujących się do aktualizacji.", cancellationToken).ConfigureAwait(false);
            progress.Report(100);
            return;
        }

        var videos = FindVideos(cancellationToken);
        var checkedFiles = 0;
        var updatedFiles = 0;
        var unchangedFiles = 0;
        var sourceMissingFiles = 0;
        var localMissingFiles = 0;
        var conflicts = 0;
        var failedFiles = 0;
        var insufficientSpace = 0;

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!videos.TryGetValue(record.MediaKey, out var video))
                {
                    localMissingFiles++;
                    _logger.LogWarning(
                        "Polskie Napisy Anime: nie znaleziono pozycji Jellyfin dla zarządzanych napisów {ReleaseId}. Lokalny plik nie został usunięty.",
                        record.ReleaseId);
                    continue;
                }

                var localPath = ResolveLocalPath(record, video.SubtitleFiles);
                if (localPath is null)
                {
                    localMissingFiles++;
                    _logger.LogWarning(
                        "Polskie Napisy Anime: nie udało się jednoznacznie odnaleźć lokalnego pliku dla {Path}, wydanie {ReleaseId}. Pominięto bez zmian.",
                        video.Path,
                        record.ReleaseId);
                    continue;
                }

                var current = await FindCurrentReleaseAsync(record, cancellationToken).ConfigureAwait(false);
                if (current is null)
                {
                    sourceMissingFiles++;
                    await _stateStore.MarkCheckedAsync(
                        record.MediaKey,
                        record.ReleaseId,
                        localPath,
                        sourceMissing: true,
                        cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Polskie Napisy Anime: wydanie {ReleaseId} nie jest już dostępne na stronie. Lokalny plik {LocalPath} pozostaje bez zmian.",
                        record.ReleaseId,
                        localPath);
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
                    await _stateStore.MarkCheckedAsync(
                        record.MediaKey,
                        record.ReleaseId,
                        localPath,
                        sourceMissing: false,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(record.Checksum))
                {
                    conflicts++;
                    _logger.LogWarning(
                        "Polskie Napisy Anime: wydanie {ReleaseId} ma nową wersję, ale stary wpis nie zawiera sumy kontrolnej. Pominięto, aby nie nadpisać ręcznych zmian.",
                        record.ReleaseId);
                    continue;
                }

                var localChecksum = await ComputeSha256Async(localPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(localChecksum, record.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts++;
                    _logger.LogWarning(
                        "Polskie Napisy Anime: lokalny plik {LocalPath} został zmieniony poza pluginem. Aktualizacja {ReleaseId} została pominięta.",
                        localPath,
                        record.ReleaseId);
                    continue;
                }

                var existingSize = new FileInfo(localPath).Length;
                var updateWorkingSize = Math.Max(existingSize, current.SizeBytes);
                var space = DiskSpaceGuard.Check(localPath, updateWorkingSize, configuration.MinimumFreeSpaceMegabytes);
                if (space.WasChecked && !space.HasSpace)
                {
                    insufficientSpace++;
                    _logger.LogWarning(
                        "Polskie Napisy Anime: aktualizacja {ReleaseId} pominięta z powodu miejsca. Dostępne: {AvailableBytes} B; wymagane: {RequiredBytes} B.",
                        record.ReleaseId,
                        space.AvailableBytes,
                        space.RequiredBytes);
                    continue;
                }

                var downloaded = await DownloadToTemporaryFileAsync(current, localPath, cancellationToken).ConfigureAwait(false);
                try
                {
                    var checksumBeforeReplace = await ComputeSha256Async(localPath, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(checksumBeforeReplace, record.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts++;
                        _logger.LogWarning(
                            "Polskie Napisy Anime: plik {LocalPath} zmienił się w czasie aktualizacji. Bezpiecznie przerwano podmianę.",
                            localPath);
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
                    updatedFiles++;
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
                _logger.LogError(
                    exception,
                    "Polskie Napisy Anime: błąd aktualizacji wydania {ReleaseId}. Istniejący lokalny plik nie został usunięty.",
                    record.ReleaseId);
            }
            finally
            {
                checkedFiles++;
                progress.Report(100d * checkedFiles / records.Length);
            }
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
                IncludeItemTypes = [BaseItemKind.Episode, BaseItemKind.Movie],
                DtoOptions = new DtoOptions(true),
                SourceTypes = [SourceType.Library],
                Parent = library,
                Recursive = true,
            };

            foreach (var item in _libraryManager.GetItemList(query))
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

    private async Task<SubtitleRelease?> FindCurrentReleaseAsync(
        ManagedSubtitleDownload record,
        CancellationToken cancellationToken)
    {
        var groups = string.IsNullOrWhiteSpace(record.GroupSlug) ? Array.Empty<string>() : [record.GroupSlug];
        var query = new SubtitleQuery(
            record.AniListId!,
            record.Season,
            record.Episode,
            [record.Language!],
            groups,
            VerifiedOnly: false,
            MinimumRating: 0,
            Limit: 100);
        var result = await _apiClient.SearchSubtitlesAsync(query, cancellationToken).ConfigureAwait(false);
        var release = result.Releases.FirstOrDefault(item => string.Equals(item.Id, record.ReleaseId, StringComparison.Ordinal));
        if (release is not null || groups.Length == 0)
        {
            return release;
        }

        result = await _apiClient.SearchSubtitlesAsync(query with { Groups = [] }, cancellationToken).ConfigureAwait(false);
        return result.Releases.FirstOrDefault(item => string.Equals(item.Id, record.ReleaseId, StringComparison.Ordinal));
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
        SubtitleRelease release,
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
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
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
        SubtitleRelease current,
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
        }
        catch
        {
            if (replaced && File.Exists(recoveryBackupPath))
            {
                File.Replace(recoveryBackupPath, localPath, null, ignoreMetadataErrors: true);
            }

            throw;
        }
        finally
        {
            DeletePluginTemporaryFile(rollbackBackupPath);
        }
    }

    private static bool IsEligible(ManagedSubtitleDownload record)
        => !string.IsNullOrWhiteSpace(record.MediaPath)
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
            },
            [],
            message);
        return _reportStore.SaveAsync(report, cancellationToken);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        =>
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(4.5).Ticks,
                MaxRuntimeTicks = TimeSpan.FromHours(2).Ticks,
            },
        ];

    private sealed record DownloadedFile(string Path, string Checksum);
}
