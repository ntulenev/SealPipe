namespace SealPipe.Tcp;

/// <summary>
/// Defines how buffered frames behave when the channel is full.
/// </summary>
public enum ChannelOverflowStrategy
{
    /// <summary>
    /// Blocks the producer until capacity is available.
    /// </summary>
    Block = 0,

    /// <summary>
    /// Drops frames when the channel is full.
    /// </summary>
    Drop = 1
}
