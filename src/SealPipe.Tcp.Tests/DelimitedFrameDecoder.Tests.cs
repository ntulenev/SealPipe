using System.IO.Pipelines;
using System.Text;

using FluentAssertions;

using SealPipe.Tcp.Exceptions;
using SealPipe.Tcp.Internal;

namespace SealPipe.Tcp.Tests;

public sealed class DelimitedFrameDecoderTests
{
    [Fact(DisplayName = "Reads delimiter split across segments")]
    [Trait("Category", "Unit")]
    public async Task ReadsDelimiterSplitAcrossSegments()
    {
        var delimiter = Encoding.ASCII.GetBytes("\r\n");
        var decoder = new DelimitedFrameDecoder(delimiter, 64);
        var pipe = new Pipe();

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("abc\r"));
        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("\n"));
        await pipe.Writer.CompleteAsync();

        var frames = await CollectAsync(decoder.ReadFramesAsync(pipe.Reader, CancellationToken.None));
        frames.Should().HaveCount(1);
        Encoding.ASCII.GetString(frames[0].Span).Should().Be("abc");
    }

    [Fact(DisplayName = "Reads multiple frames in one buffer")]
    [Trait("Category", "Unit")]
    public async Task ReadsMultipleFramesInOneBuffer()
    {
        var decoder = new DelimitedFrameDecoder(Encoding.ASCII.GetBytes("\n"), 64);
        var pipe = new Pipe();

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("a\nb\n"));
        await pipe.Writer.CompleteAsync();

        var frames = await CollectAsync(decoder.ReadFramesAsync(pipe.Reader, CancellationToken.None));
        frames.Should().HaveCount(2);
        Encoding.ASCII.GetString(frames[0].Span).Should().Be("a");
        Encoding.ASCII.GetString(frames[1].Span).Should().Be("b");
    }

    [Fact(DisplayName = "Reads empty frame")]
    [Trait("Category", "Unit")]
    public async Task ReadsEmptyFrame()
    {
        var decoder = new DelimitedFrameDecoder(Encoding.ASCII.GetBytes("\n"), 64);
        var pipe = new Pipe();

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("\n"));
        await pipe.Writer.CompleteAsync();

        var frames = await CollectAsync(decoder.ReadFramesAsync(pipe.Reader, CancellationToken.None));
        frames.Should().HaveCount(1);
        frames[0].IsEmpty.Should().BeTrue();
    }

    [Fact(DisplayName = "Throws when frame exceeds max bytes")]
    [Trait("Category", "Unit")]
    public async Task ThrowsWhenFrameExceedsMaxBytes()
    {
        var decoder = new DelimitedFrameDecoder(Encoding.ASCII.GetBytes("\n"), 4);
        var pipe = new Pipe();

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("12345"));
        await pipe.Writer.CompleteAsync();

        var act = async () => await ConsumeAsync(
            decoder.ReadFramesAsync(pipe.Reader, CancellationToken.None));

        await act.Should().ThrowAsync<TcpProtocolException>();
    }

    private static async Task<List<ReadOnlyMemory<byte>>> CollectAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> frames)
    {
        var results = new List<ReadOnlyMemory<byte>>();
        await foreach (var frame in frames)
        {
            results.Add(frame);
        }

        return results;
    }

    private static async Task ConsumeAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> frames)
    {
        await foreach (var _ in frames)
        {
        }
    }
}
