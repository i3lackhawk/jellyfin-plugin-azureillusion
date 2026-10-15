namespace Jellyfin.Plugin.AzureIllusion.Subtitles;

/// <summary>
/// Rozróżnia własny, kontrolowany skan wtyczki od ogólnego automatycznego
/// zadania napisów Jellyfin. AsyncLocal zachowuje zakres przez wywołania async.
/// </summary>
internal static class PluginAutomaticSearchContext
{
    private static readonly AsyncLocal<Scope?> Current = new();

    public static bool IsActive => Current.Value is not null;

    public static Scope Enter()
    {
        var scope = new Scope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    public static void SetOutcome(string code) { if (Current.Value is { } scope) scope.Outcome = code; }

    internal sealed class Scope(Scope? previous) : IDisposable
    {
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
