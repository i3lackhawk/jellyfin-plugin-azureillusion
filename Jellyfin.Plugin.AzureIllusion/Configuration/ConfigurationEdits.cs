using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Plugin.AzureIllusion.Matching;

namespace Jellyfin.Plugin.AzureIllusion.Configuration;

public sealed record ConfigurationEdit(string Revision, Dictionary<string, JsonElement> Changes);

internal static class ConfigurationEdits
{
    public static readonly SemaphoreSlim Gate = new(1, 1);
    public static string Revision(PluginConfiguration configuration)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(configuration)));

    public static PluginConfiguration Apply(PluginConfiguration current, IReadOnlyDictionary<string, JsonElement> changes)
    {
        var values = JsonSerializer.SerializeToElement(current).EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone());
        var allowed = typeof(PluginConfiguration).GetProperties().Where(x => x.CanWrite).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in changes)
        {
            if (!allowed.Contains(pair.Key)) throw new InvalidDataException("Nieznane ustawienie: " + pair.Key);
            values[pair.Key] = pair.Value;
        }
        var updated = JsonSerializer.Deserialize<PluginConfiguration>(JsonSerializer.Serialize(values)) ?? throw new InvalidDataException("Nieprawidłowe ustawienia.");
        if (updated.RequestTimeoutSeconds is < 5 or > 120 || updated.MinimumFreeSpaceMegabytes < 0
            || updated.MaximumGroups < 0 || !double.IsFinite(updated.MinimumRating) || updated.MinimumRating is < 0 or > 10
            || updated.Languages is null || updated.Languages.Length == 0
            || updated.SelectedGroupSlugs is null || updated.IgnoredGroupSlugs is null || updated.PriorityGroupSlugs is null
            || updated.SelectedLibraryPaths is null || !Enum.IsDefined(updated.GroupSelection) || !Enum.IsDefined(updated.ReleaseSelection))
            throw new InvalidDataException("Nieprawidłowe limity lub listy w konfiguracji.");
        using var mappings = JsonDocument.Parse(updated.ExternalIdMappingsJson);
        if (mappings.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Mapowanie musi być obiektem.");
        foreach (var entry in mappings.RootElement.EnumerateObject()) ValidateMapping(entry.Value);
        return updated;
    }

    private static void ValidateMapping(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (!value.TryGetProperty("episodeStart", out var start) || !start.TryGetInt32(out var from)
                    || AniListResolver.ParseMappingValue(value, from) is null
                    || !value.TryGetProperty("episodeEnd", out var end) || !end.TryGetInt32(out var to)
                    || AniListResolver.ParseMappingValue(value, to) is null) throw new InvalidDataException("Nieprawidłowy zakres mapowania.");
            }
            else if (AniListResolver.ParseMappingValue(value, null) is null) throw new InvalidDataException("Nieprawidłowy identyfikator mapowania.");
            return;
        }
        var ranges = value.EnumerateArray().ToArray();
        if (ranges.Length == 0 || ranges.Any(x => x.ValueKind != JsonValueKind.Object)) throw new InvalidDataException("Lista mapowań musi zawierać zakresy.");
        foreach (var range in ranges) ValidateMapping(range);
        var sorted = ranges.OrderBy(x => x.GetProperty("episodeStart").GetInt32()).ToArray();
        for (var i = 1; i < sorted.Length; i++)
            if (sorted[i].GetProperty("episodeStart").GetInt32() <= sorted[i - 1].GetProperty("episodeEnd").GetInt32())
                throw new InvalidDataException("Zakresy odcinków nakładają się.");
    }
}
