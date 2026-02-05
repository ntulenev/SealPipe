using System.Text;

using FluentAssertions;

using SealPipe.Tcp.Internal;

namespace SealPipe.Tcp.Tests;

public sealed class FrameToStringDecoderTests
{
    [Fact(DisplayName = "Decode returns expected string for UTF-8")]
    [Trait("Category", "Unit")]
    public void DecodeReturnsExpectedStringForUtf8()
    {
        // Arrange
        var encoding = Encoding.UTF8;
        var payload = encoding.GetBytes("Hello, world!");

        // Act
        var result = FrameToStringDecoder.Decode(payload, encoding);

        // Assert
        result.Should().Be("Hello, world!");
    }

    [Fact(DisplayName = "Decode returns empty string for empty payload")]
    [Trait("Category", "Unit")]
    public void DecodeReturnsEmptyStringForEmptyPayload()
    {
        // Arrange
        var encoding = Encoding.UTF8;
        var payload = ReadOnlyMemory<byte>.Empty;

        // Act
        var result = FrameToStringDecoder.Decode(payload, encoding);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact(DisplayName = "Decode throws when encoding is null")]
    [Trait("Category", "Unit")]
    public void DecodeThrowsWhenEncodingIsNull()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("data");

        // Act
        var act = () => FrameToStringDecoder.Decode(payload, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
