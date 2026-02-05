namespace SealPipe.Tcp;

/// <summary>
/// Provides counters and timestamps for a TCP delimited stream client.
/// </summary>
public sealed class TcpDelimitedClientDiagnostics
{
    /// <summary>
    /// Gets the total number of reconnect attempts.
    /// </summary>
    public long ReconnectAttempts => Interlocked.Read(ref _reconnectAttempts);

    /// <summary>
    /// Gets the total number of bytes received from the socket.
    /// </summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>
    /// Gets the total number of frames successfully decoded.
    /// </summary>
    public long FramesReceived => Interlocked.Read(ref _framesReceived);

    /// <summary>
    /// Gets the total number of frames dropped due to channel overflow.
    /// </summary>
    public long FramesDropped => Interlocked.Read(ref _framesDropped);

    /// <summary>
    /// Gets the timestamp of the last successfully decoded frame, if any.
    /// </summary>
    public DateTimeOffset? LastMessageTimestamp
    {
        get
        {
            var raw = Interlocked.Read(ref _lastMessageUnixMs);
            return raw == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(raw);
        }
    }

    internal void AddBytes(long count)
    {
        _ = Interlocked.Add(ref _bytesReceived, count);
    }

    internal void AddFrame()
    {
        _ = Interlocked.Increment(ref _framesReceived);
        _ = Interlocked.Exchange(
            ref _lastMessageUnixMs,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    internal void AddDroppedFrame()
    {
        _ = Interlocked.Increment(ref _framesDropped);
    }

    internal void AddReconnectAttempt()
    {
        _ = Interlocked.Increment(ref _reconnectAttempts);
    }

    private long _reconnectAttempts;
    private long _bytesReceived;
    private long _framesReceived;
    private long _framesDropped;
    private long _lastMessageUnixMs;
}
