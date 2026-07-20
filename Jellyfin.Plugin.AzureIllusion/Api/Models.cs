using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AzureIllusion.Api;

/// <summary>Otoczka poprawnej odpowiedzi publicznego API.</summary>
/// <typeparam name="T">Typ danych.</typeparam>
public sealed record ApiEnvelope<T>(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("error")] ApiError? Error);

/// <summary>Błąd publicznego API.</summary>
public sealed record ApiError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

/// <summary>Stronicowana lista.</summary>
/// <typeparam name="T">Typ elementu.</typeparam>
public sealed record PagedResult<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("totalPages")] int TotalPages);

/// <summary>Tytuły anime.</summary>
public sealed record AnimeTitles(
    [property: JsonPropertyName("romaji")] string Romaji,
    [property: JsonPropertyName("english")] string? English,
    [property: JsonPropertyName("native")] string? Native);

/// <summary>Anime zwrócone przez API.</summary>
public sealed record AnimeItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("anilistId")] string? AniListId,
    [property: JsonPropertyName("title")] AnimeTitles Title,
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("episodes")] int? Episodes,
    [property: JsonPropertyName("posterUrl")] string? PosterUrl);

/// <summary>Grupa tłumaczeniowa.</summary>
public sealed record GroupItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("logoUrl")] string? LogoUrl,
    [property: JsonPropertyName("status")] string Status);

/// <summary>Anime powiązane z wynikami napisów.</summary>
public sealed record SubtitleAnime(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("anilistId")] string AniListId,
    [property: JsonPropertyName("title")] AnimeTitles Title,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("episodes")] int? Episodes);

/// <summary>Sezon wydania.</summary>
public sealed record SubtitleSeason(
    [property: JsonPropertyName("number")] double Number,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("title")] string? Title);

/// <summary>Odcinek wydania.</summary>
public sealed record SubtitleEpisode(
    [property: JsonPropertyName("number")] double Number,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("index")] double Index,
    [property: JsonPropertyName("title")] string? Title);

/// <summary>Ocena jakości.</summary>
public sealed record SubtitleRating(
    [property: JsonPropertyName("average")] double Average,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("translation")] double Translation,
    [property: JsonPropertyName("synchronization")] double Synchronization,
    [property: JsonPropertyName("styling")] double Styling);

/// <summary>Wydanie napisów.</summary>
public sealed record SubtitleRelease(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("season")] SubtitleSeason? Season,
    [property: JsonPropertyName("episode")] SubtitleEpisode? Episode,
    [property: JsonPropertyName("group")] GroupItem? Group,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes,
    [property: JsonPropertyName("checksumSha256")] string? ChecksumSha256,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("isVerified")] bool IsVerified,
    [property: JsonPropertyName("isRecommended")] bool IsRecommended,
    [property: JsonPropertyName("rating")] SubtitleRating Rating,
    [property: JsonPropertyName("downloads")] int Downloads,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl);

/// <summary>Wynik wyszukania napisów.</summary>
public sealed record SubtitleSearchResult(
    [property: JsonPropertyName("anime")] SubtitleAnime Anime,
    [property: JsonPropertyName("releases")] IReadOnlyList<SubtitleRelease> Releases);

/// <summary>Parametry wyszukania napisów.</summary>
public sealed record SubtitleQuery(
    string AniListId,
    double? Season,
    double? Episode,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Groups,
    bool VerifiedOnly,
    double MinimumRating,
    int Limit);

/// <summary>Błąd klienta AzureIllusion.</summary>
public sealed class AzureIllusionApiException : Exception
{
    /// <summary>Inicjalizuje błąd.</summary>
    public AzureIllusionApiException(string message, string? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Kod błędu API.</summary>
    public string? Code { get; }
}
