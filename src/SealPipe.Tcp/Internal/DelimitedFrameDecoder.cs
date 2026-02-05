using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SealPipe.Tcp;
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
    /// <param name="overflowStrategy">The strategy used when the frame channel is full.</param>
    /// <param name="channelCapacity">The capacity for the frame buffering channel.</param>
    /// <param name="diagnostics">The diagnostics sink for dropped frame counts.</param>
    public DelimitedFrameDecoder(
        ReadOnlyMemory<byte> delimiter,
        int maxFrameBytes,
        ChannelOverflowStrategy overflowStrategy,
        int channelCapacity,
        TcpDelimitedClientDiagnostics diagnostics)
    {
        if (delimiter.Length == 0)
        {
            throw new ArgumentException("Delimiter cannot be empty.", nameof(delimiter));
        }

        if (maxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameBytes), "MaxFrameBytes must be positive.");
        }

        if (channelCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCapacity), "ChannelCapacity must be positive.");
        }

        _delimiter = delimiter;
        _maxFrameBytes = maxFrameBytes;
        _overflowStrategy = overflowStrategy;
        _channelCapacity = channelCapacity;
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Reads frames from the provided <see cref="PipeReader"/> as pooled buffers.
    /// </summary>
    /// <param name="reader">The pipe reader to consume.</param>
    /// <param name="cancellationToken">The token used to cancel the read operation.</param>
    /// <returns>A stream of pooled frames; each frame must be disposed by the consumer.</returns>
    public IAsyncEnumerable<IMemoryOwner<byte>> ReadFramesAsync(
        PipeReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var channel = Channel.CreateBounded<IMemoryOwner<byte>>(new BoundedChannelOptions(_channelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = MapFullMode(_overflowStrategy)
        });

        using var decodeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var decodeTask = RunDecodeAsync(reader, channel.Writer, decodeCts.Token);

        return ReadAllWithCleanupAsync(channel.Reader, decodeTask, decodeCts, cancellationToken);
    }

    private static async IAsyncEnumerable<IMemoryOwner<byte>> ReadAllWithCleanupAsync(
        ChannelReader<IMemoryOwner<byte>> reader,
        Task decodeTask,
        CancellationTokenSource decodeCts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            if (!decodeTask.IsCompleted)
            {
                await decodeCts.CancelAsync().ConfigureAwait(false);
            }

            try
            {
                await decodeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            while (reader.TryRead(out var frame))
            {
                frame.Dispose();
            }
        }
    }

    private async Task RunDecodeAsync(
        PipeReader reader,
        ChannelWriter<IMemoryOwner<byte>> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;
                if (buffer.Length == 0 && result.IsCompleted)
                {
                    writer.TryComplete();
                    break;
                }

                ParseResult parseResult;
                try
                {
                    parseResult = await ParseFramesAsync(buffer, writer, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    throw;
                }

                if (parseResult.RemainingLength > _maxFrameBytes)
                {
                    throw new TcpProtocolException(
                        $"Frame length {parseResult.RemainingLength} exceeds max {_maxFrameBytes} bytes.");
                }

                reader.AdvanceTo(parseResult.Consumed, parseResult.Examined);

                if (result.IsCompleted)
                {
                    if (parseResult.RemainingLength > 0)
                    {
                        throw new TcpProtocolException("Connection closed with incomplete frame data.");
                    }

                    writer.TryComplete();
                    break;
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            writer.TryComplete(ex);
        }
        catch (TcpProtocolException ex)
        {
            writer.TryComplete(ex);
        }
        catch (ChannelClosedException ex)
        {
            writer.TryComplete(ex);
        }
        catch (ObjectDisposedException ex)
        {
            writer.TryComplete(ex);
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<ParseResult> ParseFramesAsync(
        ReadOnlySequence<byte> buffer,
        ChannelWriter<IMemoryOwner<byte>> writer,
        CancellationToken cancellationToken)
    {
        var consumed = buffer.Start;
        var examined = buffer.End;
        var remainingLength = buffer.Length;

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

            PooledFrame? pooled = null;
            try
            {
                pooled = frame.Length == 0
                    ? PooledFrame.CreateEmpty()
                    : PooledFrame.CopyFrom(frame);

                if (_overflowStrategy == ChannelOverflowStrategy.Block)
                {
                    await writer.WriteAsync(pooled, cancellationToken).ConfigureAwait(false);
                    pooled = null;
                }
                else
                {
                    if (writer.TryWrite(pooled))
                    {
                        pooled = null;
                    }
                    else
                    {
                        _diagnostics.AddDroppedFrame();
                    }
                }
            }
            finally
            {
                pooled?.Dispose();
            }
        }

        remainingLength = bufferSlice.Length;
        return new ParseResult(consumed, examined, remainingLength);
    }

    private static BoundedChannelFullMode MapFullMode(ChannelOverflowStrategy strategy)
    {
        return strategy == ChannelOverflowStrategy.Block
                   ? BoundedChannelFullMode.Wait
                   : BoundedChannelFullMode.DropWrite;
    }

    private readonly ReadOnlyMemory<byte> _delimiter;
    private readonly int _maxFrameBytes;
    private readonly ChannelOverflowStrategy _overflowStrategy;
    private readonly TcpDelimitedClientDiagnostics _diagnostics;
    private readonly int _channelCapacity;

    private readonly record struct ParseResult(
        SequencePosition Consumed,
        SequencePosition Examined,
        long RemainingLength);
}
