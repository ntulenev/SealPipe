using FluentAssertions;

namespace SealPipe.Tcp.Tests;

public sealed class TcpDelimitedClientDiagnosticsTests
{
    [Fact(DisplayName = "Diagnostics start with zero counts and no timestamp")]
    [Trait("Category", "Unit")]
    public void DiagnosticsStartWithZeroCountsAndNoTimestamp()
    {
        // Arrange
        var diagnostics = new TcpDelimitedClientDiagnostics();

        // Act & Assert
        diagnostics.ReconnectAttempts.Should().Be(0);
        diagnostics.BytesReceived.Should().Be(0);
        diagnostics.FramesReceived.Should().Be(0);
        diagnostics.FramesDropped.Should().Be(0);
        diagnostics.LastMessageTimestamp.Should().BeNull();
    }

    [Fact(DisplayName = "AddBytes increments bytes received")]
    [Trait("Category", "Unit")]
    public void AddBytesIncrementsBytesReceived()
    {
        // Arrange
        var diagnostics = new TcpDelimitedClientDiagnostics();

        // Act
        diagnostics.AddBytes(5);
        diagnostics.AddBytes(3);

        // Assert
        diagnostics.BytesReceived.Should().Be(8);
    }

    [Fact(DisplayName = "AddFrame increments frames received and updates timestamp")]
    [Trait("Category", "Unit")]
    public void AddFrameIncrementsFramesReceivedAndUpdatesTimestamp()
    {
        // Arrange
        var diagnostics = new TcpDelimitedClientDiagnostics();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Act
        diagnostics.AddFrame();
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        // Assert
        diagnostics.FramesReceived.Should().Be(1);
        diagnostics.LastMessageTimestamp.Should().NotBeNull();
        diagnostics.LastMessageTimestamp!.Value.Should().BeOnOrAfter(before);
        diagnostics.LastMessageTimestamp!.Value.Should().BeOnOrBefore(after);
    }

    [Fact(DisplayName = "AddDroppedFrame increments frames dropped")]
    [Trait("Category", "Unit")]
    public void AddDroppedFrameIncrementsFramesDropped()
    {
        // Arrange
        var diagnostics = new TcpDelimitedClientDiagnostics();

        // Act
        diagnostics.AddDroppedFrame();
        diagnostics.AddDroppedFrame();

        // Assert
        diagnostics.FramesDropped.Should().Be(2);
    }

    [Fact(DisplayName = "AddReconnectAttempt increments reconnect attempts")]
    [Trait("Category", "Unit")]
    public void AddReconnectAttemptIncrementsReconnectAttempts()
    {
        // Arrange
        var diagnostics = new TcpDelimitedClientDiagnostics();

        // Act
        diagnostics.AddReconnectAttempt();
        diagnostics.AddReconnectAttempt();
        diagnostics.AddReconnectAttempt();

        // Assert
        diagnostics.ReconnectAttempts.Should().Be(3);
    }
}
