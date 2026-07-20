using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AzureIllusion.Configuration;

/// <summary>
/// Sposób wyboru grup tłumaczeniowych.
/// </summary>
public enum GroupSelectionMode
{
    /// <summary>Najlepiej ocenione wydania niezależnie od grupy.</summary>
    BestRated,

    /// <summary>Tylko grupy wskazane przez administratora.</summary>
    SelectedGroups,
}

/// <summary>
/// Konfiguracja wtyczki AzureIllusion.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Inicjalizuje domyślne ustawienia.</summary>
    public PluginConfiguration()
    {
        ApiBaseUrl = "https://subs.azureillusion.ovh";
        ApiKey = string.Empty;
        EnableAutomaticSearch = true;
        GroupSelection = GroupSelectionMode.BestRated;
        SelectedGroupSlugs = [];
        MaximumGroups = 1;
        MinimumRating = 0;
        VerifiedOnly = false;
        Languages = ["pl", "pl2"];
        EnableExactTitleFallback = true;
        RequestTimeoutSeconds = 30;
        ExternalIdMappingsJson = "{}";
    }

    /// <summary>Adres strony AzureIllusion.</summary>
    public string ApiBaseUrl { get; set; }

    /// <summary>Klucz publicznego API.</summary>
    public string ApiKey { get; set; }

    /// <summary>Czy pozwalać zadaniom Jellyfin na automatyczne wyszukiwanie.</summary>
    public bool EnableAutomaticSearch { get; set; }

    /// <summary>Sposób wyboru grup.</summary>
    public GroupSelectionMode GroupSelection { get; set; }

    /// <summary>Slugi wybranych grup.</summary>
    public string[] SelectedGroupSlugs { get; set; }

    /// <summary>Maksymalna liczba różnych grup. Zero oznacza wszystkie.</summary>
    public int MaximumGroups { get; set; }

    /// <summary>Minimalna ocena wydania w skali 0-10.</summary>
    public double MinimumRating { get; set; }

    /// <summary>Czy pokazywać wyłącznie zweryfikowane wydania.</summary>
    public bool VerifiedOnly { get; set; }

    /// <summary>Akceptowane warianty języka, np. pl i pl2.</summary>
    public string[] Languages { get; set; }

    /// <summary>Czy przy braku AniList ID próbować jednoznacznego tytułu i roku.</summary>
    public bool EnableExactTitleFallback { get; set; }

    /// <summary>Limit czasu pojedynczego zapytania.</summary>
    public int RequestTimeoutSeconds { get; set; }

    /// <summary>
    /// Lokalne mapowanie identyfikatorów Jellyfin na AniList. Przykład:
    /// {"anidb:123":456,"kitsu:abc":456}. Dane nie są wysyłane do strony.
    /// </summary>
    public string ExternalIdMappingsJson { get; set; }
}
