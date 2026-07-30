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

    /// <summary>Moves preferred groups to the front while preserving API ranking inside every priority bucket.</summary>
    public static IReadOnlyList<SubtitleRelease> ApplyGroupPriority(IEnumerable<SubtitleRelease> releases, IReadOnlyList<string>? priorityGroupSlugs)
    {
        var priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in priorityGroupSlugs ?? [])
        {
            if (!string.IsNullOrWhiteSpace(slug) && !priority.ContainsKey(slug.Trim()))
            {
                priority[slug.Trim()] = priority.Count;
            }
        }

        return releases
            .Select((release, index) => new
            {
                Release = release,
                OriginalIndex = index,
                Priority = release.Group?.Slug is { } slug && priority.TryGetValue(slug, out var value) ? value : int.MaxValue,
            })
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.Release)
            .ToArray();
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
