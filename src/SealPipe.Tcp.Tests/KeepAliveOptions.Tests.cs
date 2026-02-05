using System.ComponentModel.DataAnnotations;

using FluentAssertions;

namespace SealPipe.Tcp.Tests;

public sealed class KeepAliveOptionsTests
{
    [Fact(DisplayName = "DataAnnotations validate valid keep-alive options")]
    [Trait("Category", "Unit")]
    public void DataAnnotationsValidateValidKeepAliveOptions()
    {
        // Arrange
        var options = new KeepAliveOptions
        {
            Enabled = true,
            TcpKeepAliveTime = TimeSpan.FromSeconds(1),
            TcpKeepAliveInterval = TimeSpan.FromSeconds(1)
        };

        // Act
        var isValid = TryValidate(options, out var results);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact(DisplayName = "DataAnnotations reject non-positive keep-alive time")]
    [Trait("Category", "Unit")]
    public void DataAnnotationsRejectNonPositiveKeepAliveTime()
    {
        // Arrange
        var options = new KeepAliveOptions
        {
            Enabled = true,
            TcpKeepAliveTime = TimeSpan.Zero,
            TcpKeepAliveInterval = TimeSpan.FromSeconds(1)
        };

        // Act
        var isValid = TryValidate(options, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().ContainSingle(result => result.MemberNames.Contains(nameof(KeepAliveOptions.TcpKeepAliveTime)));
    }

    [Fact(DisplayName = "DataAnnotations reject non-positive keep-alive interval")]
    [Trait("Category", "Unit")]
    public void DataAnnotationsRejectNonPositiveKeepAliveInterval()
    {
        // Arrange
        var options = new KeepAliveOptions
        {
            Enabled = true,
            TcpKeepAliveTime = TimeSpan.FromSeconds(1),
            TcpKeepAliveInterval = TimeSpan.Zero
        };

        // Act
        var isValid = TryValidate(options, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().ContainSingle(result => result.MemberNames.Contains(nameof(KeepAliveOptions.TcpKeepAliveInterval)));
    }

    private static bool TryValidate(KeepAliveOptions options, out List<ValidationResult> results)
    {
        results = new List<ValidationResult>();
        var context = new ValidationContext(options);
        return Validator.TryValidateObject(options, context, results, validateAllProperties: true);
    }
}
