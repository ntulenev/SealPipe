namespace SealPipe.Tcp.Internal;

/// <summary>
/// Computes reconnect delays based on configured options.
/// </summary>
internal sealed class ReconnectPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReconnectPolicy"/> class.
    /// </summary>
    /// <param name="options">The reconnect configuration.</param>
    public ReconnectPolicy(ReconnectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Resets the reconnect attempt counter.
    /// </summary>
    public void Reset()
    {
        _ = Interlocked.Exchange(ref _attempts, 0);
    }

    /// <summary>
    /// Computes the next delay and increments the attempt count.
    /// </summary>
    /// <param name="delay">The computed delay.</param>
    /// <returns><c>true</c> if another attempt is allowed; otherwise <c>false</c>.</returns>
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

#pragma warning disable CA5394

        // Suppress CA5394: cryptographically secure randomness is unnecessary here.
        // The value is used solely to add non-deterministic jitter to a delay and has
        // no impact on security guarantees.

        var jitterMs = Random.Shared.NextDouble() * delay.TotalMilliseconds;

#pragma warning restore CA5394

        return TimeSpan.FromMilliseconds(jitterMs);
    }

    private readonly ReconnectOptions _options;
    private int _attempts;
}
