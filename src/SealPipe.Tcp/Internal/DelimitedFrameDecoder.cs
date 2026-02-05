using System.Buffers;
using System.Collections.Generic;
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

                List<IMemoryOwner<byte>> frames;
                ParseResult parseResult;
                try
                {
                    parseResult = ParseFrames(buffer, out frames);
                }
                catch
                {
                    throw;
                }

                if (parseResult.RemainingLength > _maxFrameBytes)
                {
                    foreach (var frame in frames)
                    {
                        frame.Dispose();
                    }

                    throw new TcpProtocolException(
                        $"Frame length {parseResult.RemainingLength} exceeds max {_maxFrameBytes} bytes.");
                }

                reader.AdvanceTo(parseResult.Consumed, parseResult.Examined);

                foreach (var frame in frames)
                {
                    yield return frame;
                }

                if (result.IsCompleted)
                {
                    if (parseResult.RemainingLength > 0)
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

    private ParseResult ParseFrames(
        ReadOnlySequence<byte> buffer,
        out List<IMemoryOwner<byte>> frames)
    {
        frames = new List<IMemoryOwner<byte>>();
        var consumed = buffer.Start;
        var examined = buffer.End;

        try
        {
            var bufferSlice = buffer;
            while (true)
            {
                var sequenceReader = new SequenceReader<byte>(bufferSlice);
                if (!sequenceReader.TryReadTo(out ReadOnlySequence<byte> frame, _delimiter.Span, true))
                {
                    break;
                }

                if (frame.Length > _maxFrameBytes)
                {
                    throw new TcpProtocolException(
                        $"Frame length {frame.Length} exceeds max {_maxFrameBytes} bytes.");
                }

                consumed = sequenceReader.Position;
                examined = consumed;
                bufferSlice = buffer.Slice(consumed);

                var pooled = frame.Length == 0
                    ? PooledFrame.CreateEmpty()
                    : PooledFrame.CopyFrom(frame);
                frames.Add(pooled);
            }

            var remainingLength = bufferSlice.Length;
            return new ParseResult(consumed, examined, remainingLength);
        }
        catch
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }

            throw;
        }
    }

    private readonly ReadOnlyMemory<byte> _delimiter;
    private readonly int _maxFrameBytes;
    private readonly record struct ParseResult(
        SequencePosition Consumed,
        SequencePosition Examined,
        long RemainingLength);
}
