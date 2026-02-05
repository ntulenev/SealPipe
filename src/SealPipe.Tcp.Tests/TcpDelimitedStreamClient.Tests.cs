using System.Net;
using System.Net.Sockets;
using System.Text;

using FluentAssertions;

namespace SealPipe.Tcp.Tests;

public sealed class TcpDelimitedStreamClientTests
{
    [Fact(DisplayName = "Throws when options are invalid")]
    [Trait("Category", "Unit")]
    public void ThrowsWhenOptionsAreInvalid()
    {
        // Act & Assert
        var act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(host: ""));
        act.Should().Throw<ArgumentException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(port: 0));
        act.Should().Throw<ArgumentOutOfRangeException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(delimiter: ""));
        act.Should().Throw<ArgumentException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(encoding: ""));
        act.Should().Throw<ArgumentException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(encoding: "not-a-real-encoding"));
        act.Should().Throw<ArgumentException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(maxFrameBytes: 0));
        act.Should().Throw<ArgumentOutOfRangeException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(connectTimeout: TimeSpan.Zero));
        act.Should().Throw<ArgumentOutOfRangeException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(readTimeout: TimeSpan.Zero));
        act.Should().Throw<ArgumentOutOfRangeException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(receiveBufferSize: 0));
        act.Should().Throw<ArgumentOutOfRangeException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(keepAlive: new KeepAliveOptions
            {
                Enabled = true,
                TcpKeepAliveTime = TimeSpan.Zero
            }));
        act.Should().Throw<ArgumentOutOfRangeException>();

        // Act & Assert
        act = () => TcpDelimitedStreamClient.Create(
            CreateOptions(keepAlive: new KeepAliveOptions
            {
                Enabled = true,
                TcpKeepAliveInterval = TimeSpan.Zero
            }));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Throws when reading after dispose")]
    [Trait("Category", "Unit")]
    public async Task ThrowsWhenReadingAfterDispose()
    {
        // Arrange
        var client = TcpDelimitedStreamClient.Create(CreateValidOptions());
        await client.DisposeAsync();

        // Act
        var act = async () =>
        {
            await using var enumerator = client.ReadMessagesAsync().GetAsyncEnumerator();
            await enumerator.MoveNextAsync();
        };

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact(DisplayName = "Rejects concurrent readers")]
    [Trait("Category", "Unit")]
    public async Task RejectsConcurrentReaders()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunScriptedServerAsync(
            listener,
            async stream =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            });

        var client = TcpDelimitedStreamClient.Create(new TcpDelimitedClientOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Delimiter = "\n",
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromSeconds(5),
            MaxFrameBytes = 1024,
            Reconnect = new ReconnectOptions
            {
                Enabled = false
            },
            KeepAlive = new KeepAliveOptions
            {
                Enabled = false
            }
        });

        try
        {
            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var firstEnumerator = client.ReadMessagesAsync(cts.Token).GetAsyncEnumerator();
            var firstMove = firstEnumerator.MoveNextAsync().AsTask();

            var act = async () =>
            {
                await using var secondEnumerator = client.ReadMessagesAsync(cts.Token).GetAsyncEnumerator();
                await secondEnumerator.MoveNextAsync();
            };

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>();

            await cts.CancelAsync();
            try
            {
                await firstMove;
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            await client.DisposeAsync();
            await serverTask;
            listener.Stop();
            listener.Dispose();
        }
    }

    [Fact(DisplayName = "Reconnects after disconnect")]
    [Trait("Category", "Unit")]
    public async Task ReconnectsAfterDisconnect()
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunScriptedServerAsync(
            listener,
            async stream =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("first\n"));
                await stream.FlushAsync();
            },
            async stream =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("second\n"));
                await stream.FlushAsync();
            });

        var client = TcpDelimitedStreamClient.Create(new TcpDelimitedClientOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Delimiter = "\n",
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromSeconds(2),
            MaxFrameBytes = 1024,
            Reconnect = new ReconnectOptions
            {
                Enabled = true,
                InitialDelay = TimeSpan.FromMilliseconds(50),
                MaxDelay = TimeSpan.FromMilliseconds(200),
                Backoff = BackoffStrategy.ExponentialWithJitter
            },
            KeepAlive = new KeepAliveOptions
            {
                Enabled = false
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var messages = new List<string>();

        // Act
        await foreach (var msg in client.ReadMessagesAsync(cts.Token))
        {
            messages.Add(msg);
            if (messages.Count == 2)
            {
                await cts.CancelAsync();
                break;
            }
        }

        await client.DisposeAsync();
        await serverTask;
        listener.Dispose();

        // Assert
        messages.Should().Equal(["first", "second"]);
    }

    private static async Task RunScriptedServerAsync(
        TcpListener listener,
        params Func<NetworkStream, Task>[] scripts)
    {
        foreach (var script in scripts)
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            await script(stream);
        }

        listener.Stop();
    }

    private static TcpDelimitedClientOptions CreateValidOptions()
    {
        return CreateOptions();
    }

    private static TcpDelimitedClientOptions CreateOptions(
        string? host = null,
        int? port = null,
        string? delimiter = null,
        string? encoding = null,
        int? maxFrameBytes = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? readTimeout = null,
        int? receiveBufferSize = null,
        KeepAliveOptions? keepAlive = null)
    {
        return new TcpDelimitedClientOptions
        {
            Host = host ?? "127.0.0.1",
            Port = port ?? 12345,
            Delimiter = delimiter ?? "\n",
            Encoding = encoding ?? "utf-8",
            ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(2),
            ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(2),
            ReceiveBufferSize = receiveBufferSize ?? 4096,
            MaxFrameBytes = maxFrameBytes ?? 1024,
            Reconnect = new ReconnectOptions
            {
                Enabled = false
            },
            KeepAlive = keepAlive ?? new KeepAliveOptions
            {
                Enabled = false
            }
        };
    }
}
