using System.ComponentModel.DataAnnotations;

namespace SealPipe.Tcp;

/// <summary>
/// Defines configuration for connecting to a TCP stream and parsing delimited frames.
/// </summary>
public sealed class TcpDelimitedClientOptions
{
    /// <summary>
    /// Gets the remote host name or IP address.
    /// </summary>
    [Required]
    public required string Host { get; init; }

    /// <summary>
    /// Gets the remote port.
    /// </summary>
    [Range(1, 65535)]
    public required int Port { get; init; }

    /// <summary>
    /// Gets the delimiter string used to frame messages (for example, "\n" or "\r\n").
    /// </summary>
    [Required]
    public string Delimiter { get; init; } = "\n";

    /// <summary>
    /// Gets the text encoding name used to decode frames.
    /// </summary>
    [Required]
    public string Encoding { get; init; } = "utf-8";

    /// <summary>
    /// Gets the maximum amount of time allowed for a TCP connection attempt.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "1.00:00:00")]
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the maximum amount of time allowed without receiving any data.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "1.00:00:00")]
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the reconnect policy options.
    /// </summary>
    public ReconnectOptions Reconnect { get; init; } = new();

    /// <summary>
    /// Gets the maximum allowed size of a single frame in bytes.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxFrameBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Gets the strategy used when buffered frame channels are full.
    /// </summary>
    public ChannelOverflowStrategy ChannelOverflowStrategy { get; init; } = ChannelOverflowStrategy.Drop;

    /// <summary>
    /// Gets TCP keep-alive configuration.
    /// </summary>
    public KeepAliveOptions KeepAlive { get; init; } = new();

    /// <summary>
    /// Validates options.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when required values are missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when values are out of range.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Host is required.", nameof(Host));
        }

        if (Port <= 0 || Port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 1 and 65535.");
        }

        if (string.IsNullOrEmpty(Delimiter))
        {
            throw new ArgumentException("Delimiter is required.", nameof(Delimiter));
        }

        if (string.IsNullOrWhiteSpace(Encoding))
        {
            throw new ArgumentException("Encoding is required.", nameof(Encoding));
        }

        if (MaxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFrameBytes), "MaxFrameBytes must be positive.");
        }

        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConnectTimeout),
                "ConnectTimeout must be greater than zero.");
        }

        if (ReadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReadTimeout),
                "ReadTimeout must be greater than zero.");
        }

        if (KeepAlive.Enabled)
        {
            if (KeepAlive.TcpKeepAliveTime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(KeepAlive.TcpKeepAliveTime),
                    "KeepAlive TcpKeepAliveTime must be greater than zero.");
            }

            if (KeepAlive.TcpKeepAliveInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(KeepAlive.TcpKeepAliveInterval),
                    "KeepAlive TcpKeepAliveInterval must be greater than zero.");
            }
        }

    }
}
