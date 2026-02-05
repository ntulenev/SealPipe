using System.Buffers;
using System.Threading;

namespace SealPipe.Tcp.Internal;

internal sealed class PooledFrame : IMemoryOwner<byte>
{
    private PooledFrame(byte[] buffer, int length, bool returnToPool)
    {
        _buffer = buffer;
        _length = length;
        _returnToPool = returnToPool;
    }

    public Memory<byte> Memory
    {
        get
        {
            var buffer = _buffer;
            return buffer is null ? Memory<byte>.Empty : buffer.AsMemory(0, _length);
        }
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null && _returnToPool)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

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

    public static PooledFrame CreateEmpty() => new(Array.Empty<byte>(), 0, returnToPool: false);

    private byte[]? _buffer;
    private readonly int _length;
    private readonly bool _returnToPool;
}
