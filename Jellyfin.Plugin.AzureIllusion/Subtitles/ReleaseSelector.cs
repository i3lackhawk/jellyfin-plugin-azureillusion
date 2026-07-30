using Jellyfin.Plugin.AzureIllusion.Api;

namespace Jellyfin.Plugin.AzureIllusion.Subtitles;

/// <summary>Applies deterministic group limits while preserving API ranking.</summary>
public static class ReleaseSelector
{
    /// <summary>Removes releases belonging to groups ignored by the administrator.</summary>
    public static IReadOnlyList<SubtitleRelease> ExcludeGroups(IEnumerable<SubtitleRelease> releases, IReadOnlyList<string>? ignoredGroupSlugs)
    {
        var ordered = releases.ToArray();
        var ignored = (ignoredGroupSlugs ?? [])
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ignored.Count == 0
            ? ordered
            : ordered.Where(release => !IsIgnoredGroup(release.Group?.Slug, ignored)).ToArray();
    }

    /// <summary>Checks whether a group slug is present on the administrator ignore list.</summary>
    public static bool IsIgnoredGroup(string? groupSlug, IReadOnlyCollection<string>? ignoredGroupSlugs)
    {
        if (string.IsNullOrWhiteSpace(groupSlug) || ignoredGroupSlugs is null || ignoredGroupSlugs.Count == 0)
        {
            return false;
        }

        return ignoredGroupSlugs.Contains(groupSlug.Trim(), StringComparer.OrdinalIgnoreCase);
    }

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
