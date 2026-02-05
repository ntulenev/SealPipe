using FluentAssertions;

namespace SealPipe.Tcp.Tests;

public sealed class TcpDelimitedClientOptionsTests
{
    [Fact(DisplayName = "Validate succeeds for valid options")]
    [Trait("Category", "Unit")]
    public void ValidateSucceedsForValidOptions()
    {
        // Arrange
        var options = CreateOptions();

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Validate throws when host is missing")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenHostIsMissing()
    {
        // Arrange & Act
        var act = () => CreateOptions(host: "").Validate();

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Validate throws when port is out of range")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenPortIsOutOfRange()
    {
        // Arrange & Act
        var act = () => CreateOptions(port: 0).Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Validate throws when delimiter is missing")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenDelimiterIsMissing()
    {
        // Arrange & Act
        var act = () => CreateOptions(delimiter: "").Validate();

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Validate throws when encoding is missing")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenEncodingIsMissing()
    {
        // Arrange & Act
        var act = () => CreateOptions(encoding: "").Validate();

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Validate throws when max frame bytes is not positive")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenMaxFrameBytesIsNotPositive()
    {
        // Arrange & Act
        var act = () => CreateOptions(maxFrameBytes: 0).Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Validate throws when connect timeout is not positive")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenConnectTimeoutIsNotPositive()
    {
        // Arrange & Act
        var act = () => CreateOptions(connectTimeout: TimeSpan.Zero).Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Validate throws when read timeout is not positive")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenReadTimeoutIsNotPositive()
    {
        // Arrange & Act
        var act = () => CreateOptions(readTimeout: TimeSpan.Zero).Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Validate throws when channel capacity is not positive")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenChannelCapacityIsNotPositive()
    {
        // Arrange & Act
        var act = () => CreateOptions(channelCapacity: 0).Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Validate throws when reconnect initial delay is not positive")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenReconnectInitialDelayIsNotPositive()
    {
        // Arrange
        var options = CreateOptions(reconnect: new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.FromMilliseconds(1)
        });

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Validate throws when reconnect max delay is not positive")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenReconnectMaxDelayIsNotPositive()
    {
        // Arrange
        var options = CreateOptions(reconnect: new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.Zero
        });

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Validate throws when reconnect max attempts is negative")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenReconnectMaxAttemptsIsNegative()
    {
        // Arrange
        var options = CreateOptions(reconnect: new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(1),
            MaxAttempts = -1
        });

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Validate throws when keep-alive time is not positive")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenKeepAliveTimeIsNotPositive()
    {
        // Arrange & Act
        var act = () => CreateOptions(keepAlive: new KeepAliveOptions
        {
            Enabled = true,
            TcpKeepAliveTime = TimeSpan.Zero
        }).Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "Validate throws when keep-alive interval is not positive")]
    [Trait("Category", "Unit")]
    public void ValidateThrowsWhenKeepAliveIntervalIsNotPositive()
    {
        // Arrange & Act
        var act = () => CreateOptions(keepAlive: new KeepAliveOptions
        {
            Enabled = true,
            TcpKeepAliveInterval = TimeSpan.Zero
        }).Validate();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static TcpDelimitedClientOptions CreateOptions(
        string? host = null,
        int? port = null,
        string? delimiter = null,
        string? encoding = null,
        int? maxFrameBytes = null,
        int? channelCapacity = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? readTimeout = null,
        KeepAliveOptions? keepAlive = null,
        ReconnectOptions? reconnect = null)
    {
        return new TcpDelimitedClientOptions
        {
            Host = host ?? "127.0.0.1",
            Port = port ?? 12345,
            Delimiter = delimiter ?? "\n",
            Encoding = encoding ?? "utf-8",
            ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(2),
            ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(2),
            MaxFrameBytes = maxFrameBytes ?? 1024,
            ChannelCapacity = channelCapacity ?? 64,
            Reconnect = reconnect ?? new ReconnectOptions
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
