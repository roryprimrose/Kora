using AwesomeAssertions;

using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class PresenceSettingsTests
{
    [Theory]
    [InlineData(PresenceSettings.MinimumTimeoutSeconds)]
    [InlineData(PresenceSettings.DefaultTimeoutSeconds)]
    [InlineData(PresenceSettings.MaximumTimeoutSeconds)]
    public void ValidateTimeoutSeconds_accepts_supported_values(int value)
    {
        var action = () => PresenceSettings.ValidateTimeoutSeconds(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumTimeoutSeconds - 1)]
    [InlineData(PresenceSettings.MaximumTimeoutSeconds + 1)]
    public void ValidateTimeoutSeconds_rejects_unsupported_values(int value)
    {
        var action = () => PresenceSettings.ValidateTimeoutSeconds(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumSizePixels)]
    [InlineData(PresenceSettings.DefaultSizePixels)]
    [InlineData(PresenceSettings.MaximumSizePixels)]
    public void ValidateSizePixels_accepts_supported_values(int value)
    {
        var action = () => PresenceSettings.ValidateSizePixels(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumSizePixels - 1)]
    [InlineData(PresenceSettings.MaximumSizePixels + 1)]
    public void ValidateSizePixels_rejects_unsupported_values(int value)
    {
        var action = () => PresenceSettings.ValidateSizePixels(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumDotSizePercent)]
    [InlineData(PresenceSettings.DefaultDotSizePercent)]
    [InlineData(PresenceSettings.MaximumDotSizePercent)]
    public void ValidateDotSizePercent_accepts_supported_values(int value)
    {
        var action = () => PresenceSettings.ValidateDotSizePercent(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumDotSizePercent - 1)]
    [InlineData(PresenceSettings.MaximumDotSizePercent + 1)]
    public void ValidateDotSizePercent_rejects_unsupported_values(int value)
    {
        var action = () => PresenceSettings.ValidateDotSizePercent(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumMovementSpeedPercent)]
    [InlineData(PresenceSettings.DefaultMovementSpeedPercent)]
    [InlineData(PresenceSettings.MaximumMovementSpeedPercent)]
    public void ValidateMovementSpeedPercent_accepts_supported_values(int value)
    {
        var action = () => PresenceSettings.ValidateMovementSpeedPercent(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumMovementSpeedPercent - 1)]
    [InlineData(PresenceSettings.MaximumMovementSpeedPercent + 1)]
    public void ValidateMovementSpeedPercent_rejects_unsupported_values(int value)
    {
        var action = () => PresenceSettings.ValidateMovementSpeedPercent(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
