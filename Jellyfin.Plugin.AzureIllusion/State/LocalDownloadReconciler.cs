using Jellyfin.Plugin.AzureIllusion.ScheduledTasks;
using Jellyfin.Plugin.AzureIllusion.Subtitles;

namespace Jellyfin.Plugin.AzureIllusion.State;

internal sealed record LocalMedia(string Path, Guid? Id, IReadOnlyList<string> SubtitleFiles, string? MetadataDirectory = null)
{
    internal static LocalMedia FromVideo(MediaBrowser.Controller.Entities.Video video)
        => new(video.Path, video.Id, video.SubtitleFiles ?? [], video.GetInternalMetadataPath());
}
internal sealed record LocalReconciliationItem(ManagedSubtitleDownload Download, string Result);
internal sealed record LocalReconciliation(IReadOnlyList<LocalReconciliationItem> Items, IReadOnlySet<string> BlockedReleases, IReadOnlySet<string> PresentReleases);

/// <summary>Recovers completed saves before searching, independently of upstream update settings.</summary>
internal sealed class LocalDownloadReconciler(DownloadStateStore store)
{
    // Bounded stripes serialize check/download/save for the same video without an unbounded lock cache.
    private static readonly SemaphoreSlim[] DownloadGates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    internal async Task<LocalReconciliation> ReconcileAsync(LocalMedia media, CancellationToken token, bool persist = true)
    {
        var key = AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(media.Path);
        var records = await store.GetForMediaAsync(key, media.Id, token).ConfigureAwait(false);
        var items = new List<LocalReconciliationItem>();
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            var moved = !string.Equals(record.MediaKey, key, StringComparison.OrdinalIgnoreCase);
            try
            {
                if (!moved && !record.Pending && !string.IsNullOrWhiteSpace(record.LocalPath) && ExistsStrict(record.LocalPath)) continue;
                if (string.IsNullOrWhiteSpace(record.StoredLanguage) || string.IsNullOrWhiteSpace(record.Format))
                {
                    // Legacy confirmed entries keep their existing deduplication semantics.
                    if (moved || record.Pending) Block("local-conflict");
                    continue;
                }
                var candidates = CandidatePaths(media, record);
                if (!moved && !string.IsNullOrWhiteSpace(record.LocalPath)) candidates = candidates.Append(record.LocalPath);
                var existing = candidates.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Where(ExistsStrict).ToArray();
                if (existing.Length == 0) continue;
                if (string.IsNullOrWhiteSpace(record.Checksum)) { Block("local-conflict"); continue; }
                var matches = new List<string>();
                foreach (var path in existing)
                    if (string.Equals(await UpdateDownloadedSubtitlesTask.ComputeSha256Async(path, token).ConfigureAwait(false), record.Checksum, StringComparison.OrdinalIgnoreCase))
                        matches.Add(path);
                if (matches.Count == 0 && record.Pending && !moved)
                {
                    // Jellyfin uses CreateNew and a numbered filename, never overwrites this unconfirmed file.
                    // Allow retry, then confirm only the uniquely matching complete copy.
                    items.Add(new(record, "incomplete-local"));
                    continue;
                }
                if (matches.Count != 1) { Block("local-conflict"); continue; }
                if (!persist) { present.Add(record.ReleaseId); continue; }
                if (await store.ConfirmLocalAsync(record, key, media.Path, matches[0], media.Id, token).ConfigureAwait(false))
                {
                    present.Add(record.ReleaseId);
                    items.Add(new(record, moved ? "rebound-local" : "confirmed-local"));
                }
                else if (!await store.ContainsAsync(key, record.ReleaseId, record.Checksum, token).ConfigureAwait(false)) Block("local-conflict");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Block("local-unavailable");
            }

            void Block(string result) { blocked.Add(record.ReleaseId); items.Add(new(record, result)); }
        }
        return new(items, blocked, present);
    }

    internal async Task<string> DownloadIfMissingAsync(LocalMedia media, SubtitleIdPayload payload,
        Func<CancellationToken, Task> download, CancellationToken token)
    {
        var key = AzureIllusionSubtitleProvider.BuildMediaKeyFromPath(media.Path);
        var gate = DownloadGates[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key) % (uint)DownloadGates.Length];
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var local = await ReconcileAsync(media, token).ConfigureAwait(false);
            if (local.BlockedReleases.Contains(payload.ReleaseId)) return local.Items.Last(x => x.Download.ReleaseId == payload.ReleaseId).Result;
            if (await store.ContainsAsync(key, payload.ReleaseId, payload.Checksum, token).ConfigureAwait(false)) return "already-downloaded";
            await download(token).ConfigureAwait(false);
            await ReconcileAsync(media, token).ConfigureAwait(false);
            var saved = await store.GetForMediaAsync(key, media.Id, token).ConfigureAwait(false);
            if (!saved.Any(x => x.MediaKey.Equals(key, StringComparison.OrdinalIgnoreCase) && x.ReleaseId == payload.ReleaseId
                && !x.Pending && !string.IsNullOrWhiteSpace(x.LocalPath) && ExistsStrict(x.LocalPath)))
                throw new IOException("Jellyfin nie potwierdził kompletnego pliku napisów. Zapis pozostaje do odzyskania; nie naliczono pobrania.");
            return "downloaded";
        }
        finally { gate.Release(); }
    }

    internal static bool ExistsStrict(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Directory) == 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException)
        {
            // A missing parent may be a disconnected mount, not a deleted subtitle.
            if (!Directory.Exists(Path.GetDirectoryName(path))) throw;
            return false;
        }
    }

    private static IEnumerable<string> CandidatePaths(LocalMedia media, ManagedSubtitleDownload record)
    {
        foreach (var path in media.SubtitleFiles)
            if (MatchesManagedName(path, record.StoredLanguage!, record.Format!)) yield return path;
        var mediaDirectory = Path.GetDirectoryName(Path.GetFullPath(media.Path))!;
        var directories = new[] { mediaDirectory, media.MetadataDirectory }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var prefix = Path.GetFileNameWithoutExtension(media.Path) + ".";
        foreach (var directory in directories)
        {
            string[] paths;
            try { paths = Directory.GetFiles(directory!); }
            catch (DirectoryNotFoundException) when (directory != mediaDirectory) { continue; }
            foreach (var path in paths)
                if (Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && MatchesManagedName(path, record.StoredLanguage!, record.Format!)) yield return path;
        }
    }

    internal static bool MatchesManagedName(string path, string storedLanguage, string format)
    {
        if (!Path.GetExtension(path).Equals("." + format.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) return false;
        var stem = Path.GetFileNameWithoutExtension(path);
        var marker = "." + storedLanguage;
        var index = stem.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        var tail = stem[(index + marker.Length)..];
        // Jellyfin lowercases the language and adds .0, .1, ... if a name is occupied.
        return tail.Length == 0 || tail.Length > 1 && tail[0] == '.' && tail.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
    }
}
