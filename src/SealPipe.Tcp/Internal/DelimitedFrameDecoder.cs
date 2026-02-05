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
    public DelimitedFrameDecoder(
        ReadOnlyMemory<byte> delimiter,
        int maxFrameBytes,
        ChannelOverflowStrategy overflowStrategy)
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
        _overflowStrategy = overflowStrategy;
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

        var channel = Channel.CreateBounded<IMemoryOwner<byte>>(new BoundedChannelOptions(DefaultChannelCapacity)
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
#pragma warning disable CA1031 // Do not catch general exception types
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

                var pendingFrames = _overflowStrategy == ChannelOverflowStrategy.Block
                    ? new List<IMemoryOwner<byte>>()
                    : null;

                ParseResult parseResult;
                try
                {
                    parseResult = ParseFrames(buffer, writer, pendingFrames);
                }
                catch
                {
                    DisposePendingFrames(pendingFrames);
                    throw;
                }

                if (pendingFrames is { Count: > 0 })
                {
                    await WriteFramesAsync(writer, pendingFrames, cancellationToken).ConfigureAwait(false);
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
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }

    private ParseResult ParseFrames(
        ReadOnlySequence<byte> buffer,
        ChannelWriter<IMemoryOwner<byte>> writer,
        List<IMemoryOwner<byte>>? pendingFrames)
    {
        var consumed = buffer.Start;
        var examined = buffer.End;
        var remainingLength = buffer.Length;

        var sequenceReader = new SequenceReader<byte>(buffer);
        while (sequenceReader.TryReadTo(out ReadOnlySequence<byte> frame, _delimiter.Span, true))
        {
            if (frame.Length > _maxFrameBytes)
            {
                throw new TcpProtocolException(
                    $"Frame length {frame.Length} exceeds max {_maxFrameBytes} bytes.");
            }

            consumed = sequenceReader.Position;
            examined = consumed;

            if (_overflowStrategy == ChannelOverflowStrategy.Block)
            {
                PooledFrame? pooled = null;
                try
                {
                    pooled = frame.Length == 0
                        ? PooledFrame.CreateEmpty()
                        : PooledFrame.CopyFrom(frame);
                    pendingFrames!.Add(pooled);
                    pooled = null;
                }
                finally
                {
                    pooled?.Dispose();
                }
            }
            else
            {
                PooledFrame? pooled = null;
                try
                {
                    pooled = frame.Length == 0
                        ? PooledFrame.CreateEmpty()
                        : PooledFrame.CopyFrom(frame);

                    if (writer.TryWrite(pooled))
                    {
                        pooled = null;
                    }
                }
                finally
                {
                    pooled?.Dispose();
                }
            }
        }

        remainingLength = buffer.Slice(consumed).Length;
        return new ParseResult(consumed, examined, remainingLength);
    }

    private static async ValueTask WriteFramesAsync(
        ChannelWriter<IMemoryOwner<byte>> writer,
        List<IMemoryOwner<byte>> frames,
        CancellationToken cancellationToken)
    {
        var index = 0;
        try
        {
            for (; index < frames.Count; index++)
            {
                await writer.WriteAsync(frames[index], cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            for (var i = index; i < frames.Count; i++)
            {
                frames[i].Dispose();
            }
            throw;
        }
    }

    private static void DisposePendingFrames(List<IMemoryOwner<byte>>? frames)
    {
        if (frames is null || frames.Count == 0)
        {
            return;
        }

        foreach (var frame in frames)
        {
            frame.Dispose();
        }
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
    private const int DefaultChannelCapacity = 64;

    private readonly record struct ParseResult(
        SequencePosition Consumed,
        SequencePosition Examined,
        long RemainingLength);
}
