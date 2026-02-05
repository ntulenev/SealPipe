using System.Text;

namespace SealPipe.Tcp.Internal;

/// <summary>
/// Decodes frame payloads into strings using a specified encoding.
/// </summary>
internal static class FrameToStringDecoder
{
    /// <summary>
    /// Decodes the provided frame to a string.
    /// </summary>
    /// <param name="frame">The frame payload.</param>
    /// <param name="encoding">The encoding to use.</param>
    /// <returns>The decoded string.</returns>
    public static string Decode(ReadOnlyMemory<byte> frame, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.GetString(frame.Span);
    }
}
