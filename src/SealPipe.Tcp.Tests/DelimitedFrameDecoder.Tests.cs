using System.IO.Pipelines;
using System.Text;
using System.Buffers;

using FluentAssertions;

using SealPipe.Tcp.Exceptions;
using SealPipe.Tcp.Internal;

namespace SealPipe.Tcp.Tests;

public sealed class DelimitedFrameDecoderTests
{
    [Fact(DisplayName = "Throws when delimiter is empty")]
    [Trait("Category", "Unit")]
    public void ThrowsWhenDelimiterIsEmpty()
    {
        // Arrange & Act
        var act = () => new DelimitedFrameDecoder(
            ReadOnlyMemory<byte>.Empty,
            64,
            ChannelOverflowStrategy.Drop,
            64,
            new TcpDelimitedClientDiagnostics());

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Throws when max frame bytes is not positive")]
    [Trait("Category", "Unit")]
    public void ThrowsWhenMaxFrameBytesIsNotPositive()
    {
        // Arrange & Act
        var act = () => new DelimitedFrameDecoder(
            Encoding.ASCII.GetBytes("\n"),
            0,
            ChannelOverflowStrategy.Drop,
            64,
            new TcpDelimitedClientDiagnostics());

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Throws when channel capacity is not positive")]
    [Trait("Category", "Unit")]
    public void ThrowsWhenChannelCapacityIsNotPositive()
    {
        // Arrange & Act
        var act = () => new DelimitedFrameDecoder(
            Encoding.ASCII.GetBytes("\n"),
            64,
            ChannelOverflowStrategy.Drop,
            0,
            new TcpDelimitedClientDiagnostics());

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Reads delimiter split across segments")]
    [Trait("Category", "Unit")]
    public async Task ReadsDelimiterSplitAcrossSegments()
    {
        // Arrange
        var delimiter = Encoding.ASCII.GetBytes("\r\n");
        var decoder = new DelimitedFrameDecoder(
            delimiter,
            64,
            ChannelOverflowStrategy.Drop,
            64,
            new TcpDelimitedClientDiagnostics());
        var pipe = new Pipe();

        // Act
        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("abc\r"));
        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("\n"));
        await pipe.Writer.CompleteAsync();

        var frames = await CollectAsync(decoder.ReadFramesAsync(pipe.Reader, CancellationToken.None));

        // Assert
        frames.Should().HaveCount(1);
        Encoding.ASCII.GetString(frames[0]).Should().Be("abc");
    }

    [Fact(DisplayName = "Reads multiple frames in one buffer")]
    [Trait("Category", "Unit")]
    public async Task ReadsMultipleFramesInOneBuffer()
    {
        // Arrange
        var decoder = new DelimitedFrameDecoder(
            Encoding.ASCII.GetBytes("\n"),
            64,
            ChannelOverflowStrategy.Drop,
            64,
            new TcpDelimitedClientDiagnostics());
        var pipe = new Pipe();

        // Act
        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("a\nb\n"));
        await pipe.Writer.CompleteAsync();

        var frames = await CollectAsync(decoder.ReadFramesAsync(pipe.Reader, CancellationToken.None));

        // Assert
        frames.Should().HaveCount(2);
        Encoding.ASCII.GetString(frames[0]).Should().Be("a");
        Encoding.ASCII.GetString(frames[1]).Should().Be("b");
    }

    [Fact(DisplayName = "Reads empty frame")]
    [Trait("Category", "Unit")]
    public async Task ReadsEmptyFrame()
    {
        // Arrange
        var decoder = new DelimitedFrameDecoder(
            Encoding.ASCII.GetBytes("\n"),
            64,
            ChannelOverflowStrategy.Drop,
            64,
            new TcpDelimitedClientDiagnostics());
        var pipe = new Pipe();

        // Act
        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("\n"));
        await pipe.Writer.CompleteAsync();

        var frames = await CollectAsync(decoder.ReadFramesAsync(pipe.Reader, CancellationToken.None));

        // Assert
        frames.Should().HaveCount(1);
        frames[0].Length.Should().Be(0);
    }

    [Fact(DisplayName = "Throws when frame exceeds max bytes")]
    [Trait("Category", "Unit")]
    public async Task ThrowsWhenFrameExceedsMaxBytes()
    {
        // Arrange
        var decoder = new DelimitedFrameDecoder(
            Encoding.ASCII.GetBytes("\n"),
            4,
            ChannelOverflowStrategy.Drop,
            64,
            new TcpDelimitedClientDiagnostics());
        var pipe = new Pipe();

        // Act
        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("12345"));
        await pipe.Writer.CompleteAsync();

        var act = async () => await ConsumeAsync(
            decoder.ReadFramesAsync(pipe.Reader, CancellationToken.None));

        // Assert
        await act.Should().ThrowAsync<TcpProtocolException>();
    }

    [Fact(DisplayName = "Throws when stream ends with incomplete frame")]
    [Trait("Category", "Unit")]
    public async Task ThrowsWhenStreamEndsWithIncompleteFrame()
    {
        // Arrange
        var decoder = new DelimitedFrameDecoder(
            Encoding.ASCII.GetBytes("\n"),
            64,
            ChannelOverflowStrategy.Drop,
            64,
            new TcpDelimitedClientDiagnostics());
        var pipe = new Pipe();

        // Act
        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("incomplete"));
        await pipe.Writer.CompleteAsync();

        var act = async () => await ConsumeAsync(
            decoder.ReadFramesAsync(pipe.Reader, CancellationToken.None));

        // Assert
        await act.Should().ThrowAsync<TcpProtocolException>();
    }


    private static async Task<List<byte[]>> CollectAsync(
        IAsyncEnumerable<IMemoryOwner<byte>> frames)
    {
        var results = new List<byte[]>();
        await foreach (var frame in frames)
        {
            using (frame)
            {
                results.Add(frame.Memory.ToArray());
            }
        }

        return results;
    }

    private static async Task ConsumeAsync(IAsyncEnumerable<IMemoryOwner<byte>> frames)
    {
        await foreach (var frame in frames)
        {
            frame.Dispose();
        }
    }
}
