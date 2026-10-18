namespace Jellyfin.Plugin.AzureIllusion.ScheduledTasks;

internal static class StageTimeBudget
{
    public static async Task<bool> RunAsync(Func<CancellationToken, Task> action, TimeSpan duration, CancellationToken parent)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(parent);
        budget.CancelAfter(duration);
        try { await action(budget.Token).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !parent.IsCancellationRequested) { return false; }
    }
}
