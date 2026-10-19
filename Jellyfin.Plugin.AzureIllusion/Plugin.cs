using System.Globalization;
using Jellyfin.Plugin.AzureIllusion.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AzureIllusion;

/// <summary>
/// Główna klasa wtyczki.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Identyfikator wtyczki.</summary>
    public static readonly Guid PluginId = Guid.Parse("83ff339c-b48b-4a86-a30f-259ed08c7f45");

    /// <summary>Inicjalizuje wtyczkę.</summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        StateDirectory = Path.Combine(applicationPaths.PluginConfigurationsPath, "AzureIllusion");
        // Fail closed before tasks or API endpoints can change the registry/configuration.
        UpgradeBackupDirectory = State.UpgradeBackup.Ensure(StateDirectory, ConfigurationFilePath, GetType().Assembly.GetName().Version!.ToString(4));
        Instance = this;
    }

    /// <summary>Aktualna instancja.</summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>Katalog prywatnego stanu wtyczki.</summary>
    public string StateDirectory { get; }

    /// <summary>Verified server-local snapshot created before exposing this version's services.</summary>
    public string UpgradeBackupDirectory { get; }

    /// <inheritdoc />
    public override string Name => "Polskie Napisy Anime";

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace),
            },
        ];
    }
}
