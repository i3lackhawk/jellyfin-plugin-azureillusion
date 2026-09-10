using System.Collections.Concurrent;

namespace Jellyfin.Plugin.AzureIllusion.Matching;

/// <summary>Coalesces and caches title-based AniList resolutions.</summary>
public sealed class AnimeMatchCache
{
    private static readonly TimeSpan PositiveLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan NegativeLifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the production cache.</summary>
    public AnimeMatchCache()
        : this(TimeProvider.System)
    {
    }

    internal AnimeMatchCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>Gets an existing resolution or creates one once for concurrent callers.</summary>
    public async Task<AnimeMatch?> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<AnimeMatch?>> factory,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var now = _timeProvider.GetUtcNow();
            if (_entries.TryGetValue(key, out var cached))
            {
                if (cached.ExpiresAt > now)
                {
                    return await cached.Value.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                _entries.TryRemove(new KeyValuePair<string, CacheEntry>(key, cached));
                continue;
            }

            var created = new CacheEntry(
                new Lazy<Task<AnimeMatch?>>(
                    () => factory(CancellationToken.None),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                DateTimeOffset.MaxValue);
            if (!_entries.TryAdd(key, created))
            {
                continue;
            }

            try
            {
                var result = await created.Value.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
                created.ExpiresAt = _timeProvider.GetUtcNow() + (result is null ? NegativeLifetime : PositiveLifetime);
                return result;
            }
            catch
            {
                _entries.TryRemove(new KeyValuePair<string, CacheEntry>(key, created));
                throw;
            }
        }
    }

    private sealed class CacheEntry(Lazy<Task<AnimeMatch?>> value, DateTimeOffset expiresAt)
    {
        public Lazy<Task<AnimeMatch?>> Value { get; } = value;

        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
    }
}
