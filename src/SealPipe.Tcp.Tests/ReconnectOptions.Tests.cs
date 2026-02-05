using System.ComponentModel.DataAnnotations;

using FluentAssertions;

namespace SealPipe.Tcp.Tests;

public sealed class ReconnectOptionsTests
{
    [Fact(DisplayName = "DataAnnotations validate valid reconnect options")]
    [Trait("Category", "Unit")]
    public void DataAnnotationsValidateValidReconnectOptions()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(1),
            MaxAttempts = 0
        };

        // Act
        var isValid = TryValidate(options, out var results);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact(DisplayName = "DataAnnotations reject non-positive initial delay")]
    [Trait("Category", "Unit")]
    public void DataAnnotationsRejectNonPositiveInitialDelay()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.FromMilliseconds(1),
            MaxAttempts = 0
        };

        // Act
        var isValid = TryValidate(options, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().ContainSingle(result => result.MemberNames.Contains(nameof(ReconnectOptions.InitialDelay)));
    }

    [Fact(DisplayName = "DataAnnotations reject non-positive max delay")]
    [Trait("Category", "Unit")]
    public void DataAnnotationsRejectNonPositiveMaxDelay()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.Zero,
            MaxAttempts = 0
        };

        // Act
        var isValid = TryValidate(options, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().ContainSingle(result => result.MemberNames.Contains(nameof(ReconnectOptions.MaxDelay)));
    }

    [Fact(DisplayName = "DataAnnotations reject negative max attempts")]
    [Trait("Category", "Unit")]
    public void DataAnnotationsRejectNegativeMaxAttempts()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(1),
            MaxAttempts = -1
        };

        // Act
        var isValid = TryValidate(options, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().ContainSingle(result => result.MemberNames.Contains(nameof(ReconnectOptions.MaxAttempts)));
    }

    private static bool TryValidate(ReconnectOptions options, out List<ValidationResult> results)
    {
        results = new List<ValidationResult>();
        var context = new ValidationContext(options);
        return Validator.TryValidateObject(options, context, results, validateAllProperties: true);
    }
}
