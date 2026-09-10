using Jellyfin.Plugin.AzureIllusion.Matching;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class AnimeMatchCacheTests
{
    [Fact]
    public async Task GetOrCreate_CoalescesConcurrentRequests()
    {
        var cache = new AnimeMatchCache();
        var calls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<AnimeMatch?> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return new AnimeMatch("123", "test", true);
        }

        var first = cache.GetOrCreateAsync("series|2026", Factory, CancellationToken.None);
        var second = cache.GetOrCreateAsync("series|2026", Factory, CancellationToken.None);
        release.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Equal("123", result?.AniListId));
    }

    [Fact]
    public async Task GetOrCreate_CachesNegativeResult()
    {
        var cache = new AnimeMatchCache();
        var calls = 0;

        Task<AnimeMatch?> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult<AnimeMatch?>(null);
        }

        Assert.Null(await cache.GetOrCreateAsync("missing|2026", Factory, CancellationToken.None));
        Assert.Null(await cache.GetOrCreateAsync("missing|2026", Factory, CancellationToken.None));
        Assert.Equal(1, calls);
    }
}
