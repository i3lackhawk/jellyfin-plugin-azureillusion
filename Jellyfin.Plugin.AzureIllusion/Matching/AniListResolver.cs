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
        var manual = ResolveLocalAnimeMapping(
            request,
            GetConfiguration().ExternalIdMappingsJson,
            episode?.Id,
            seriesId == Guid.Empty ? null : seriesId, season?.Id);
        if (manual is not null)
        {
            return manual;
        }

        var ownId = SelectMetadataIdentifier(
            request.ProviderIds, episode?.ProviderIds, season?.ProviderIds, episode?.Series?.ProviderIds, seasonNumber, season?.Name);
        if (ownId is { IsConfident: false }) return ownId;

        var identifiers = new[]
        {
            ("malId", new[] { "myanimelist", "mal", "myanimelistid" }),
            ("anidbId", new[] { "anidb", "anidbid" }),
            ("kitsuId", new[] { "kitsu", "kitsuid" }),
        };
        var externalMatches = new List<AnimeMatch>();
        if (ownId is not null) externalMatches.Add(ownId);
        foreach (var (provider, keys) in identifiers)
        {
            // AniDB identifiers on Episode items can identify episodes, not anime.
            var identifier = request.ContentType == MediaBrowser.Controller.Providers.VideoContentType.Episode
                ? FindProviderId(season?.ProviderIds, keys) ?? (seasonNumber == 1 && (string.IsNullOrWhiteSpace(season?.Name) || IsGenericSeasonTitle(season.Name)) ? FindProviderId(episode?.Series?.ProviderIds, keys) : null)
                : FindProviderId(request.ProviderIds, keys);
            if (!IsPositiveInteger(identifier)) continue;
            var resolved = await _cache.GetOrCreateAsync($"identifier:{provider}:{identifier}", async token =>
            {
                var anime = await _apiClient.ResolveIdentifierAsync(provider, identifier!, token).ConfigureAwait(false);
                return IsPositiveInteger(anime?.AniListId) ? new AnimeMatch(anime!.AniListId!, $"Jellyfin {provider}", true) : null;
            }, cancellationToken).ConfigureAwait(false);
            if (resolved is not null) externalMatches.Add(resolved);
        }
        if (externalMatches.Select(item => item.AniListId).Distinct().Count() > 1)
            return new AnimeMatch("", "conflicting identifiers", false);
        if (externalMatches.Count > 0) return externalMatches[0];

        if (!GetConfiguration().EnableExactTitleFallback)
        {
            return null;
        }

        var titleCandidates = BuildTitleFallbacks(
            request.ContentType == MediaBrowser.Controller.Providers.VideoContentType.Episode ? request.SeriesName : request.Name,
            season?.Name,
            seasonNumber).Concat(BuildTitleFallbacks(episode?.Series?.OriginalTitle, season?.OriginalTitle, seasonNumber))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (titleCandidates.Length == 0)
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

    internal static AnimeMatch? SelectMetadataIdentifier(
        IReadOnlyDictionary<string, string>? requestIds, IReadOnlyDictionary<string, string>? episodeIds,
        IReadOnlyDictionary<string, string>? seasonIds, IReadOnlyDictionary<string, string>? seriesIds, int? seasonNumber, string? seasonName = null)
    {
        var root = FindProviderId(seriesIds, AniListKeys);
        var direct = FindProviderId(requestIds, AniListKeys);
        var mayInheritParent = seasonNumber == 1 && (string.IsNullOrWhiteSpace(seasonName) || IsGenericSeasonTitle(seasonName));
        if (!mayInheritParent && direct == root) direct = null;
        var own = new[] { FindProviderId(seasonIds, AniListKeys), FindProviderId(episodeIds, AniListKeys), direct }
            .Where(IsPositiveInteger).Distinct(StringComparer.Ordinal).ToArray();
        if (own.Length > 1) return new AnimeMatch("", "conflicting element AniList IDs", false);
        if (own.Length == 1) return new AnimeMatch(own[0]!, "Jellyfin element AniList ID", true);
        if (mayInheritParent && IsPositiveInteger(root)) return new AnimeMatch(root!, "Jellyfin series AniList ID (season one)", true);
        return null;
    }

    internal static IReadOnlyList<string> BuildTitleFallbacks(string? seriesTitle, string? seasonTitle, int? seasonNumber)
    {
        var titles = new List<string>();
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
        else if (seasonNumber is null or 1)
        {
            Add(seriesTitle);
        }
        return titles;
    }

    private static bool IsGenericSeasonTitle(string value)
        => Regex.IsMatch(value.Trim(), @"^(?:(?:season|sezon)\s*\d+|specials?|extras|dodatki|odcinki specjalne)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        if (candidates.Count(item => CandidateTitles(item).Any(candidate => NormalizeTitle(candidate) == NormalizeTitle(title))) > 1)
            return new AnimeMatch("", "ambiguous exact title", false);
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
        return providerIds.FirstOrDefault(pair => normalizedKeys.Contains(pair.Key)).Value?.Trim();
    }

    internal static string? ResolveLocalMapping(SubtitleSearchRequest request, string mappingJson, Guid? episodeId = null, Guid? seriesId = null)
        => ResolveLocalAnimeMapping(request, mappingJson, episodeId, seriesId)?.AniListId;

    internal static AnimeMatch? ResolveLocalAnimeMapping(SubtitleSearchRequest request, string mappingJson, Guid? episodeId = null, Guid? seriesId = null, Guid? seasonId = null)
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
        if (seasonId is { } knownSeasonId && knownSeasonId != Guid.Empty) keys.Add($"id:{knownSeasonId:N}");
        if (seriesId is { } seriesKey && seriesKey != Guid.Empty && request.ParentIndexNumber == 1)
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
                // Exact files are safe; an unqualified ancestor could represent season one.
                if (path == request.MediaPath.Replace('\\', '/').Trim().TrimEnd('/').ToLowerInvariant()
                    || request.ParentIndexNumber == 1 || request.ContentType == MediaBrowser.Controller.Providers.VideoContentType.Movie)
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

            return ParseMappingValue(match.Value, request.IndexNumber)
                ?? throw new InvalidDataException("Ręczne mapowanie jest nieprawidłowe, nakłada się lub nie obejmuje tego odcinka. Automatyczny wybór został zablokowany.");
        }

        return null;
    }

    internal static AnimeMatch? ParseMappingValue(JsonElement value, int? episode)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var matches = value.EnumerateArray().Select(item => ParseMappingValue(item, episode)).Where(item => item is not null).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (!episode.HasValue || !value.TryGetProperty("anilistId", out var id)
                || !value.TryGetProperty("episodeStart", out var start) || !start.TryGetInt32(out var from)
                || !value.TryGetProperty("episodeEnd", out var end) || !end.TryGetInt32(out var to)
                || !value.TryGetProperty("episodeOffset", out var offset) || !offset.TryGetInt32(out var shift)
                || from < 1 || to < from || episode < from || episode > to) return null;
            var mapped = (long)episode.Value + shift;
            var identifier = id.ValueKind == JsonValueKind.String ? id.GetString() : id.ToString();
            int? targetSeason = null;
            if (value.TryGetProperty("targetSeason", out var seasonValue))
            {
                if (!seasonValue.TryGetInt32(out var target) || target < 0) return null;
                targetSeason = target;
            }
            return IsPositiveInteger(identifier) && mapped is > 0 and <= int.MaxValue
                ? new AnimeMatch(identifier!, "administrator episode range", true, (int)mapped, targetSeason) : null;
        }
        var plain = value.ValueKind is JsonValueKind.String ? value.GetString() : value.ToString();
        return IsPositiveInteger(plain) ? new AnimeMatch(plain!, "administrator mapping", true) : null;
    }

    private static bool IsPositiveInteger(string? value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    private static PluginConfiguration GetConfiguration()
        => Plugin.Instance?.Configuration ?? throw new InvalidOperationException("AzureIllusion plugin is not initialized.");

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
