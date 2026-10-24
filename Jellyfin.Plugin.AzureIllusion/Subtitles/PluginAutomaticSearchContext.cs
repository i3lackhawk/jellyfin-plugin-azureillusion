namespace Jellyfin.Plugin.AzureIllusion.Subtitles;

/// <summary>
/// Rozróżnia własny, kontrolowany skan wtyczki od ogólnego automatycznego
/// zadania napisów Jellyfin. AsyncLocal zachowuje zakres przez wywołania async.
/// </summary>
internal static class PluginAutomaticSearchContext
{
    private static readonly AsyncLocal<Scope?> Current = new();

    public static bool IsActive => Current.Value is not null;

    public static Scope Enter(bool simulation = false)
    {
        var scope = new Scope(Current.Value, simulation);
        Current.Value = scope;
        return scope;
    }

    public static void SetOutcome(string code) { if (Current.Value is { } scope) scope.Outcome = code; }
    internal static void RecordAlreadyPresent() { if (Current.Value is { } scope) scope.AlreadyPresent++; }
    internal static bool IsSimulation => Current.Value?.Simulation == true;
    internal static void RecordLocal(IEnumerable<State.LocalReconciliationItem> items)
    {
        if (Current.Value is { } scope)
            foreach (var item in items)
                if (!scope.LocalItems.Any(x => x.Download.MediaKey == item.Download.MediaKey && x.Download.ReleaseId == item.Download.ReleaseId && x.Result == item.Result))
                    scope.LocalItems.Add(item);
    }

    internal sealed class Scope(Scope? previous, bool simulation) : IDisposable
    {
        internal bool Simulation { get; } = simulation;
        internal int AlreadyPresent { get; set; }
        internal List<State.LocalReconciliationItem> LocalItems { get; } = [];
        public string Outcome { get; set; } = "noResults";
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = previous;
        }
    }
}
