namespace SealPipe.Tcp;

/// <summary>
/// Defines methods for reading delimited frames from a TCP stream.
/// </summary>
public interface ITcpDelimitedStreamClient
{
    /// <summary>
    /// Reads delimited frames as strings using the configured encoding.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the read operation.</param>
    /// <returns>A stream of decoded messages.</returns>
    IAsyncEnumerable<string> ReadMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads delimited frames as raw bytes.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the read operation.</param>
    /// <returns>A stream of frame buffers.</returns>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFramesAsync(CancellationToken cancellationToken = default);
}
