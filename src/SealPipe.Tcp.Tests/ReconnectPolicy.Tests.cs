using FluentAssertions;

using SealPipe.Tcp.Internal;

namespace SealPipe.Tcp.Tests;

public sealed class ReconnectPolicyTests
{
    [Fact(DisplayName = "TryGetNextDelay returns false when reconnect is disabled")]
    [Trait("Category", "Unit")]
    public void TryGetNextDelayReturnsFalseWhenReconnectIsDisabled()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = false
        };
        var policy = new ReconnectPolicy(options);

        // Act
        var result = policy.TryGetNextDelay(out var delay);

        // Assert
        result.Should().BeFalse();
        delay.Should().Be(TimeSpan.Zero);
    }

    [Fact(DisplayName = "TryGetNextDelay honors max attempts")]
    [Trait("Category", "Unit")]
    public void TryGetNextDelayHonorsMaxAttempts()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            MaxAttempts = 2
        };
        var policy = new ReconnectPolicy(options);

        // Act
        var first = policy.TryGetNextDelay(out _);
        var second = policy.TryGetNextDelay(out _);
        var third = policy.TryGetNextDelay(out var delay);

        // Assert
        first.Should().BeTrue();
        second.Should().BeTrue();
        third.Should().BeFalse();
        delay.Should().Be(TimeSpan.Zero);
    }

    [Fact(DisplayName = "Reset allows attempts after max is reached")]
    [Trait("Category", "Unit")]
    public void ResetAllowsAttemptsAfterMaxIsReached()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            MaxAttempts = 1
        };
        var policy = new ReconnectPolicy(options);

        // Act
        policy.TryGetNextDelay(out _);
        var blocked = policy.TryGetNextDelay(out _);
        policy.Reset();
        var afterReset = policy.TryGetNextDelay(out _);

        // Assert
        blocked.Should().BeFalse();
        afterReset.Should().BeTrue();
    }

    [Fact(DisplayName = "Fixed backoff returns initial delay")]
    [Trait("Category", "Unit")]
    public void FixedBackoffReturnsInitialDelay()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromSeconds(1),
            Backoff = BackoffStrategy.Fixed
        };
        var policy = new ReconnectPolicy(options);

        // Act
        policy.TryGetNextDelay(out var delay);

        // Assert
        delay.Should().Be(options.InitialDelay);
    }

    [Fact(DisplayName = "Exponential backoff scales delay and caps at max")]
    [Trait("Category", "Unit")]
    public void ExponentialBackoffScalesDelayAndCapsAtMax()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(50),
            MaxDelay = TimeSpan.FromMilliseconds(120),
            Backoff = BackoffStrategy.Exponential
        };
        var policy = new ReconnectPolicy(options);

        // Act
        policy.TryGetNextDelay(out var first);
        policy.TryGetNextDelay(out var second);
        policy.TryGetNextDelay(out var third);

        // Assert
        first.Should().Be(TimeSpan.FromMilliseconds(50));
        second.Should().Be(TimeSpan.FromMilliseconds(100));
        third.Should().Be(TimeSpan.FromMilliseconds(120));
    }

    [Fact(DisplayName = "Exponential with jitter returns delay within expected bounds")]
    [Trait("Category", "Unit")]
    public void ExponentialWithJitterReturnsDelayWithinExpectedBounds()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(80),
            MaxDelay = TimeSpan.FromSeconds(1),
            Backoff = BackoffStrategy.ExponentialWithJitter
        };
        var policy = new ReconnectPolicy(options);

        // Act
        policy.TryGetNextDelay(out var delay);

        // Assert
        delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        delay.Should().BeLessThanOrEqualTo(options.InitialDelay);
    }

    [Fact(DisplayName = "Initial delay of zero returns zero delay")]
    [Trait("Category", "Unit")]
    public void InitialDelayOfZeroReturnsZeroDelay()
    {
        // Arrange
        var options = new ReconnectOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.FromSeconds(1),
            Backoff = BackoffStrategy.Exponential
        };
        var policy = new ReconnectPolicy(options);

        // Act
        policy.TryGetNextDelay(out var delay);

        // Assert
        delay.Should().Be(TimeSpan.Zero);
    }
}
