namespace Jellyfin.Plugin.AzureIllusion.Api;

/// <summary>Limits request start rate across the whole plugin process.</summary>
public sealed class ApiRequestGate
{
    internal static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromMilliseconds(250);

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
        TimeSpan delay;
        await _scheduleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var scheduledAt = _nextRequestAt > now ? _nextRequestAt : now;
            delay = scheduledAt - now;
            _nextRequestAt = scheduledAt + _minimumInterval;
        }
        finally
        {
            _scheduleLock.Release();
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
