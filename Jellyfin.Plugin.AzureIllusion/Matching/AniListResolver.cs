using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.AzureIllusion.Api;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.Matching;

/// <summary>Resolves Jellyfin metadata to the AniList identifier used by AzureIllusion.</summary>
public sealed partial class AniListResolver
{
    private static readonly string[] AniListKeys = ["anilist", "ani-list", "anilistid"];
    private readonly AzureIllusionApiClient _apiClient;
    private readonly ILogger<AniListResolver> _logger;

    /// <summary>Initializes the resolver.</summary>
    public AniListResolver(AzureIllusionApiClient apiClient, ILogger<AniListResolver> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>Resolves an AniList identifier without sending foreign identifiers to the website.</summary>
    public async Task<AnimeMatch?> ResolveAsync(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        var directId = FindProviderId(request.ProviderIds, AniListKeys);
        if (IsPositiveInteger(directId))
        {
            return new AnimeMatch(directId!, "Jellyfin AniList ID", true);
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

        var candidates = await _apiClient.SearchAnimeAsync(title, request.ProductionYear, cancellationToken).ConfigureAwait(false);
        var normalized = NormalizeTitle(title);
        var exact = candidates
            .Where(item => item.AniListId is not null)
            .Where(item => request.ProductionYear is null || item.Year == request.ProductionYear)
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

    /* Legacy local mappings intentionally removed.
    private static string? ResolveLocalMapping(IReadOnlyDictionary<string, string>? providerIds, string mappingJson)
    {
        if (providerIds is null || string.IsNullOrWhiteSpace(mappingJson))
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

        foreach (var provider in providerIds)
        {
            var key = $"{provider.Key.ToLowerInvariant()}:{provider.Value}";
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
    }*/

    private static bool IsPositiveInteger(string? value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    private static PluginConfiguration GetConfiguration()
        => Plugin.Instance?.Configuration ?? throw new InvalidOperationException("AzureIllusion plugin is not initialized.");

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
