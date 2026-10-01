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
        if (request.ContentType == MediaBrowser.Controller.Providers.VideoContentType.Episode
            && !string.IsNullOrWhiteSpace(request.MediaPath))
        {
            episode = _libraryManager.FindByPath(request.MediaPath, false) as Episode;
        }

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

        // A Jellyfin series often has the AniList ID while its episodes do not.
        // It is safe to inherit that ID for season one only: later seasons have
        // distinct AniList entries and require a season-specific mapping.
        if (episode is not null && request.ParentIndexNumber is null or 1)
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

        var title = request.ContentType == MediaBrowser.Controller.Providers.VideoContentType.Episode
            ? request.SeriesName
            : request.Name;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var cacheKey = $"{NormalizeTitle(title)}|{request.ProductionYear?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            token => ResolveTitleAsync(title, request.ProductionYear, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AnimeMatch?> ResolveTitleAsync(string title, int? productionYear, CancellationToken cancellationToken)
    {
        var candidates = await _apiClient.SearchAnimeAsync(title, productionYear, cancellationToken).ConfigureAwait(false);
        var normalized = NormalizeTitle(title);
        var exact = candidates
            .Where(item => item.AniListId is not null)
            .Where(item => productionYear is null || item.Year == productionYear)
            .Where(item => CandidateTitles(item).Any(candidate => NormalizeTitle(candidate) == normalized))
            .ToArray();

        if (exact.Length == 1 && IsPositiveInteger(exact[0].AniListId))
        {
            return new AnimeMatch(exact[0].AniListId!, "exact title and year", true);
        }

        _logger.LogInformation(
            "AzureIllusion did not choose an ambiguous title match for {Title}. Exact candidates: {Count}.",
            title,
            exact.Length);
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
