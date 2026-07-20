using Jellyfin.Plugin.AzureIllusion.Api;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using Jellyfin.Plugin.AzureIllusion.Matching;
using Jellyfin.Plugin.AzureIllusion.State;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.Subtitles;

/// <summary>Native Jellyfin subtitle provider backed by the AzureIllusion public API.</summary>
public sealed class AzureIllusionSubtitleProvider : ISubtitleProvider
{
    private readonly AzureIllusionApiClient _apiClient;
    private readonly AniListResolver _resolver;
    private readonly DownloadStateStore _stateStore;
    private readonly ILogger<AzureIllusionSubtitleProvider> _logger;

    /// <summary>Initializes the provider.</summary>
    public AzureIllusionSubtitleProvider(
        AzureIllusionApiClient apiClient,
        AniListResolver resolver,
        DownloadStateStore stateStore,
        ILogger<AzureIllusionSubtitleProvider> logger)
    {
        _apiClient = apiClient;
        _resolver = resolver;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "AzureIllusion";

    /// <inheritdoc />
    public IEnumerable<VideoContentType> SupportedMediaTypes => [VideoContentType.Episode, VideoContentType.Movie];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        var configuration = GetConfiguration();
        if (request.IsAutomated && !configuration.EnableAutomaticSearch)
        {
            return [];
        }

        if (!IsPolishRequest(request))
        {
            return [];
        }

        try
        {
            var match = await _resolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
            if (match is null || !match.IsConfident)
            {
                _logger.LogInformation("AzureIllusion skipped {Title}: no unambiguous AniList match.", request.SeriesName ?? request.Name);
                return [];
            }

            var groups = configuration.GroupSelection == GroupSelectionMode.SelectedGroups
                ? configuration.SelectedGroupSlugs.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
                : [];
            var mediaKey = BuildMediaKey(request);
            var query = new SubtitleQuery(
                match.AniListId,
                request.ContentType == VideoContentType.Episode ? request.ParentIndexNumber : null,
                request.ContentType == VideoContentType.Episode ? request.IndexNumber : null,
                configuration.Languages,
                groups,
                configuration.VerifiedOnly,
                Math.Clamp(configuration.MinimumRating, 0, 10),
                100);
            var result = await _apiClient.SearchSubtitlesAsync(query, cancellationToken).ConfigureAwait(false);
            var releases = ReleaseSelector.LimitGroups(result.Releases, Math.Max(configuration.MaximumGroups, 0));
            var output = new List<RemoteSubtitleInfo>(releases.Count);

            foreach (var release in releases)
            {
                if (request.IsAutomated
                    && await _stateStore.ContainsAsync(mediaKey, release.Id, release.ChecksumSha256, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var language = NormalizeLanguage(release.Language);
                var payload = new SubtitleIdPayload(release.Id, mediaKey, language, release.Format, release.ChecksumSha256);
                output.Add(new RemoteSubtitleInfo
                {
                    Id = SubtitleIdCodec.Encode(payload),
                    ProviderName = Name,
                    ThreeLetterISOLanguageName = "pol",
                    Name = BuildDisplayName(release),
                    Format = release.Format.ToLowerInvariant(),
                    Author = release.Group?.Name ?? "AzureIllusion",
                    Comment = BuildComment(release),
                    DateCreated = release.PublishedAt.UtcDateTime,
                    CommunityRating = (float)release.Rating.Average,
                    DownloadCount = release.Downloads,
                    IsHashMatch = false,
                    AiTranslated = false,
                    MachineTranslated = false,
                    Forced = false,
                    HearingImpaired = false,
                });
            }

            return output;
        }
        catch (Exception exception) when (exception is AzureIllusionApiException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "AzureIllusion subtitle search failed for {Title}.", request.SeriesName ?? request.Name);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        var payload = SubtitleIdCodec.Decode(id);
        var stream = await _apiClient.DownloadSubtitleAsync(payload.ReleaseId, cancellationToken).ConfigureAwait(false);
        try
        {
            await _stateStore.MarkDownloadedAsync(
                payload.MediaKey,
                payload.ReleaseId,
                payload.Checksum,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Subtitle downloaded, but AzureIllusion deduplication state could not be saved.");
        }

        return new SubtitleResponse
        {
            Language = "pol",
            Format = payload.Format.ToLowerInvariant(),
            IsForced = false,
            IsHearingImpaired = false,
            Stream = stream,
        };
    }

    /// <summary>Builds a stable key for automatic-download deduplication.</summary>
    public static string BuildMediaKey(SubtitleSearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.MediaPath))
        {
            return request.MediaPath.Replace('\\', '/').Trim().ToLowerInvariant();
        }

        return string.Join(
            '|',
            request.ContentType,
            request.SeriesName ?? request.Name,
            request.ParentIndexNumber,
            request.IndexNumber).ToLowerInvariant();
    }

    private static bool IsPolishRequest(SubtitleSearchRequest request)
    {
        var values = new[] { request.Language, request.TwoLetterISOLanguageName }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        return !values.Any() || values.Any(value => value.Equals("pl", StringComparison.OrdinalIgnoreCase)
            || value.Equals("pol", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLanguage(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "pl2" => "pl2",
            _ => "pl",
        };

    private static string BuildDisplayName(SubtitleRelease release)
    {
        var group = release.Group?.Name ?? "AzureIllusion";
        return $"[{release.Language.ToUpperInvariant()}] {group} - {release.Filename}";
    }

    private static string BuildComment(SubtitleRelease release)
    {
        var flags = new List<string>();
        if (release.IsVerified)
        {
            flags.Add("zweryfikowane");
        }

        if (release.IsRecommended)
        {
            flags.Add("polecane");
        }

        flags.Add($"ocena {release.Rating.Average:0.0}/10");
        flags.Add($"wersja {release.Version}");
        return string.Join(" | ", flags);
    }

    private static PluginConfiguration GetConfiguration()
        => Plugin.Instance?.Configuration ?? throw new AzureIllusionApiException("AzureIllusion plugin is not initialized.");
}
