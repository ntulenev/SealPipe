using System.ComponentModel.DataAnnotations;

namespace SealPipe.Tcp;

/// <summary>
/// Defines reconnect behavior for the TCP client.
/// </summary>
public sealed class ReconnectOptions
{
    /// <summary>
    /// Gets a value indicating whether reconnects are enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the initial delay before reconnecting.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "1.00:00:00")]
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets the maximum delay between reconnect attempts.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "1.00:00:00")]
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the backoff strategy used when reconnecting.
    /// </summary>
    public BackoffStrategy Backoff { get; init; } = BackoffStrategy.ExponentialWithJitter;

    /// <summary>
    /// Gets the maximum number of reconnect attempts. Zero means infinite.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int MaxAttempts { get; init; } = 0;
}
