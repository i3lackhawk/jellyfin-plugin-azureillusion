using System.Text.Json;
using Jellyfin.Plugin.AzureIllusion.ScheduledTasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.State;

internal sealed record SubtitleTransaction(string MediaKey, string ReleaseId, string Target, string Backup, string RetainedBackup,
    string OldChecksum, string NewChecksum, string? Revision);

internal static class SubtitleRecoveryJournal
{
    public static async Task<string> BeginAsync(string directory, SubtitleTransaction transaction, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, transaction, cancellationToken: token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        File.Move(temp, path);
        return path;
    }

    public static async Task RecoverAsync(string directory, DownloadStateStore store, ILogger logger, CancellationToken token)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.GetFiles(directory, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var transaction = JsonSerializer.Deserialize<SubtitleTransaction>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false))
                    ?? throw new InvalidDataException("Pusty dziennik podmiany.");
                if (!Path.IsPathFullyQualified(transaction.Target)
                    || !transaction.Backup.StartsWith(transaction.Target + ".pna-rollback-", StringComparison.Ordinal)
                    || transaction.RetainedBackup != transaction.Target + ".pna-backup")
                    throw new InvalidDataException("Nieprawidłowe ścieżki dziennika podmiany.");
                var checksum = File.Exists(transaction.Target)
                    ? await UpdateDownloadedSubtitlesTask.ComputeSha256Async(transaction.Target, token).ConfigureAwait(false) : null;
                var records = await store.GetAllAsync(token).ConfigureAwait(false);
                var committed = records.Any(x => x.MediaKey == transaction.MediaKey && x.ReleaseId == transaction.ReleaseId
                    && x.Checksum == transaction.NewChecksum && x.SourceRevision == transaction.Revision);
                if (committed && checksum == transaction.NewChecksum) { File.Delete(path); continue; }
                if (checksum == transaction.OldChecksum) { File.Delete(path); continue; }
                // Only roll back our own verified replacement; never overwrite an unrelated manual edit.
                if (checksum is not null && checksum != transaction.NewChecksum)
                    throw new InvalidDataException("Plik zmieniono poza dodatkiem; zachowano dziennik i kopię ratunkową.");
                var backup = File.Exists(transaction.Backup) ? transaction.Backup : transaction.RetainedBackup;
                if (!File.Exists(backup) || await UpdateDownloadedSubtitlesTask.ComputeSha256Async(backup, token).ConfigureAwait(false) != transaction.OldChecksum)
                    throw new InvalidDataException("Brak zweryfikowanej kopii do odzyskania.");
                if (File.Exists(transaction.Target)) File.Replace(backup, transaction.Target, null, true);
                else File.Copy(backup, transaction.Target);
                File.Delete(path);
                logger.LogWarning("Przywrócono przerwaną podmianę napisu {ReleaseId}.", transaction.ReleaseId);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.LogCritical(ex, "Nie można bezpiecznie zakończyć odzyskiwania {Journal}. Automatyczna aktualizacja zatrzymana.", path);
                throw;
            }
        }
    }
}
