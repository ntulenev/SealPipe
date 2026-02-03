using System.Buffers;
using System.IO.Pipelines;
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

        var channel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _ = RunDecodeAsync(reader, channel.Writer, cancellationToken);
        return channel.Reader.ReadAllAsync(cancellationToken);
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
}
