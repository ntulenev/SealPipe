using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SealPipe.Tcp.Exceptions;
using SealPipe.Tcp.Internal;

namespace SealPipe.Tcp;

/// <summary>
/// Reads delimiter-framed messages from a TCP socket.
/// </summary>
public sealed class TcpDelimitedStreamClient : ITcpDelimitedStreamClient, IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TcpDelimitedStreamClient"/> class.
    /// </summary>
    /// <param name="options">The client configuration. Cannot be null.</param>
    /// <param name="logger">The logger instance, if any.</param>
    public TcpDelimitedStreamClient(
        TcpDelimitedClientOptions options,
        ILogger<TcpDelimitedStreamClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = ValidateOptions(options);
        _logger = logger ?? NullLogger<TcpDelimitedStreamClient>.Instance;
        _encoding = Encoding.GetEncoding(_options.Encoding);
        _delimiterBytes = _encoding.GetBytes(_options.Delimiter);
        _connector = new SocketConnector(_options, _logger);
        _decoder = new DelimitedFrameDecoder(_delimiterBytes, _options.MaxFrameBytes);
        Diagnostics = new TcpDelimitedClientDiagnostics();
    }

    /// <summary>
    /// Gets diagnostics counters for this client instance.
    /// </summary>
    public TcpDelimitedClientDiagnostics Diagnostics { get; }

    /// <summary>
    /// Creates a new <see cref="TcpDelimitedStreamClient"/> instance.
    /// </summary>
    /// <param name="options">The client configuration. Cannot be null.</param>
    /// <param name="logger">The logger instance, if any.</param>
    /// <returns>The configured client instance.</returns>
    public static TcpDelimitedStreamClient Create(
        TcpDelimitedClientOptions options,
        ILogger<TcpDelimitedStreamClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new TcpDelimitedStreamClient(options, logger);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCts.Token);
        using var guard = StartReadGuard();

        await foreach (var frame in ReadFramesCoreAsync(linkedCts.Token).ConfigureAwait(false))
        {
            yield return FrameToStringDecoder.Decode(frame, _encoding);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCts.Token);
        using var guard = StartReadGuard();

        await foreach (var frame in ReadFramesCoreAsync(linkedCts.Token).ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _disposeCts.CancelAsync().ConfigureAwait(false);
        _disposeCts.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFramesCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var channel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        var runTask = RunReadLoopAsync(channel.Writer, cancellationToken);

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            await runTask.ConfigureAwait(false);
        }
    }

    private async Task RunReadLoopAsync(
        ChannelWriter<ReadOnlyMemory<byte>> writer,
        CancellationToken cancellationToken)
    {
        var reconnectPolicy = new ReconnectPolicy(_options.Reconnect);

#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Socket? socket = null;
                try
                {
                    socket = await _connector.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    reconnectPolicy.Reset();
                    _logger.LogInformation(
                        "Connected to {Host}:{Port}.",
                        _options.Host,
                        _options.Port);

                    await foreach (var frame in ReadFromSocketAsync(socket, cancellationToken).ConfigureAwait(false))
                    {
                        Diagnostics.AddFrame();
                        await writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                    }

                    if (!_options.Reconnect.Enabled)
                    {
                        writer.TryComplete();
                        return;
                    }

                    Diagnostics.AddReconnectAttempt();
                    _logger.LogWarning("Connection closed. Reconnecting...");
                }
                catch (Exception ex) when (ShouldReconnect(ex, cancellationToken))
                {
                    if (!_options.Reconnect.Enabled)
                    {
                        writer.TryComplete(ex);
                        return;
                    }

                    Diagnostics.AddReconnectAttempt();
                    _logger.LogWarning(ex, "Connection failed. Reconnecting...");
                }
                finally
                {
                    socket?.Dispose();
                }

                if (!reconnectPolicy.TryGetNextDelay(out var delay))
                {
                    writer.TryComplete(new TcpConnectException(
                        $"Reconnect attempts exceeded the configured limit for {_options.Host}:{_options.Port}."));
                    return;
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException oce)
        {
            writer.TryComplete(oce);
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFromSocketAsync(
        Socket socket,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pipe = new Pipe();
        var fillTask = FillPipeAsync(socket, pipe.Writer, cancellationToken);

        await foreach (var frame in _decoder.ReadFramesAsync(pipe.Reader, cancellationToken).ConfigureAwait(false))
        {
            yield return frame;
        }

        await fillTask.ConfigureAwait(false);
    }

    private async Task FillPipeAsync(
        Socket socket,
        PipeWriter writer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var memory = writer.GetMemory(4096);
                var bytesRead = await ReceiveWithTimeoutAsync(socket, memory, cancellationToken)
                    .ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    break;
                }

                Diagnostics.AddBytes(bytesRead);
                writer.Advance(bytesRead);

                var result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<int> ReceiveWithTimeoutAsync(
        Socket socket,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.ReadTimeout);

        try
        {
            return await socket.ReceiveAsync(buffer, SocketFlags.None, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TcpReadTimeoutException(
                $"No data received within {_options.ReadTimeout}.");
        }
    }

    private static bool ShouldReconnect(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return ex switch
        {
            TcpProtocolException => true,
            TcpReadTimeoutException => true,
            TcpConnectException => true,
            SocketException => true,
            IOException => true,
            _ => false
        };
    }

    private static TcpDelimitedClientOptions ValidateOptions(TcpDelimitedClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new ArgumentException("Host is required.", nameof(options));
        }

        if (options.Port <= 0 || options.Port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Port must be between 1 and 65535.");
        }

        if (string.IsNullOrEmpty(options.Delimiter))
        {
            throw new ArgumentException("Delimiter is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Encoding))
        {
            throw new ArgumentException("Encoding is required.", nameof(options));
        }

        if (options.MaxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxFrameBytes must be positive.");
        }

        if (options.ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "ConnectTimeout must be greater than zero.");
        }

        if (options.ReadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "ReadTimeout must be greater than zero.");
        }

        return options;
    }

    private ReadGuard StartReadGuard()
    {
        ThrowIfDisposed();

        if (Interlocked.CompareExchange(ref _activeRead, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "ReadMessagesAsync and ReadFramesAsync are single-consumer and cannot be used concurrently.");
        }

        return new ReadGuard(this);
    }

    private void ReleaseReadGuard()
    {
        _ = Interlocked.Exchange(ref _activeRead, 0);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
    }

    private sealed class ReadGuard : IDisposable
    {
        public ReadGuard(TcpDelimitedStreamClient owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner.ReleaseReadGuard();
        }

        private readonly TcpDelimitedStreamClient _owner;
    }

    private readonly TcpDelimitedClientOptions _options;
    private readonly ILogger _logger;
    private readonly Encoding _encoding;
    private readonly ReadOnlyMemory<byte> _delimiterBytes;
    private readonly SocketConnector _connector;
    private readonly DelimitedFrameDecoder _decoder;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _activeRead;
    private int _disposed;
}
