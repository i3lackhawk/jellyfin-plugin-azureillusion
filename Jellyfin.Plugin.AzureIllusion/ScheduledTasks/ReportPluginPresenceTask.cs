using Jellyfin.Plugin.AzureIllusion.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AzureIllusion.ScheduledTasks;

/// <summary>Reports the installed plugin version through an authenticated health request.</summary>
public sealed class ReportPluginPresenceTask : IScheduledTask
{
    private readonly AzureIllusionApiClient _client;
    private readonly ILogger<ReportPluginPresenceTask> _logger;

    public ReportPluginPresenceTask(AzureIllusionApiClient client, ILogger<ReportPluginPresenceTask> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string Name => "Polskie Napisy Anime — zgłoś wersję dodatku";

    public string Key => "PolskieNapisyAnimeReportPluginPresence";

    public string Description => "Co 30 minut potwierdza WebSubs wersję dodatku i działanie klucza API.";

    public string Category => "Polskie Napisy Anime";

    public bool IsHidden => false;

    public bool IsEnabled => true;

    public bool IsLogged => true;

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            await _client.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
            progress.Report(100);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Nie udało się zgłosić wersji dodatku do WebSubs.");
            throw;
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromMinutes(30).Ticks }];
}
