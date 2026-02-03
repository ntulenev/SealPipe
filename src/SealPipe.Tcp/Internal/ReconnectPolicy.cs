namespace SealPipe.Tcp.Internal;

internal sealed class ReconnectPolicy
{
    public ReconnectPolicy(ReconnectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public void Reset()
    {
        _ = Interlocked.Exchange(ref _attempts, 0);
    }

    public bool TryGetNextDelay(out TimeSpan delay)
    {
        if (!_options.Enabled)
        {
            delay = TimeSpan.Zero;
            return false;
        }

        var attempt = Interlocked.Increment(ref _attempts);
        if (_options.MaxAttempts > 0 && attempt > _options.MaxAttempts)
        {
            delay = TimeSpan.Zero;
            return false;
        }

        delay = ComputeDelay(attempt);
        return true;
    }

    private TimeSpan ComputeDelay(int attempt)
    {
        var maxDelay = _options.MaxDelay;
        var initial = _options.InitialDelay;
        if (initial <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var delay = _options.Backoff switch
        {
            BackoffStrategy.Fixed => initial,
            BackoffStrategy.Exponential => Scale(initial, attempt),
            BackoffStrategy.ExponentialWithJitter => ApplyJitter(Scale(initial, attempt)),
            _ => initial
        };

        return delay > maxDelay ? maxDelay : delay;
    }

    private static TimeSpan Scale(TimeSpan initial, int attempt)
    {
        if (attempt <= 1)
        {
            return initial;
        }

        var factor = Math.Pow(2, attempt - 1);
        var totalMs = initial.TotalMilliseconds * factor;
        return TimeSpan.FromMilliseconds(Math.Min(totalMs, int.MaxValue));
    }

    private static TimeSpan ApplyJitter(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return delay;
        }

#pragma warning disable CA5394 // Do not use insecure randomness
        var jitterMs = Random.Shared.NextDouble() * delay.TotalMilliseconds;
#pragma warning restore CA5394 // Do not use insecure randomness
        return TimeSpan.FromMilliseconds(jitterMs);
    }

    private readonly ReconnectOptions _options;
    private int _attempts;
}
