using System.Diagnostics;
using Jellyfin.Plugin.AzureIllusion.Api;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class ApiRequestGateTests
{
    [Fact]
    public async Task WaitAsync_SpacesConcurrentRequestStarts()
    {
        var interval = TimeSpan.FromMilliseconds(25);
        var gate = new ApiRequestGate(TimeProvider.System, interval);
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(
            gate.WaitAsync(CancellationToken.None),
            gate.WaitAsync(CancellationToken.None),
            gate.WaitAsync(CancellationToken.None));

        Assert.True(stopwatch.Elapsed >= interval + interval - TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void RetryDelay_UsesRetryAfterHeader()
    {
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(12));

        Assert.Equal(TimeSpan.FromSeconds(12), AzureIllusionApiClient.RetryDelay(response, 0));
    }
}
