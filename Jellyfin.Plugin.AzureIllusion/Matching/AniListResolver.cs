using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.AzureIllusion.Api;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.Matching;

/// <summary>Resolves Jellyfin metadata to the AniList identifier used by AzureIllusion.</summary>
public sealed partial class AniListResolver
{
    private static readonly string[] AniListKeys = ["anilist", "ani-list", "anilistid"];
    private readonly AzureIllusionApiClient _apiClient;
    private readonly AnimeMatchCache _cache;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<AniListResolver> _logger;

    /// <summary>Initializes the resolver.</summary>
    public AniListResolver(AzureIllusionApiClient apiClient, AnimeMatchCache cache, ILibraryManager libraryManager, ILogger<AniListResolver> logger)
    {
        _apiClient = apiClient;
        _cache = cache;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>Resolves an AniList identifier without sending foreign identifiers to the website.</summary>
    public async Task<AnimeMatch?> ResolveAsync(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        Episode? episode = null;
        Season? season = null;
        if (request.ContentType == MediaBrowser.Controller.Providers.VideoContentType.Episode
            && !string.IsNullOrWhiteSpace(request.MediaPath))
        {
            episode = _libraryManager.FindByPath(request.MediaPath, false) as Episode;
            season = episode?.Season;
        }

        var seasonNumber = season?.IndexNumber ?? request.ParentIndexNumber;

        var seriesId = episode?.SeriesId;
        var manualId = ResolveLocalMapping(
            request,
            GetConfiguration().ExternalIdMappingsJson,
            episode?.Id,
            seriesId == Guid.Empty ? null : seriesId);
        if (IsPositiveInteger(manualId))
        {
            return new AnimeMatch(manualId!, "administrator mapping", true);
        }

        var directId = FindProviderId(request.ProviderIds, AniListKeys);
        if (IsPositiveInteger(directId))
        {
            return new AnimeMatch(directId!, "Jellyfin AniList ID", true);
        }

        var episodeId = FindProviderId(episode?.ProviderIds, AniListKeys);
        if (IsPositiveInteger(episodeId))
        {
            return new AnimeMatch(episodeId!, "Jellyfin episode AniList ID", true);
        }

        // Metadata providers attach a separate AniList entry to every Jellyfin
        // season even though Jellyfin presents them below one parent series.
        var seasonId = FindProviderId(season?.ProviderIds, AniListKeys);
        if (IsPositiveInteger(seasonId))
        {
            return new AnimeMatch(seasonId!, "Jellyfin season AniList ID", true);
        }

        // A Jellyfin series often has the AniList ID while its episodes do not.
        // It is safe to inherit that ID for season one only: later seasons have
        // distinct AniList entries and require a season-specific mapping.
        if (episode is not null && seasonNumber is null or 1)
        {
            var series = episode.Series;
            var inheritedId = FindProviderId(series?.ProviderIds, AniListKeys);
            if (IsPositiveInteger(inheritedId))
            {
                return new AnimeMatch(inheritedId!, "Jellyfin series AniList ID", true);
            }
        }

        if (!GetConfiguration().EnableExactTitleFallback)
        {
            return null;
        }

        var titleCandidates = BuildTitleFallbacks(
            request.ContentType == MediaBrowser.Controller.Providers.VideoContentType.Episode ? request.SeriesName : request.Name,
            season?.Name,
            seasonNumber);
        if (titleCandidates.Count == 0)
        {
            return null;
        }

        var productionYear = season?.ProductionYear ?? request.ProductionYear;
        foreach (var title in titleCandidates)
        {
            var cacheKey = $"{NormalizeTitle(title)}|{productionYear?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
            var match = await _cache.GetOrCreateAsync(
                cacheKey,
                token => ResolveTitleAsync(title, productionYear, token),
                cancellationToken).ConfigureAwait(false);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> BuildTitleFallbacks(string? seriesTitle, string? seasonTitle, int? seasonNumber)
    {
        var titles = new List<string>();
        static bool IsGenericSeasonTitle(string value)
            => Regex.IsMatch(value.Trim(), @"^(?:season|sezon)\s*\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || titles.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase)) return;
            titles.Add(value.Trim());
        }

        if (!string.IsNullOrWhiteSpace(seasonTitle) && !IsGenericSeasonTitle(seasonTitle)) Add(seasonTitle);
        if (!string.IsNullOrWhiteSpace(seriesTitle) && seasonNumber is > 1)
        {
            Add($"{seriesTitle} Season {seasonNumber}");
            Add($"{seriesTitle} {OrdinalSeason(seasonNumber.Value)} Season");
        }
        else
        {
            Add(seriesTitle);
        }
        return titles;
    }

    private static string OrdinalSeason(int number)
    {
        var remainder100 = number % 100;
        if (remainder100 is >= 11 and <= 13) return $"{number}th";
        var suffix = (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{number}{suffix}";
    }

    private async Task<AnimeMatch?> ResolveTitleAsync(string title, int? productionYear, CancellationToken cancellationToken)
    {
        var candidates = await _apiClient.SearchAnimeAsync(title, productionYear, cancellationToken).ConfigureAwait(false);
        var selected = SelectExactTitleCandidate(candidates, title, productionYear, false);
        if (selected is not null)
        {
            return new AnimeMatch(selected.AniListId!, "exact title and year", true);
        }

        // Jellyfin can copy the parent series year to every Season item.  A
        // later season then has a correct, season-specific title but an
        // incorrect year (for example 2019 instead of 2022).  Retry without
        // the year and accept only one exact title, so the fallback remains
        // deterministic and cannot silently select a similarly named anime.
        if (productionYear.HasValue)
        {
            candidates = await _apiClient.SearchAnimeAsync(title, null, cancellationToken).ConfigureAwait(false);
            selected = SelectExactTitleCandidate(candidates, title, null, true);
            if (selected is not null)
            {
                return new AnimeMatch(selected.AniListId!, "unique exact title (Jellyfin season year ignored)", true);
            }
        }

        _logger.LogInformation(
            "AzureIllusion did not choose an ambiguous title match for {Title}.",
            title);
        return null;
    }

    internal static AnimeItem? SelectExactTitleCandidate(
        IReadOnlyList<AnimeItem> candidates,
        string title,
        int? productionYear,
        bool allowYearMismatch)
    {
        var normalized = NormalizeTitle(title);
        var exact = candidates
            .Where(item => item.AniListId is not null)
            .Where(item => CandidateTitles(item).Any(candidate => NormalizeTitle(candidate) == normalized))
            .ToArray();
        var exactYear = productionYear.HasValue
            ? exact.Where(item => item.Year == productionYear).ToArray()
            : exact;

        if (exactYear.Length == 1 && IsPositiveInteger(exactYear[0].AniListId))
        {
            return exactYear[0];
        }

        if (allowYearMismatch && exact.Length == 1 && IsPositiveInteger(exact[0].AniListId))
        {
            return exact[0];
        }

        return null;
    }

    /// <summary>Normalizes a title for an exact, punctuation-insensitive comparison.</summary>
    public static string NormalizeTitle(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    private static IEnumerable<string> CandidateTitles(AnimeItem item)
    {
        yield return item.Title.Romaji;
        if (!string.IsNullOrWhiteSpace(item.Title.English))
        {
            yield return item.Title.English;
        }

        if (!string.IsNullOrWhiteSpace(item.Title.Native))
        {
            yield return item.Title.Native;
        }

        foreach (var alias in item.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
        {
            yield return alias;
        }
    }

    private static string? FindProviderId(IReadOnlyDictionary<string, string>? providerIds, IEnumerable<string> keys)
    {
        if (providerIds is null)
        {
            return null;
        }

        var normalizedKeys = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return providerIds.FirstOrDefault(pair => normalizedKeys.Contains(pair.Key)).Value;
    }

    internal static string? ResolveLocalMapping(SubtitleSearchRequest request, string mappingJson, Guid? episodeId = null, Guid? seriesId = null)
    {
        if (string.IsNullOrWhiteSpace(mappingJson))
        {
            return null;
        }

        Dictionary<string, JsonElement>? mappings;
        try
        {
            mappings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(mappingJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (mappings is null)
        {
            return null;
        }

        var keys = new List<string>();
        if (seriesId is { } knownSeriesId && knownSeriesId != Guid.Empty && request.ParentIndexNumber is int seasonNumber)
        {
            keys.Add($"id:{knownSeriesId:N}#season:{seasonNumber}");
        }
        if (episodeId is { } knownEpisodeId && knownEpisodeId != Guid.Empty)
        {
            keys.Add($"id:{knownEpisodeId:N}");
        }
        if (seriesId is { } seriesKey && seriesKey != Guid.Empty)
        {
            keys.Add($"id:{seriesKey:N}");
        }
        if (!string.IsNullOrWhiteSpace(request.MediaPath))
        {
            var path = request.MediaPath.Replace('\\', '/').Trim().TrimEnd('/').ToLowerInvariant();
            while (!string.IsNullOrEmpty(path))
            {
                if (request.ParentIndexNumber is int season)
                {
                    keys.Add($"path:{path}#season:{season}");
                }
                keys.Add($"path:{path}");
                var separator = path.LastIndexOf('/');
                if (separator <= 0) break;
                path = path[..separator];
            }
        }
        if (request.ProviderIds is not null)
        {
            keys.AddRange(request.ProviderIds.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{pair.Key.ToLowerInvariant()}:{pair.Value}"));
        }

        foreach (var key in keys)
        {
            var match = mappings.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(match.Key))
            {
                continue;
            }

            return match.Value.ValueKind switch
            {
                JsonValueKind.String => match.Value.GetString(),
                JsonValueKind.Number when match.Value.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
                _ => null,
            };
        }

        return null;
    }

    private static bool IsPositiveInteger(string? value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    private static PluginConfiguration GetConfiguration()
        => Plugin.Instance?.Configuration ?? throw new InvalidOperationException("AzureIllusion plugin is not initialized.");

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
