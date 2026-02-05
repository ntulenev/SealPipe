using System.Buffers;

using FluentAssertions;

using SealPipe.Tcp.Internal;

namespace SealPipe.Tcp.Tests;

public sealed class PooledFrameTests
{
    [Fact(DisplayName = "CopyFrom copies data from single segment")]
    [Trait("Category", "Unit")]
    public void CopyFromCopiesDataFromSingleSegment()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4 };
        var sequence = new ReadOnlySequence<byte>(data);

        // Act
        using var frame = PooledFrame.CopyFrom(sequence);

        // Assert
        frame.Memory.ToArray().Should().Equal(data);
    }

    [Fact(DisplayName = "CopyFrom copies data from multiple segments")]
    [Trait("Category", "Unit")]
    public void CopyFromCopiesDataFromMultipleSegments()
    {
        // Arrange
        var sequence = CreateSequence(
            new byte[] { 1, 2 },
            new byte[] { 3, 4, 5 });

        // Act
        using var frame = PooledFrame.CopyFrom(sequence);

        // Assert
        frame.Memory.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4, 5 });
    }

    [Fact(DisplayName = "CreateEmpty returns empty memory and can be disposed")]
    [Trait("Category", "Unit")]
    public void CreateEmptyReturnsEmptyMemoryAndCanBeDisposed()
    {
        // Arrange & Act
        using var frame = PooledFrame.CreateEmpty();

        // Assert
        frame.Memory.Length.Should().Be(0);
    }

    [Fact(DisplayName = "Dispose clears memory and is idempotent")]
    [Trait("Category", "Unit")]
    public void DisposeClearsMemoryAndIsIdempotent()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        using var frame = PooledFrame.CopyFrom(new ReadOnlySequence<byte>(data));

        // Act
        frame.Dispose();
        frame.Dispose();

        // Assert
        frame.Memory.Length.Should().Be(0);
    }

    private static ReadOnlySequence<byte> CreateSequence(params byte[][] segments)
    {
        BufferSegment? first = null;
        BufferSegment? current = null;

        foreach (var segment in segments)
        {
            var next = new BufferSegment(segment);
            if (first is null)
            {
                first = next;
            }
            else
            {
                current!.SetNext(next);
            }

            current = next;
        }

        return new ReadOnlySequence<byte>(first!, 0, current!, current!.Memory.Length);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(byte[] buffer)
        {
            Memory = buffer;
        }

        public void SetNext(BufferSegment next)
        {
            next.RunningIndex = RunningIndex + Memory.Length;
            Next = next;
        }
    }
}
