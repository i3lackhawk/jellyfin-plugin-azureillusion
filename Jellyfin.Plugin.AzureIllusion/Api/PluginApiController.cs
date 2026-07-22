using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AzureIllusion.Api;

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Plugins/PolskieNapisyAnime")]
public sealed class PluginApiController : ControllerBase
{
    private readonly AzureIllusionApiClient _client;
    public PluginApiController(AzureIllusionApiClient client) => _client = client;

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
