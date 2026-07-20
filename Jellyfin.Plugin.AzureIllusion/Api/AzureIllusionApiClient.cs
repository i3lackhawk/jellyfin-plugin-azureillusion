using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.Api;

/// <summary>
/// Typowany klient publicznego API AzureIllusion.
/// </summary>
public sealed class AzureIllusionApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<AzureIllusionApiClient> _logger;

    /// <summary>Inicjalizuje klienta.</summary>
    public AzureIllusionApiClient(HttpClient httpClient, ILogger<AzureIllusionApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>Sprawdza połączenie i klucz API.</summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        _ = await GetAsync<JsonElement>("/api/public/v1/health", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pobiera grupy dostępne przez API.</summary>
    public async Task<IReadOnlyList<GroupItem>> GetGroupsAsync(CancellationToken cancellationToken)
    {
        var result = await GetAsync<PagedResult<GroupItem>>("/api/public/v1/groups?limit=100", cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>Wyszukuje anime po tytule i opcjonalnym roku.</summary>
    public async Task<IReadOnlyList<AnimeItem>> SearchAnimeAsync(string title, int? year, CancellationToken cancellationToken)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("q", title),
            new("limit", "20"),
        };
        if (year.HasValue)
        {
            query.Add(new("year", year.Value.ToString(CultureInfo.InvariantCulture)));
        }

        var result = await GetAsync<PagedResult<AnimeItem>>(BuildPath("/api/public/v1/anime", query), cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>Wyszukuje wydania napisów dla AniList ID.</summary>
    public async Task<SubtitleSearchResult> SearchSubtitlesAsync(SubtitleQuery query, CancellationToken cancellationToken)
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("anilistId", query.AniListId),
            new("verified", query.VerifiedOnly ? "true" : "false"),
            new("minRating", query.MinimumRating.ToString(CultureInfo.InvariantCulture)),
            new("limit", query.Limit.ToString(CultureInfo.InvariantCulture)),
            new("format", "ASS,SRT"),
        };
        if (query.Season.HasValue)
        {
            values.Add(new("season", query.Season.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (query.Episode.HasValue)
        {
            values.Add(new("episode", query.Episode.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (query.Languages.Count > 0)
        {
            values.Add(new("language", string.Join(',', query.Languages)));
        }

        if (query.Groups.Count > 0)
        {
            values.Add(new("group", string.Join(',', query.Groups)));
        }

        return await GetAsync<SubtitleSearchResult>(BuildPath("/api/public/v1/subtitles/search", values), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pobiera plik napisów.</summary>
    public async Task<Stream> DownloadSubtitleAsync(string releaseId, CancellationToken cancellationToken)
    {
        var request = CreateRequest(HttpMethod.Get, BuildPath(
            "/api/public/v1/subtitles/file",
            [new("type", "subtitle"), new("id", releaseId)]));
        var response = await SendWithRetryAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiErrorAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new ResponseOwnedStream(source, response);
    }

    private static string BuildPath(string path, IEnumerable<KeyValuePair<string, string?>> values)
    {
        var query = string.Join(
            '&',
            values
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
        return string.IsNullOrEmpty(query) ? path : $"{path}?{query}";
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await SendWithRetryAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiErrorAsync(response, cancellationToken).ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (envelope?.Ok != true || envelope.Data is null)
        {
            throw new AzureIllusionApiException(envelope?.Error?.Message ?? "API AzureIllusion zwróciło niepełną odpowiedź.", envelope?.Error?.Code);
        }

        return envelope.Data;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var configuration = GetConfiguration();
        var baseUri = ValidateBaseUri(configuration.ApiBaseUrl);
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey.Trim());
        return request;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        var configuration = GetConfiguration();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.RequestTimeoutSeconds, 5, 120)));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var copy = await CloneRequestAsync(request, timeout.Token).ConfigureAwait(false);
            try
            {
                var response = await _httpClient.SendAsync(copy, completionOption, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                {
                    return response;
                }

                if (attempt == 2)
                {
                    return response;
                }

                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(350 * Math.Pow(2, attempt));
                response.Dispose();
                await Task.Delay(delay, timeout.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (attempt < 2)
            {
                _logger.LogWarning(exception, "Przejściowy błąd połączenia z AzureIllusion (próba {Attempt}/3).", attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(350 * Math.Pow(2, attempt)), timeout.Token).ConfigureAwait(false);
            }
        }

        throw new AzureIllusionApiException("Nie udało się połączyć z API AzureIllusion.");
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage source, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (source.Content is not null)
        {
            var bytes = await source.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in source.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static async Task ThrowApiErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var error = await JsonSerializer.DeserializeAsync<ApiEnvelope<JsonElement>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            throw new AzureIllusionApiException(
                error?.Error?.Message ?? $"API AzureIllusion zwróciło HTTP {(int)response.StatusCode}.",
                error?.Error?.Code);
        }
        catch (JsonException exception)
        {
            throw new AzureIllusionApiException($"API AzureIllusion zwróciło HTTP {(int)response.StatusCode}.", innerException: exception);
        }
    }

    private static PluginConfiguration GetConfiguration()
    {
        var configuration = Plugin.Instance?.Configuration ?? throw new AzureIllusionApiException("Wtyczka AzureIllusion nie została zainicjalizowana.");
        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            throw new AzureIllusionApiException("W konfiguracji wtyczki brakuje klucza API.");
        }

        return configuration;
    }

    private static Uri ValidateBaseUri(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri))
        {
            throw new AzureIllusionApiException("Adres API AzureIllusion jest nieprawidłowy.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !uri.IsLoopback)
        {
            throw new AzureIllusionApiException("Poza adresem lokalnym API musi używać HTTPS.");
        }

        return uri;
    }

    private sealed class ResponseOwnedStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public ResponseOwnedStream(Stream inner, HttpResponseMessage response)
        {
            _inner = inner;
            _response = response;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            _response.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
