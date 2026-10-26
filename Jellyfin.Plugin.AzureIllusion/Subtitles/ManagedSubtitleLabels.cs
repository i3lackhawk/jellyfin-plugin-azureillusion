using Jellyfin.Plugin.AzureIllusion.State;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AzureIllusion.Subtitles;

/// <summary>Human-readable stream metadata; managed filenames and identity remain unchanged.</summary>
internal static class ManagedSubtitleLabels
{
    internal static int Apply(string mediaPath, IReadOnlyList<MediaStream> streams, IReadOnlyList<ManagedSubtitleDownload> records)
    {
        var changed = 0;
        foreach (var stream in streams)
        {
            if (stream.Type != MediaStreamType.Subtitle || !stream.IsExternal || string.IsNullOrWhiteSpace(stream.Path)) continue;
            var owners = records.Where(record => Owns(mediaPath, stream.Path, record)).ToArray();
            if (owners.Length != 1) continue;
            var owner = owners[0];
            var language = AzureIllusionSubtitleProvider.ToThreeLetterIso(owner.Language!);
            if (!string.IsNullOrWhiteSpace(stream.Language)
                && !string.Equals(stream.Language, owner.StoredLanguage, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(AzureIllusionSubtitleProvider.ToThreeLetterIso(stream.Language), language, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsGeneratedTitle(stream.Title, owner.StoredLanguage!)) continue;
            var title = string.IsNullOrWhiteSpace(owner.GroupName) ? "Polskie Napisy Anime"
                : string.Join(' ', owner.GroupName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (stream.Title == title && stream.Language == language) continue;
            stream.Title = title;
            stream.Language = language;
            changed++;
        }
        return changed;
    }

    private static bool Owns(string mediaPath, string path, ManagedSubtitleDownload record)
    {
        if (string.IsNullOrWhiteSpace(record.Language) || string.IsNullOrWhiteSpace(record.StoredLanguage)
            || string.IsNullOrWhiteSpace(record.Format)) return false;
        if (!string.IsNullOrWhiteSpace(record.LocalPath))
            return string.Equals(path, record.LocalPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        // Pending/manual saves may not yet have LocalPath. Require the full
        // managed name, not just a hash-looking title from another provider.
        var prefix = Path.GetFileNameWithoutExtension(mediaPath) + ".";
        return Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && LocalDownloadReconciler.MatchesManagedName(path, record.StoredLanguage, record.Format);
    }

    private static bool IsGeneratedTitle(string? title, string storedLanguage)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        var dot = storedLanguage.IndexOf('.');
        var generated = dot < 0 ? storedLanguage : storedLanguage[(dot + 1)..];
        return Matches(title, generated) || Matches(title, storedLanguage);

        static bool Matches(string title, string generated)
        {
            if (title.Equals(generated, StringComparison.OrdinalIgnoreCase)) return true;
            if (!title.StartsWith(generated + ".", StringComparison.OrdinalIgnoreCase)) return false;
            var tail = title.AsSpan(generated.Length + 1);
            return tail.Length > 0 && tail.IndexOfAnyExceptInRange('0', '9') < 0;
        }
    }
}
