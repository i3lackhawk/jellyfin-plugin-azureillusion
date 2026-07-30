using Jellyfin.Plugin.AzureIllusion.State;
using Jellyfin.Plugin.AzureIllusion.Storage;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class SafetyAndReportingTests
{
    [Fact]
    public void DiskSpaceGuard_ReservesConfiguredSpaceAndTwoPayloadCopies()
    {
        const long megabyte = 1024L * 1024L;

        Assert.Equal(712 * megabyte, DiskSpaceGuard.CalculateRequiredBytes(100 * megabyte, 512));
        Assert.Equal(528 * megabyte, DiskSpaceGuard.CalculateRequiredBytes(null, 512));
    }

    [Fact]
    public void ReportKey_RemovesTraversalAndUnsafeCharacters()
    {
        Assert.Equal("download-missing", TaskReportStore.SafeTaskKey("../Download-Missing??"));
    }
}
