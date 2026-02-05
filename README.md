# SealPipe
Production-grade .NET TCP streaming library built on top of `System.IO.Pipelines`.

## Usage
```csharp
using SealPipe.Tcp;

const int demoPort = 19000;
using var demoCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

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
        ChannelOverflowStrategy = ChannelOverflowStrategy.Drop,
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
```

Read raw frames instead of strings:
```csharp
await foreach (var frame in client.ReadFramesAsync(demoCts.Token))
{
    using (frame)
    {
        // frame.Memory is ReadOnlyMemory<byte>
    }
}
```

## Configuration
| Option | Type | Default | Description |
| --- | --- | --- | --- |
| `Host` | `string` | (required) | Remote host name or IP. |
| `Port` | `int` | (required) | Remote port (1-65535). |
| `Delimiter` | `string` | `"\n"` | Frame delimiter. Can be multi-byte (e.g., `"\r\n"`). |
| `Encoding` | `string` | `"utf-8"` | Encoding name used for decoding messages. |
| `ConnectTimeout` | `TimeSpan` | 5s | Timeout for establishing a TCP connection. |
| `ReadTimeout` | `TimeSpan` | 30s | Timeout for receiving any data. |
| `Reconnect.Enabled` | `bool` | true | Enables automatic reconnect. |
| `Reconnect.InitialDelay` | `TimeSpan` | 250ms | Initial delay before reconnect. |
| `Reconnect.MaxDelay` | `TimeSpan` | 10s | Maximum reconnect delay. |
| `Reconnect.Backoff` | `BackoffStrategy` | `ExponentialWithJitter` | Backoff algorithm. |
| `Reconnect.MaxAttempts` | `int` | 0 | Max attempts; 0 means infinite. |
| `MaxFrameBytes` | `int` | 1,048,576 | Max size for a single frame. |
| `ChannelOverflowStrategy` | `ChannelOverflowStrategy` | `Drop` | Behavior when buffered frame channels are full. (`Block` is temporary not supported.) |
| `KeepAlive.Enabled` | `bool` | false | Enables TCP keep-alives. |
| `KeepAlive.TcpKeepAliveTime` | `TimeSpan` | 30s | Idle time before keep-alive probes. |
| `KeepAlive.TcpKeepAliveInterval` | `TimeSpan` | 10s | Interval between keep-alive probes. |

## Diagnostics
Each client exposes counters:
- `ReconnectAttempts`
- `BytesReceived`
- `FramesReceived`
- `LastMessageTimestamp`

Access them via `TcpDelimitedStreamClient.Diagnostics`.

## Assumptions
- If a connection closes while a frame is incomplete (no delimiter seen), a protocol error is raised and the client reconnects if enabled.
- `ReadMessagesAsync` and `ReadFramesAsync` are single-consumer APIs; do not call them concurrently on the same client instance.

## Projects
- `SealPipe.Tcp` - library
- `SealPipe.Tcp.Tests` - xUnit + FluentAssertions tests
- `SealPipe.Tcp.Samples` - minimal console sample
