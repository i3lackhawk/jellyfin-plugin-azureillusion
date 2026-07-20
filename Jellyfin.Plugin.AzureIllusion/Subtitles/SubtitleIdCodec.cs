using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.AzureIllusion.Subtitles;

/// <summary>Encodes all information required by Jellyfin's later download callback.</summary>
public static class SubtitleIdCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Encodes a subtitle payload as base64url.</summary>
    public static string Encode(SubtitleIdPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Decodes a subtitle payload.</summary>
    public static SubtitleIdPayload Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        try
        {
            return JsonSerializer.Deserialize<SubtitleIdPayload>(Convert.FromBase64String(base64), JsonOptions)
                ?? throw new FormatException("Subtitle identifier is empty.");
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            throw new FormatException("Invalid AzureIllusion subtitle identifier.", exception);
        }
    }
}

/// <summary>Serialized subtitle download information.</summary>
public sealed record SubtitleIdPayload(
    string ReleaseId,
    string MediaKey,
    string Language,
    string Format,
    string? Checksum);
