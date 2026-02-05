namespace SealPipe.Tcp.Exceptions;

/// <summary>
/// Represents an error when a TCP read operation exceeds the configured timeout.
/// </summary>
public sealed class TcpReadTimeoutException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TcpReadTimeoutException"/> class.
    /// </summary>
    public TcpReadTimeoutException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpReadTimeoutException"/> class.
    /// </summary>
    /// <param name="message">The error message. Cannot be null or empty.</param>
    public TcpReadTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpReadTimeoutException"/> class.
    /// </summary>
    /// <param name="message">The error message. Cannot be null or empty.</param>
    /// <param name="innerException">The inner exception that caused the error.</param>
    public TcpReadTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
