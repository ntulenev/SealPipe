using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using SealPipe.Tcp.Exceptions;

namespace SealPipe.Tcp.Internal;

/// <summary>
/// Decodes delimiter-separated frames from a <see cref="PipeReader"/>.
/// </summary>
internal sealed class DelimitedFrameDecoder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DelimitedFrameDecoder"/> class.
    /// </summary>
    /// <param name="delimiter">The delimiter sequence used to terminate frames.</param>
    /// <param name="maxFrameBytes">The maximum allowed size for a single frame.</param>
    public DelimitedFrameDecoder(
        ReadOnlyMemory<byte> delimiter,
        int maxFrameBytes)
    {
        if (delimiter.Length == 0)
        {
            throw new ArgumentException("Delimiter cannot be empty.", nameof(delimiter));
        }

        if (maxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameBytes), "MaxFrameBytes must be positive.");
        }

        _delimiter = delimiter;
        _maxFrameBytes = maxFrameBytes;
    }

    /// <summary>
    /// Reads frames from the provided <see cref="PipeReader"/> as pooled buffers.
    /// </summary>
    /// <param name="reader">The pipe reader to consume.</param>
    /// <param name="cancellationToken">The token used to cancel the read operation.</param>
    /// <returns>A stream of pooled frames; each frame must be disposed by the consumer.</returns>
    public async IAsyncEnumerable<IMemoryOwner<byte>> ReadFramesAsync(
        PipeReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;
                if (buffer.Length == 0 && result.IsCompleted)
                {
                    break;
                }

                var sequenceReader = new SequenceReader<byte>(buffer);
                if (sequenceReader.TryReadTo(
                        out ReadOnlySequence<byte> frame,
                        _delimiter.Span,
                        advancePastDelimiter: true))
                {
                    if (frame.Length > _maxFrameBytes)
                    {
                        throw new TcpProtocolException(
                            $"Frame length {frame.Length} exceeds max {_maxFrameBytes} bytes.");
                    }

                    var pooled = frame.Length == 0
                        ? PooledFrame.CreateEmpty()
                        : PooledFrame.CopyFrom(frame);

                    var consumed = sequenceReader.Position;
                    reader.AdvanceTo(consumed, consumed);
                    yield return pooled;
                    continue;
                }

                if (buffer.Length > _maxFrameBytes)
                {
                    throw new TcpProtocolException(
                        $"Frame length {buffer.Length} exceeds max {_maxFrameBytes} bytes.");
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    if (buffer.Length > 0)
                    {
                        throw new TcpProtocolException("Connection closed with incomplete frame data.");
                    }

                    break;
                }
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private readonly ReadOnlyMemory<byte> _delimiter;
    private readonly int _maxFrameBytes;
}
