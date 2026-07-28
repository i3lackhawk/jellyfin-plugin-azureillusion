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
        var version = typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        serviceCollection.AddHttpClient<Api.AzureIllusionApiClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Jellyfin-Plugin-PolskieNapisyAnime/{version}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        serviceCollection.AddSingleton<Matching.AniListResolver>();
        serviceCollection.AddSingleton<State.DownloadStateStore>();
        serviceCollection.AddSingleton<ISubtitleProvider, Subtitles.AzureIllusionSubtitleProvider>();
    }
}
