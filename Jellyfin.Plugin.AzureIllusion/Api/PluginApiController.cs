using Jellyfin.Plugin.AzureIllusion.State;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AzureIllusion.Api;

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Plugins/PolskieNapisyAnime")]
public sealed class PluginApiController : ControllerBase
{
    public sealed record MappingPreviewRequest(string Path);

    [HttpPost("mapping-preview")]
    public async Task<ActionResult> MappingPreview([FromBody] MappingPreviewRequest input,
        [FromServices] MediaBrowser.Controller.Library.ILibraryManager library,
        [FromServices] Matching.AniListResolver resolver, CancellationToken token)
    {
        var video = library.FindByPath(input.Path, false) as MediaBrowser.Controller.Entities.Video;
        if (video is null) return NotFound(new { message = "Nie znaleziono pliku w bibliotece Jellyfin." });
        var episode = video as MediaBrowser.Controller.Entities.TV.Episode;
        var request = new MediaBrowser.Controller.Subtitles.SubtitleSearchRequest
        {
            ContentType = episode is null ? MediaBrowser.Controller.Providers.VideoContentType.Movie : MediaBrowser.Controller.Providers.VideoContentType.Episode,
            MediaPath = video.Path, Name = video.Name, SeriesName = episode?.SeriesName,
            IndexNumber = video.IndexNumber, ParentIndexNumber = video.ParentIndexNumber,
            ProductionYear = video.ProductionYear, ProviderIds = video.ProviderIds,
        };
        var match = await resolver.ResolveAsync(request, token).ConfigureAwait(false);
        return Ok(new { series = episode?.SeriesName ?? video.Name, season = video.ParentIndexNumber, episode = video.IndexNumber,
            episodeEnd = episode?.IndexNumberEnd, match, multiEpisode = episode?.IndexNumberEnd > video.IndexNumber,
            message = episode?.IndexNumberEnd > video.IndexNumber ? "Plik obejmuje kilka odcinków; wymagany jest ręczny wybór napisów. Automat pozostawi go bez zmian."
                : match is { IsConfident: true } ? "Jednoznaczne dopasowanie. Podgląd nie pobiera plików." : "Brak pewnego dopasowania; automat pozostawi plik bez zmian." });
    }
    [HttpGet("configuration")]
    public async Task<ActionResult> ConfigurationSnapshot(CancellationToken token)
    {
        await Configuration.ConfigurationEdits.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var configuration = Plugin.Instance!.Configuration;
            return Ok(new { configuration = System.Text.Json.JsonSerializer.SerializeToElement(configuration), revision = Configuration.ConfigurationEdits.Revision(configuration) });
        }
        finally { Configuration.ConfigurationEdits.Gate.Release(); }
    }

    [HttpPost("configuration")]
    public async Task<ActionResult> EditConfiguration([FromBody] Configuration.ConfigurationEdit edit, CancellationToken token)
    {
        await Configuration.ConfigurationEdits.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var plugin = Plugin.Instance!;
            if (edit.Revision != Configuration.ConfigurationEdits.Revision(plugin.Configuration))
                return Conflict(new { message = "Ustawienia zmieniono w innej karcie. Twoje zmiany pozostają w formularzu. Otwórz ponownie panel przed zapisem." });
            var next = Configuration.ConfigurationEdits.Apply(plugin.Configuration, edit.Changes);
            plugin.UpdateConfiguration(next);
            return Ok(new { revision = Configuration.ConfigurationEdits.Revision(next) });
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        { return BadRequest(new { message = ex.Message }); }
        finally { Configuration.ConfigurationEdits.Gate.Release(); }
    }

    [HttpGet("progress")]
    public ActionResult Progress() => Ok(ScheduledTasks.MaintenanceProgress.Current);

    [HttpGet("reports")]
    public async Task<ActionResult> History(CancellationToken token)
        => Ok((await _reports.ReadHistoryAsync(token).ConfigureAwait(false)).Select(report => report with { Items = [] }));
    private readonly AzureIllusionApiClient _client;
    private readonly TaskReportStore _reports;
    private readonly DiagnosticEventStore _diagnostics;
    public PluginApiController(AzureIllusionApiClient client, TaskReportStore reports, DiagnosticEventStore diagnostics)
    {
        _client = client;
        _reports = reports;
        _diagnostics = diagnostics;
    }

    [HttpGet("status")]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        try { await _client.TestConnectionAsync(cancellationToken).ConfigureAwait(false); return Ok(new { ok = true, message = "Połączenie działa." }); }
        catch (AzureIllusionApiException exception) { return BadRequest(new { ok = false, message = FriendlyMessage(exception) }); }
        catch (TaskCanceledException) { return StatusCode(504, new { ok = false, message = "API nie odpowiedziało w ustawionym czasie." }); }
        catch (HttpRequestException) { return StatusCode(502, new { ok = false, message = "Nie można połączyć się z adresem API. Sprawdź adres, DNS i certyfikat TLS." }); }
    }

    [HttpGet("groups")]
    public async Task<ActionResult> Groups(CancellationToken cancellationToken)
    {
        try { return Ok(new { ok = true, items = await _client.GetGroupsAsync(cancellationToken).ConfigureAwait(false) }); }
        catch (Exception exception) when (exception is AzureIllusionApiException or HttpRequestException or TaskCanceledException) { return BadRequest(new { ok = false, message = exception.Message }); }
    }

    [HttpGet("languages")]
    public async Task<ActionResult> Languages(CancellationToken cancellationToken)
    {
        try { return Ok(new { ok = true, items = await _client.GetLanguagesAsync(cancellationToken).ConfigureAwait(false) }); }
        catch (Exception exception) when (exception is AzureIllusionApiException or HttpRequestException or TaskCanceledException) { return BadRequest(new { ok = false, message = exception.Message }); }
    }

    [HttpGet("reports/{taskKey}")]
    public async Task<ActionResult> LatestReport(string taskKey, CancellationToken cancellationToken)
    {
        var report = await _reports.ReadLatestAsync(taskKey, cancellationToken).ConfigureAwait(false);
        return report is null ? NotFound(new { ok = false, message = "Brak raportu dla tego zadania." }) : Ok(report);
    }

    [HttpGet("diagnostics")]
    public async Task<ActionResult> Diagnostics(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _diagnostics.ReadAsync(cancellationToken).ConfigureAwait(false);
            return Ok(snapshot);
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return StatusCode(503, new { message = "Nie udało się odczytać diagnostyki dodatku. Sprawdź log Jellyfin." });
        }
    }

    [HttpGet("logo")]
    [AllowAnonymous]
    public ActionResult Logo()
    {
        var stream = typeof(PluginApiController).Assembly.GetManifestResourceStream("Jellyfin.Plugin.AzureIllusion.Assets.logo.png");
        return stream is null ? NotFound() : File(stream, "image/png");
    }

    private static string FriendlyMessage(AzureIllusionApiException exception) => exception.Code switch
    {
        "API_KEY_REQUIRED" or "API_KEY_INVALID" or "API_KEY_FORBIDDEN" => "Klucz API jest nieprawidłowy albo nieaktywny.",
        "API_MAINTENANCE" => "Publiczne API jest chwilowo wyłączone z powodu prac technicznych.",
        _ => exception.Message,
    };
}
