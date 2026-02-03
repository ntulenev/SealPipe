namespace SealPipe.Tcp.Exceptions;

/// <summary>
/// Represents an error that occurs while establishing a TCP connection.
/// </summary>
public sealed class TcpConnectException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TcpConnectException"/> class.
    /// </summary>
    /// <param name="message">The error message. Cannot be null or empty.</param>
    public TcpConnectException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpConnectException"/> class.
    /// </summary>
    /// <param name="message">The error message. Cannot be null or empty.</param>
    /// <param name="innerException">The inner exception that caused the error.</param>
    public TcpConnectException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
