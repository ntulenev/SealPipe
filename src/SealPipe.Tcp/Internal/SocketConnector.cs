using System.Net.Sockets;

using Microsoft.Extensions.Logging;

using SealPipe.Tcp.Exceptions;

namespace SealPipe.Tcp.Internal;

internal sealed class SocketConnector
{
    public SocketConnector(
        TcpDelimitedClientOptions options,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
    }

    public async Task<Socket> ConnectAsync(CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            ConfigureSocket(socket);
            await ConnectWithTimeoutAsync(socket, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch (Exception ex)
        {
            socket.Dispose();
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
#pragma warning disable CA1031 // Do not catch general exception types
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
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to configure TCP keep-alive options.");
            }
#pragma warning restore CA1031 // Do not catch general exception types
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
