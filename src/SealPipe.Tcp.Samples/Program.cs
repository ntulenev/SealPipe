using System.Net;
using System.Net.Sockets;
using System.Text;

using SealPipe.Tcp;

const int demoPort = 19000;
using var demoCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

var serverTask = RunDemoServerAsync(demoPort, demoCts.Token);

var client = TcpDelimitedStreamClient.Create(
    new TcpDelimitedClientOptions
    {
        Host = "127.0.0.1",
        Port = demoPort,
        Delimiter = "\n",
        Encoding = "utf-8",
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ReadTimeout = TimeSpan.FromSeconds(5),
        Reconnect = new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(250),
            MaxDelay = TimeSpan.FromSeconds(2),
            Backoff = BackoffStrategy.ExponentialWithJitter,
            MaxAttempts = 5
        },
        MaxFrameBytes = 1024 * 1024,
        KeepAlive = new KeepAliveOptions
        {
            Enabled = false
        }
    });

try
{
    await foreach (var msg in client.ReadMessagesAsync(demoCts.Token).ConfigureAwait(false))
    {
        Console.WriteLine($"Client reads: {msg}");
    }
}
finally
{
    await client.DisposeAsync().ConfigureAwait(false);
}

await serverTask.ConfigureAwait(false);

static async Task RunDemoServerAsync(int port, CancellationToken cancellationToken)
{
    using var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();

    using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
    using var stream = client.GetStream();

    for (var i = 1; i <= 10; i++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = $"demo-message-{i}\n";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
    }
}
