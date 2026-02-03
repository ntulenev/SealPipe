using System.Net;
using System.Net.Sockets;
using System.Text;

using FluentAssertions;

namespace SealPipe.Tcp.Tests;

public sealed class TcpDelimitedStreamClientTests
{
    [Fact(DisplayName = "Reconnects after disconnect")]
    [Trait("Category", "Unit")]
    public async Task ReconnectsAfterDisconnect()
    {
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
}
