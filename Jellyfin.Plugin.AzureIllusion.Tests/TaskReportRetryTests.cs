using Jellyfin.Plugin.AzureIllusion.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class TaskReportRetryTests
{
    [Fact]
    public async Task BudgetedRun_CanRewriteCancelledReportAsPartial()
    {
        using var fixture = new Fixture();
        await fixture.Store.SaveAsync(fixture.Report, default);
        var cancelled = await fixture.Store.ReadLatestAsync("download-missing", default);
        await fixture.Store.SaveAsync(cancelled! with { Status = "partial", Message = "stage time budget" }, default);
        await fixture.Store.SaveAsync(cancelled! with { Status = "partial", Message = "stage time budget" }, default);
        var history = Assert.Single(await fixture.Store.ReadHistoryAsync(default));
        Assert.Equal("partial", history.Status);
        Assert.Equal(cancelled!.RunId, history.RunId);
        Assert.Equal(cancelled.Counters["downloaded"], history.Counters["downloaded"]);
        Assert.Equal("partial", (await fixture.Store.ReadLatestAsync("download-missing", default))!.Status);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ConcurrentSaveOfSameRun_IsIdempotent()
    {
        using var fixture = new Fixture();
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fixture.Store.SaveAsync(fixture.Report, default)));
        Assert.Single(await fixture.Store.ReadHistoryAsync(default));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DifferentRunsWithSameTimestamp_AreKeptSeparately()
    {
        using var fixture = new Fixture();
        await fixture.Store.SaveAsync(fixture.Report, default);
        await fixture.Store.SaveAsync(fixture.Report with { RunId = "second-run" }, default);
        Assert.Equal(2, (await fixture.Store.ReadHistoryAsync(default)).Count);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "pna-report-retry-" + Guid.NewGuid().ToString("N"));
        internal TaskReportStore Store { get; }
        internal PluginTaskReport Report { get; } = new("download-missing", "test", false, DateTimeOffset.UtcNow.AddMinutes(-90),
            DateTimeOffset.UtcNow, "cancelled", new Dictionary<string, int> { ["downloaded"] = 817 }, [], "interrupted", RunId: "same-run");
        internal Fixture() { Directory.CreateDirectory(Root); Store = new(NullLogger<TaskReportStore>.Instance, Root); }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
