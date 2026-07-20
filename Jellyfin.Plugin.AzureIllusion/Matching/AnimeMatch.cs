namespace Jellyfin.Plugin.AzureIllusion.Matching;

/// <summary>Result of resolving a Jellyfin item to AniList.</summary>
public sealed record AnimeMatch(string AniListId, string Source, bool IsConfident);
