using System.Text;

namespace SealPipe.Tcp.Internal;

internal static class FrameToStringDecoder
{
    public static string Decode(ReadOnlyMemory<byte> frame, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.GetString(frame.Span);
    }
}
