using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Buffers;

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

        options.Validate();
        _options = options;
        _logger = logger ?? NullLogger<TcpDelimitedStreamClient>.Instance;
        _encoding = Encoding.GetEncoding(_options.Encoding);
        _delimiterBytes = _encoding.GetBytes(_options.Delimiter);
        Diagnostics = new TcpDelimitedClientDiagnostics();
        _connector = new SocketConnector(_options, _logger);
        _decoder = new DelimitedFrameDecoder(
            _delimiterBytes,
            _options.MaxFrameBytes);
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
    /// <exception cref="OperationCanceledException">
    /// Thrown when the provided <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    public async IAsyncEnumerable<string> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCts.Token);
        using var guard = StartReadGuard();

        await foreach (var frame in ReadFramesCoreAsync(linkedCts.Token).ConfigureAwait(false))
        {
            try
            {
                yield return FrameToStringDecoder.Decode(frame.Memory, _encoding);
            }
            finally
            {
                frame.Dispose();
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">
    /// Thrown when the provided <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    public async IAsyncEnumerable<IMemoryOwner<byte>> ReadFramesAsync(
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

    private async IAsyncEnumerable<IMemoryOwner<byte>> ReadFramesCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var channel = Channel.CreateBounded<IMemoryOwner<byte>>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = MapFullMode(_options.ChannelOverflowStrategy)
        });

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runTask = RunReadLoopAsync(channel.Writer, runCts.Token);

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            if (!runTask.IsCompleted)
            {
                await runCts.CancelAsync().ConfigureAwait(false);
            }

            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            while (channel.Reader.TryRead(out var frame))
            {
                frame.Dispose();
            }
        }
    }

    private async Task RunReadLoopAsync(
        ChannelWriter<IMemoryOwner<byte>> writer,
        CancellationToken cancellationToken)
    {
        var reconnectPolicy = new ReconnectPolicy(_options.Reconnect);
        var isReconnectAttempt = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Socket? socket = null;
                try
                {
                    if (isReconnectAttempt)
                    {
                        Diagnostics.AddReconnectAttempt();
                        isReconnectAttempt = false;
                    }

                    socket = await _connector.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    reconnectPolicy.Reset();
                    LogConnected(_logger, _options.Host, _options.Port, null);

                    await foreach (var frame in ReadFromSocketAsync(socket, cancellationToken).ConfigureAwait(false))
                    {
                        if (await WriteFrameAsync(writer, frame, cancellationToken).ConfigureAwait(false))
                        {
                            Diagnostics.AddFrame();
                        }
                    }

                    if (!_options.Reconnect.Enabled)
                    {
                        writer.TryComplete();
                        return;
                    }

                    LogConnectionClosed(_logger, null);
                }
                catch (Exception ex) when (ShouldReconnect(ex, cancellationToken))
                {
                    if (!_options.Reconnect.Enabled)
                    {
                        writer.TryComplete(ex);
                        return;
                    }

                    LogConnectionFailed(_logger, ex);
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

                isReconnectAttempt = true;
            }
        }
        catch (OperationCanceledException oce)
        {
            writer.TryComplete(oce);
        }
        catch (Exception ex) when (
            ex is TcpProtocolException ||
            ex is TcpReadTimeoutException ||
            ex is TcpConnectException ||
            ex is SocketException ||
            ex is IOException ||
            ex is ChannelClosedException ||
            ex is ObjectDisposedException)
        {
            writer.TryComplete(ex);
        }
    }

    private async IAsyncEnumerable<IMemoryOwner<byte>> ReadFromSocketAsync(
        Socket socket,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pipe = new Pipe();
        using var fillCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var fillTask = FillPipeAsync(socket, pipe.Writer, fillCts.Token);

        try
        {
            await foreach (var frame in _decoder.ReadFramesAsync(pipe.Reader, cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            if (!fillTask.IsCompleted)
            {
                await fillCts.CancelAsync().ConfigureAwait(false);
            }

            try
            {
                await fillTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
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

                var memory = writer.GetMemory(_options.ReceiveBufferSize);
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
            TcpReadTimeoutException => true,
            TcpConnectException => true,
            SocketException => true,
            IOException => true,
            _ => false
        };
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

    private async ValueTask<bool> WriteFrameAsync(
        ChannelWriter<IMemoryOwner<byte>> writer,
        IMemoryOwner<byte> frame,
        CancellationToken cancellationToken)
    {
        if (_options.ChannelOverflowStrategy == ChannelOverflowStrategy.Block)
        {
            try
            {
                await writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }

        if (writer.TryWrite(frame))
        {
            return true;
        }

        Diagnostics.AddDroppedFrame();
        frame.Dispose();
        return false;
    }

    private static BoundedChannelFullMode MapFullMode(ChannelOverflowStrategy strategy)
    {
        return strategy == ChannelOverflowStrategy.Block
            ? BoundedChannelFullMode.Wait
            : BoundedChannelFullMode.DropWrite;
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

    private static readonly Action<ILogger, string, int, Exception?> LogConnected =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(1, "Connected"),
            "Connected to {Host}:{Port}.");

    private static readonly Action<ILogger, Exception?> LogConnectionClosed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, "ConnectionClosed"),
            "Connection closed. Reconnecting...");

    private static readonly Action<ILogger, Exception?> LogConnectionFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, "ConnectionFailed"),
            "Connection failed. Reconnecting...");
}
