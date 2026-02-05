using System.Net.Sockets;

using Microsoft.Extensions.Logging;

using SealPipe.Tcp.Exceptions;

namespace SealPipe.Tcp.Internal;

/// <summary>
/// Creates and configures TCP sockets for the client.
/// </summary>
internal sealed class SocketConnector
{
    private static readonly Action<ILogger, Exception?> LogKeepAliveFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, "KeepAliveConfigFailed"),
            "Failed to configure TCP keep-alive options.");

    /// <summary>
    /// Initializes a new instance of the <see cref="SocketConnector"/> class.
    /// </summary>
    /// <param name="options">The client configuration.</param>
    /// <param name="logger">The logger instance, if any.</param>
    public SocketConnector(
        TcpDelimitedClientOptions options,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Opens a TCP socket to the configured host and port.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the connection attempt.</param>
    /// <returns>The connected socket.</returns>
    public async Task<Socket> ConnectAsync(CancellationToken cancellationToken)
    {
        Socket socket = null!;
        try
        {
            socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            ConfigureSocket(socket);
            await ConnectWithTimeoutAsync(socket, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch (Exception ex)
        {
            socket?.Dispose();
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            if (ex is TcpConnectException)
            {
                throw;
            }

            throw new TcpConnectException(
                $"Failed to connect to {_options.Host}:{_options.Port}.", ex);
        }
    }

    private void ConfigureSocket(Socket socket)
    {
        if (_options.KeepAlive.Enabled)
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(
                    SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveTime,
                    (int)_options.KeepAlive.TcpKeepAliveTime.TotalSeconds);
                socket.SetSocketOption(
                    SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveInterval,
                    (int)_options.KeepAlive.TcpKeepAliveInterval.TotalSeconds);
            }
            catch (Exception ex) when (
                ex is SocketException ||
                ex is ArgumentException ||
                ex is PlatformNotSupportedException ||
                ex is NotSupportedException)
            {
                if (_logger is not null)
                {
                    LogKeepAliveFailed(_logger, ex);
                }
            }
        }
    }

    private async Task ConnectWithTimeoutAsync(Socket socket, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.ConnectTimeout);

        try
        {
            await socket.ConnectAsync(_options.Host, _options.Port, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TcpConnectException(
                $"Connect to {_options.Host}:{_options.Port} timed out.");
        }
    }

    private readonly TcpDelimitedClientOptions _options;
    private readonly ILogger? _logger;
}
