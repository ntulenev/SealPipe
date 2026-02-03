using System.ComponentModel.DataAnnotations;

namespace SealPipe.Tcp;

/// <summary>
/// Defines TCP keep-alive configuration.
/// </summary>
public sealed class KeepAliveOptions
{
    /// <summary>
    /// Gets a value indicating whether keep-alives are enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets how long a connection must be idle before keep-alives are sent.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan TcpKeepAliveTime { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the interval between keep-alive probes.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan TcpKeepAliveInterval { get; init; } = TimeSpan.FromSeconds(10);
}
