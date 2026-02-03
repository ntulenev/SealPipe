namespace SealPipe.Tcp;

/// <summary>
/// Defines reconnect backoff strategies.
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    /// Uses a fixed delay between attempts.
    /// </summary>
    Fixed = 0,

    /// <summary>
    /// Uses an exponential backoff delay.
    /// </summary>
    Exponential = 1,

    /// <summary>
    /// Uses an exponential backoff delay with random jitter.
    /// </summary>
    ExponentialWithJitter = 2
}
