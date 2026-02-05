using FluentAssertions;

using SealPipe.Tcp.Exceptions;

namespace SealPipe.Tcp.Tests;

public sealed class TcpExceptionsTests
{
    [Fact(DisplayName = "TcpConnectException can be created with message")]
    [Trait("Category", "Unit")]
    public void TcpConnectExceptionCanBeCreatedWithMessage()
    {
        // Arrange & Act
        var exception = new TcpConnectException("connect failed");

        // Assert
        exception.Message.Should().Be("connect failed");
        exception.InnerException.Should().BeNull();
    }

    [Fact(DisplayName = "TcpConnectException can be created with message and inner exception")]
    [Trait("Category", "Unit")]
    public void TcpConnectExceptionCanBeCreatedWithMessageAndInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("inner");

        // Act
        var exception = new TcpConnectException("connect failed", inner);

        // Assert
        exception.Message.Should().Be("connect failed");
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact(DisplayName = "TcpProtocolException can be created with message")]
    [Trait("Category", "Unit")]
    public void TcpProtocolExceptionCanBeCreatedWithMessage()
    {
        // Arrange & Act
        var exception = new TcpProtocolException("protocol failed");

        // Assert
        exception.Message.Should().Be("protocol failed");
        exception.InnerException.Should().BeNull();
    }

    [Fact(DisplayName = "TcpProtocolException can be created with message and inner exception")]
    [Trait("Category", "Unit")]
    public void TcpProtocolExceptionCanBeCreatedWithMessageAndInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("inner");

        // Act
        var exception = new TcpProtocolException("protocol failed", inner);

        // Assert
        exception.Message.Should().Be("protocol failed");
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact(DisplayName = "TcpReadTimeoutException can be created with message")]
    [Trait("Category", "Unit")]
    public void TcpReadTimeoutExceptionCanBeCreatedWithMessage()
    {
        // Arrange & Act
        var exception = new TcpReadTimeoutException("read timeout");

        // Assert
        exception.Message.Should().Be("read timeout");
        exception.InnerException.Should().BeNull();
    }

    [Fact(DisplayName = "TcpReadTimeoutException can be created with message and inner exception")]
    [Trait("Category", "Unit")]
    public void TcpReadTimeoutExceptionCanBeCreatedWithMessageAndInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("inner");

        // Act
        var exception = new TcpReadTimeoutException("read timeout", inner);

        // Assert
        exception.Message.Should().Be("read timeout");
        exception.InnerException.Should().BeSameAs(inner);
    }
}
