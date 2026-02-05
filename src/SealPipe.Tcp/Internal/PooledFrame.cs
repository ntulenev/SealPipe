using System.Buffers;
using System.Threading;

namespace SealPipe.Tcp.Internal;

/// <summary>
/// Represents a pooled frame buffer that must be disposed to return it to the pool.
/// </summary>
internal sealed class PooledFrame : IMemoryOwner<byte>
{
    private PooledFrame(byte[] buffer, int length, bool returnToPool)
    {
        _buffer = buffer;
        _length = length;
        _returnToPool = returnToPool;
    }

    /// <summary>
    /// Gets the buffer memory for this frame.
    /// </summary>
    public Memory<byte> Memory
    {
        get
        {
            var buffer = _buffer;
            return buffer is null ? Memory<byte>.Empty : buffer.AsMemory(0, _length);
        }
    }

    /// <summary>
    /// Returns the underlying buffer to the pool if owned.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null && _returnToPool)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Copies the provided sequence into a pooled buffer.
    /// </summary>
    /// <param name="sequence">The sequence to copy.</param>
    /// <returns>A pooled frame containing the copied data.</returns>
    public static PooledFrame CopyFrom(ReadOnlySequence<byte> sequence)
    {
        if (sequence.Length == 0)
        {
            return CreateEmpty();
        }

        var length = checked((int)sequence.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        var frame = new PooledFrame(buffer, length, returnToPool: true);
        sequence.CopyTo(frame.Memory.Span);
        return frame;
    }

    /// <summary>
    /// Creates an empty frame that does not allocate from the pool.
    /// </summary>
    /// <returns>An empty pooled frame.</returns>
    public static PooledFrame CreateEmpty() => new(_emptyBuffer, 0, returnToPool: false);

    private byte[]? _buffer;
    private readonly int _length;
    private readonly bool _returnToPool;
    private int _disposed;
    private static readonly byte[] _emptyBuffer = [];
}
