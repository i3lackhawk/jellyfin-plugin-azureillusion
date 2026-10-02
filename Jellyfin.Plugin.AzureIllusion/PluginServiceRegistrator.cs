using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AzureIllusion;

/// <summary>
/// Rejestruje usługi w kontenerze Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        var version = typeof(Plugin).Assembly.GetName().Version?.ToString(4) ?? "unknown";
        serviceCollection.AddSingleton<Api.ApiRequestGate>();
        serviceCollection.AddHttpClient<Api.AzureIllusionApiClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Jellyfin-Plugin-PolskieNapisyAnime/{version}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.Add("X-AzureIllusion-Client", "JELLYFIN");
            client.DefaultRequestHeaders.Add("X-AzureIllusion-Plugin-Version", version);
        });
        serviceCollection.AddSingleton<Matching.AnimeMatchCache>();
        serviceCollection.AddSingleton<Matching.AniListResolver>();
        serviceCollection.AddSingleton<State.DownloadStateStore>();
        serviceCollection.AddSingleton<State.TaskReportStore>();
        serviceCollection.AddSingleton<State.DiagnosticEventStore>();
        serviceCollection.AddSingleton<ISubtitleProvider, Subtitles.AzureIllusionSubtitleProvider>();
    }
}
