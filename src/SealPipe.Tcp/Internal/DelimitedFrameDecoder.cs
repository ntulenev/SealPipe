using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SealPipe.Tcp.Exceptions;

namespace SealPipe.Tcp.Internal;

internal sealed class DelimitedFrameDecoder
{
    public DelimitedFrameDecoder(ReadOnlyMemory<byte> delimiter, int maxFrameBytes)
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

    public IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFramesAsync(
        PipeReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var channel = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(DefaultChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        using var decodeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var decodeTask = RunDecodeAsync(reader, channel.Writer, decodeCts.Token);

        return ReadAllWithCleanupAsync(channel.Reader, decodeTask, decodeCts, cancellationToken);
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAllWithCleanupAsync(
        ChannelReader<ReadOnlyMemory<byte>> reader,
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
        }
    }

    private async Task RunDecodeAsync(
        PipeReader reader,
        ChannelWriter<ReadOnlyMemory<byte>> writer,
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

                ParseFrames(buffer, writer, out var consumed, out var examined, out var remainingLength);

                if (remainingLength > _maxFrameBytes)
                {
                    throw new TcpProtocolException(
                        $"Frame length {remainingLength} exceeds max {_maxFrameBytes} bytes.");
                }

                reader.AdvanceTo(consumed, examined);

                if (result.IsCompleted)
                {
                    if (remainingLength > 0)
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

    private void ParseFrames(
        ReadOnlySequence<byte> buffer,
        ChannelWriter<ReadOnlyMemory<byte>> writer,
        out SequencePosition consumed,
        out SequencePosition examined,
        out long remainingLength)
    {
        consumed = buffer.Start;
        examined = buffer.End;
        remainingLength = buffer.Length;

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

            if (frame.Length == 0)
            {
                _ = writer.TryWrite(ReadOnlyMemory<byte>.Empty);
            }
            else
            {
                _ = writer.TryWrite(frame.ToArray());
            }
        }

        remainingLength = buffer.Slice(consumed).Length;
    }

    private readonly ReadOnlyMemory<byte> _delimiter;
    private readonly int _maxFrameBytes;
    private const int DefaultChannelCapacity = 64;
}
