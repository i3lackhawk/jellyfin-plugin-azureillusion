using Jellyfin.Plugin.AzureIllusion.Api;

namespace Jellyfin.Plugin.AzureIllusion.Subtitles;

/// <summary>Applies deterministic group limits while preserving API ranking.</summary>
public static class ReleaseSelector
{
    /// <summary>Selects releases from at most the configured number of distinct groups.</summary>
    public static IReadOnlyList<SubtitleRelease> LimitGroups(IEnumerable<SubtitleRelease> releases, int maximumGroups)
    {
        var ordered = releases.ToArray();
        if (maximumGroups <= 0)
        {
            return ordered;
        }

        var acceptedGroups = ordered
            .Select(release => release.Group?.Slug ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumGroups)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ordered.Where(release => acceptedGroups.Contains(release.Group?.Slug ?? string.Empty)).ToArray();
    }
}
