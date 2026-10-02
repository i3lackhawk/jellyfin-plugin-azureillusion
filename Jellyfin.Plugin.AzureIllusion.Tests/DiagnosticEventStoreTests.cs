using Jellyfin.Plugin.AzureIllusion.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class DiagnosticEventStoreTests
{
    [Fact]
    public async Task RecordsCountersAndBoundedRecentEventsAcrossInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-diagnostics-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "diagnostics.json");
        try
        {
            var store = new DiagnosticEventStore(NullLogger<DiagnosticEventStore>.Instance, path);
            for (var index = 0; index < DiagnosticEventStore.MaximumEvents + 5; index++)
            {
                await store.TryRecordAsync("warning", "API_RATE_LIMIT", "Rate limited", null, CancellationToken.None);
            }

            var reopened = new DiagnosticEventStore(NullLogger<DiagnosticEventStore>.Instance, path);
            var snapshot = await reopened.ReadAsync(CancellationToken.None);

            Assert.Equal(DiagnosticEventStore.MaximumEvents + 5, snapshot.CountsLastSevenDays["API_RATE_LIMIT"]);
            Assert.Equal(DiagnosticEventStore.MaximumEvents, snapshot.RecentEvents.Count);
            Assert.All(snapshot.RecentEvents, item => Assert.Equal("API_RATE_LIMIT", item.Code));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
