namespace Jellyfin.Plugin.AzureIllusion.ScheduledTasks;

/// <summary>Read-only live progress. No credentials or media paths.</summary>
public static class MaintenanceProgress
{
    public sealed record Snapshot(string Stage, double Percent, DateTimeOffset ChangedAtUtc, int Queued = 0);
    private static Snapshot _current = new("idle", 0, DateTimeOffset.UtcNow);
    private static readonly object Sync = new();
    public static Snapshot Current => Volatile.Read(ref _current);
    public static void Report(string stage, double percent)
    {
        lock (Sync) Volatile.Write(ref _current, new(stage, Math.Clamp(percent, 0, 100), DateTimeOffset.UtcNow, _current.Queued));
    }
    public static async Task WaitAsync(SemaphoreSlim gate, CancellationToken token)
    {
        lock (Sync) Volatile.Write(ref _current, _current with { Queued = _current.Queued + 1 });
        try { await gate.WaitAsync(token).ConfigureAwait(false); }
        finally { lock (Sync) Volatile.Write(ref _current, _current with { Queued = _current.Queued - 1 }); }
    }
}
