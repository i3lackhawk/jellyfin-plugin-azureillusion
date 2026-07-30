namespace Jellyfin.Plugin.AzureIllusion.ScheduledTasks;

internal static class SubtitleTaskExecutionGate
{
    internal static SemaphoreSlim Instance { get; } = new(1, 1);
}
