namespace Jellyfin.Plugin.AzureIllusion.State;

internal sealed class TaskOperationMetrics : IDisposable
{
    private static readonly AsyncLocal<TaskOperationMetrics?> Slot = new();
    private readonly TaskOperationMetrics? _previous;
    private long _apiRequests, _apiMilliseconds, _stateWrites, _stateMilliseconds;
    private TaskOperationMetrics() { _previous = Slot.Value; Slot.Value = this; }
    public static TaskOperationMetrics Enter() => new();
    public static void Api(long elapsed) { var c = Slot.Value; if (c is null) return; Interlocked.Increment(ref c._apiRequests); Interlocked.Add(ref c._apiMilliseconds, elapsed); }
    public static void State(long elapsed) { var c = Slot.Value; if (c is null) return; Interlocked.Increment(ref c._stateWrites); Interlocked.Add(ref c._stateMilliseconds, elapsed); }
    public static IReadOnlyDictionary<string, int> Snapshot()
    {
        var c = Slot.Value;
        return new Dictionary<string, int>
        {
            ["apiRequests"] = (int)Math.Min(int.MaxValue, c?._apiRequests ?? 0),
            ["apiMilliseconds"] = (int)Math.Min(int.MaxValue, c?._apiMilliseconds ?? 0),
            ["stateWrites"] = (int)Math.Min(int.MaxValue, c?._stateWrites ?? 0),
            ["stateMilliseconds"] = (int)Math.Min(int.MaxValue, c?._stateMilliseconds ?? 0),
        };
    }
    public void Dispose() => Slot.Value = _previous;
}
