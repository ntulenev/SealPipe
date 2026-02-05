using System.Net;
using System.Net.Sockets;

using FluentAssertions;

using SealPipe.Tcp.Exceptions;
using SealPipe.Tcp.Internal;

namespace SealPipe.Tcp.Tests;

public sealed class SocketConnectorTests
{
    [Fact(DisplayName = "ConnectAsync returns a connected socket")]
    [Trait("Category", "Unit")]
    public async Task ConnectAsyncReturnsAConnectedSocket()
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var options = CreateOptions(port);
        var connector = new SocketConnector(options, logger: null);

        // Act
        using var socket = await connector.ConnectAsync(CancellationToken.None);
        using var accepted = await acceptTask;

        // Assert
        socket.Connected.Should().BeTrue();
        accepted.Connected.Should().BeTrue();
    }

    [Fact(DisplayName = "ConnectAsync wraps failures in TcpConnectException")]
    [Trait("Category", "Unit")]
    public async Task ConnectAsyncWrapsFailuresInTcpConnectException()
    {
        // Arrange
        var port = GetUnusedPort();
        var options = CreateOptions(port);
        var connector = new SocketConnector(options, logger: null);

        // Act
        var act = async () => await connector.ConnectAsync(CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<TcpConnectException>();
        exception.Which.Message.Should().Contain($"{options.Host}:{options.Port}");
    }

    [Fact(DisplayName = "ConnectAsync wraps cancellation in TcpConnectException")]
    [Trait("Category", "Unit")]
    public async Task ConnectAsyncWrapsCancellationInTcpConnectException()
    {
        // Arrange
        var options = CreateOptions(12345);
        var connector = new SocketConnector(options, logger: null);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = async () => await connector.ConnectAsync(cts.Token);

        // Assert
        var exception = await act.Should().ThrowAsync<TcpConnectException>();
        exception.Which.InnerException.Should().BeAssignableTo<OperationCanceledException>();
    }

    private static TcpDelimitedClientOptions CreateOptions(int port)
    {
        return new TcpDelimitedClientOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Delimiter = "\n",
            ConnectTimeout = TimeSpan.FromSeconds(1),
            ReadTimeout = TimeSpan.FromSeconds(1),
            MaxFrameBytes = 1024,
            Reconnect = new ReconnectOptions
            {
                Enabled = false
            },
            KeepAlive = new KeepAliveOptions
            {
                Enabled = false
            }
        };
    }

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
