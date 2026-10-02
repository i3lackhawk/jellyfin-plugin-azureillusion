namespace Jellyfin.Plugin.AzureIllusion.Api;

/// <summary>Limits request start rate across the whole plugin process.</summary>
public sealed class ApiRequestGate
{
    // WebSubs allows 60 requests/minute per key. Leave headroom for manual searches.
    internal static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromMilliseconds(1200);

    private readonly SemaphoreSlim _scheduleLock = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minimumInterval;
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    /// <summary>Initializes the production request gate.</summary>
    public ApiRequestGate()
        : this(TimeProvider.System, DefaultMinimumInterval)
    {
    }

    internal ApiRequestGate(TimeProvider timeProvider, TimeSpan minimumInterval)
    {
        _timeProvider = timeProvider;
        _minimumInterval = minimumInterval;
    }

    /// <summary>Waits until the next request may start.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            await _scheduleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _timeProvider.GetUtcNow();
                delay = _nextRequestAt - now;
                if (delay <= TimeSpan.Zero)
                {
                    _nextRequestAt = now + _minimumInterval;
                    return;
                }
            }
            finally
            {
                _scheduleLock.Release();
            }

            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Applies a server retry delay to every plugin request, not only the failing call.</summary>
    public async Task ApplyRetryAfterAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await _scheduleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var retryAt = _timeProvider.GetUtcNow() + delay;
            if (retryAt > _nextRequestAt)
            {
                _nextRequestAt = retryAt;
            }
        }
        finally
        {
            _scheduleLock.Release();
        }
    }
}
