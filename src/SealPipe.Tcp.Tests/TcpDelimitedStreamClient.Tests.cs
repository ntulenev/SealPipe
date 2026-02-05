using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using FluentAssertions;

using SealPipe.Tcp.Exceptions;

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
        await using var server = StartScriptedServer(
            async stream =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            });
        var port = server.Port;
        var serverTask = server.Completion;

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
        }
    }

    [Fact(DisplayName = "Reconnects after disconnect")]
    [Trait("Category", "Unit")]
    public async Task ReconnectsAfterDisconnect()
    {
        // Arrange
        await using var server = StartScriptedServer(
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
        var port = server.Port;
        var serverTask = server.Completion;

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

        // Assert
        messages.Should().Equal(["first", "second"]);
    }

    [Fact(DisplayName = "Drops frames when channel overflows")]
    [Trait("Category", "Unit")]
    public async Task DropsFramesWhenChannelOverflows()
    {
        // Arrange
        const int totalFrames = 200;
        await using var server = StartScriptedServer(
            async stream =>
            {
                for (var i = 0; i < totalFrames; i++)
                {
                    var payload = Encoding.UTF8.GetBytes($"msg-{i}\n");
                    await stream.WriteAsync(payload);
                }

                await stream.FlushAsync();
            });
        var port = server.Port;
        var serverTask = server.Completion;
        var client = TcpDelimitedStreamClient.Create(new TcpDelimitedClientOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Delimiter = "\n",
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromSeconds(10),
            MaxFrameBytes = 1024,
            ChannelCapacity = 1,
            ChannelOverflowStrategy = ChannelOverflowStrategy.Drop,
            Reconnect = new ReconnectOptions
            {
                Enabled = false
            },
            KeepAlive = new KeepAliveOptions
            {
                Enabled = false
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            // Act
            await using var enumerator = client.ReadMessagesAsync(cts.Token).GetAsyncEnumerator();
            var hasFirst = await enumerator.MoveNextAsync();
            hasFirst.Should().BeTrue();

            await Task.Delay(200, cts.Token);
            await cts.CancelAsync();

            try
            {
                while (await enumerator.MoveNextAsync())
                {
                }
            }
            catch (OperationCanceledException)
            {
            }

            // Assert
            client.Diagnostics.FramesDropped.Should().BeGreaterThan(0);
        }
        finally
        {
            await client.DisposeAsync();
            await serverTask;
        }
    }

    [Fact(DisplayName = "Blocks instead of dropping when channel overflows")]
    [Trait("Category", "Unit")]
    public async Task BlocksInsteadOfDroppingWhenChannelOverflows()
    {
        // Arrange
        const int totalFrames = 10;
        await using var server = StartScriptedServer(
            async stream =>
            {
                for (var i = 0; i < totalFrames; i++)
                {
                    var payload = Encoding.UTF8.GetBytes($"msg-{i}\n");
                    await stream.WriteAsync(payload);
                }

                await stream.FlushAsync();
            });
        var port = server.Port;
        var serverTask = server.Completion;
        var client = TcpDelimitedStreamClient.Create(new TcpDelimitedClientOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Delimiter = "\n",
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromSeconds(2),
            MaxFrameBytes = 1024,
            ChannelCapacity = 1,
            ChannelOverflowStrategy = ChannelOverflowStrategy.Block,
            Reconnect = new ReconnectOptions
            {
                Enabled = false
            },
            KeepAlive = new KeepAliveOptions
            {
                Enabled = false
            }
        });

        var messages = new List<string>();

        try
        {
            // Act
            await foreach (var msg in client.ReadMessagesAsync())
            {
                messages.Add(msg);
                await Task.Delay(10);
            }

            // Assert
            client.Diagnostics.FramesDropped.Should().Be(0);
            messages.Should().HaveCount(totalFrames);
        }
        finally
        {
            await client.DisposeAsync();
            await serverTask;
        }
    }

    [Fact(DisplayName = "Read timeout surfaces when reconnect disabled")]
    [Trait("Category", "Unit")]
    public async Task ReadTimeoutSurfacesWhenReconnectDisabled()
    {
        // Arrange
        await using var server = StartScriptedServer(
            async stream =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300));
            });
        var port = server.Port;
        var serverTask = server.Completion;
        var client = TcpDelimitedStreamClient.Create(new TcpDelimitedClientOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Delimiter = "\n",
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromMilliseconds(100),
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
            var act = async () =>
            {
                await foreach (var _ in client.ReadMessagesAsync())
                {
                }
            };

            // Assert
            await act.Should().ThrowAsync<TcpReadTimeoutException>();
        }
        finally
        {
            await client.DisposeAsync();
            await serverTask;
        }
    }

    [Fact(DisplayName = "Read timeout triggers reconnect attempts")]
    [Trait("Category", "Unit")]
    public async Task ReadTimeoutTriggersReconnectAttempts()
    {
        // Arrange
        await using var server = StartScriptedServer(
            async stream =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300));
            },
            async stream =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300));
            });
        var port = server.Port;
        var serverTask = server.Completion;
        var client = TcpDelimitedStreamClient.Create(new TcpDelimitedClientOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Delimiter = "\n",
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromMilliseconds(100),
            MaxFrameBytes = 1024,
            Reconnect = new ReconnectOptions
            {
                Enabled = true,
                InitialDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(20),
                MaxAttempts = 2
            },
            KeepAlive = new KeepAliveOptions
            {
                Enabled = false
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        try
        {
            // Act
            try
            {
                await foreach (var _ in client.ReadMessagesAsync(cts.Token))
                {
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (TcpConnectException)
            {
            }

            // Assert
            client.Diagnostics.ReconnectAttempts.Should().BeGreaterThan(0);
        }
        finally
        {
            await client.DisposeAsync();
            await serverTask;
        }
    }

    private static ScriptedServer StartScriptedServer(
        params Func<NetworkStream, Task>[] scripts)
    {
        return new ScriptedServer(scripts);
    }

    private sealed class ScriptedServer : IAsyncDisposable
    {
        public ScriptedServer(params Func<NetworkStream, Task>[] scripts)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Completion = RunAsync(scripts);
        }

        public int Port { get; }
        public Task Completion { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Completion.ConfigureAwait(false);
            }
            finally
            {
                DisposeListenerOnce();
            }
        }

        private async Task RunAsync(Func<NetworkStream, Task>[] scripts)
        {
            try
            {
                foreach (var script in scripts)
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    await script(stream);
                }
            }
            finally
            {
                DisposeListenerOnce();
            }
        }

        private readonly TcpListener _listener;
        private int _disposed;

        private void DisposeListenerOnce()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _listener.Stop();
            _listener.Dispose();
        }
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
