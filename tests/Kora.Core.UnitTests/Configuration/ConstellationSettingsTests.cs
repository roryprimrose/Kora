using AwesomeAssertions;

using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class ConstellationSettingsTests
{
    [Theory]
    [InlineData(ConstellationSettings.MinimumSizePixels)]
    [InlineData(ConstellationSettings.DefaultSizePixels)]
    [InlineData(ConstellationSettings.MaximumSizePixels)]
    public void ValidateSizePixels_accepts_supported_values(int value)
    {
        var action = () => ConstellationSettings.ValidateSizePixels(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(ConstellationSettings.MinimumSizePixels - 1)]
    [InlineData(ConstellationSettings.MaximumSizePixels + 1)]
    public void ValidateSizePixels_rejects_unsupported_values(int value)
    {
        var action = () => ConstellationSettings.ValidateSizePixels(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(ConstellationSettings.MinimumDotSizePercent)]
    [InlineData(ConstellationSettings.DefaultDotSizePercent)]
    [InlineData(ConstellationSettings.MaximumDotSizePercent)]
    public void ValidateDotSizePercent_accepts_supported_values(int value)
    {
        var action = () => ConstellationSettings.ValidateDotSizePercent(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(ConstellationSettings.MinimumDotSizePercent - 1)]
    [InlineData(ConstellationSettings.MaximumDotSizePercent + 1)]
    public void ValidateDotSizePercent_rejects_unsupported_values(int value)
    {
        var action = () => ConstellationSettings.ValidateDotSizePercent(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(ConstellationSettings.MinimumMovementSpeedPercent)]
    [InlineData(ConstellationSettings.DefaultMovementSpeedPercent)]
    [InlineData(ConstellationSettings.MaximumMovementSpeedPercent)]
    public void ValidateMovementSpeedPercent_accepts_supported_values(int value)
    {
        var action = () => ConstellationSettings.ValidateMovementSpeedPercent(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(ConstellationSettings.MinimumMovementSpeedPercent - 1)]
    [InlineData(ConstellationSettings.MaximumMovementSpeedPercent + 1)]
    public void ValidateMovementSpeedPercent_rejects_unsupported_values(int value)
    {
        var action = () => ConstellationSettings.ValidateMovementSpeedPercent(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
