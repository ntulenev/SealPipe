namespace SealPipe.Tcp.Exceptions;

/// <summary>
/// Represents a protocol error when parsing TCP delimited frames.
/// </summary>
public sealed class TcpProtocolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TcpProtocolException"/> class.
    /// </summary>
    public TcpProtocolException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpProtocolException"/> class.
    /// </summary>
    /// <param name="message">The error message. Cannot be null or empty.</param>
    public TcpProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpProtocolException"/> class.
    /// </summary>
    /// <param name="message">The error message. Cannot be null or empty.</param>
    /// <param name="innerException">The inner exception that caused the error.</param>
    public TcpProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
