using AwesomeAssertions;

using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class PresenceSettingsTests
{
    [Fact]
    public void New_presence_settings_have_the_expected_defaults()
    {
        PresenceSettings.DefaultTimeoutSeconds.Should().Be(10);
        ResponseWindowSettings.DefaultTimeoutSeconds.Should().Be(5);
        VisibilityTimeoutSettings.MinimumSeconds.Should().Be(1);
        VisibilityTimeoutSettings.MaximumSeconds.Should().Be(60);
        PresenceSettings.MinimumTimeoutSeconds.Should().Be(1);
        PresenceSettings.MaximumTimeoutSeconds.Should().Be(60);
        ResponseWindowSettings.MinimumTimeoutSeconds.Should().Be(PresenceSettings.MinimumTimeoutSeconds);
        ResponseWindowSettings.MaximumTimeoutSeconds.Should().Be(PresenceSettings.MaximumTimeoutSeconds);
        PresenceSettings.DefaultDotDensityPercent.Should().Be(100);
        PresenceSettings.DefaultSpeechScalingEnabled.Should().BeTrue();
        PresenceSettings.DefaultSpeechScaleAmountPercent.Should().Be(100);
    }

    [Theory]
    [InlineData(VisibilityTimeoutSettings.MinimumSeconds)]
    [InlineData(PresenceSettings.DefaultTimeoutSeconds)]
    [InlineData(ResponseWindowSettings.DefaultTimeoutSeconds)]
    [InlineData(VisibilityTimeoutSettings.MaximumSeconds)]
    public void Shared_visibility_timeout_validation_accepts_supported_values(int seconds)
    {
        var action = () => VisibilityTimeoutSettings.ValidateSeconds(seconds);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(VisibilityTimeoutSettings.MinimumSeconds - 1)]
    [InlineData(VisibilityTimeoutSettings.MaximumSeconds + 1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Shared_visibility_timeout_validation_rejects_unsupported_values(int seconds)
    {
        var action = () => VisibilityTimeoutSettings.ValidateSeconds(seconds);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(nameof(seconds));
    }

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
    [InlineData(ResponseWindowSettings.MinimumTimeoutSeconds)]
    [InlineData(ResponseWindowSettings.DefaultTimeoutSeconds)]
    [InlineData(ResponseWindowSettings.MaximumTimeoutSeconds)]
    public void Response_timeout_validation_accepts_supported_values(int value)
    {
        var action = () => ResponseWindowSettings.ValidateTimeoutSeconds(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(ResponseWindowSettings.MinimumTimeoutSeconds - 1)]
    [InlineData(ResponseWindowSettings.MaximumTimeoutSeconds + 1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Response_timeout_validation_rejects_unsupported_values(int value)
    {
        var action = () => ResponseWindowSettings.ValidateTimeoutSeconds(value);

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
    [InlineData(PresenceSettings.MinimumDotDensityPercent)]
    [InlineData(PresenceSettings.DefaultDotDensityPercent)]
    [InlineData(PresenceSettings.MaximumDotDensityPercent)]
    public void ValidateDotDensityPercent_accepts_supported_values(int value)
    {
        var action = () => PresenceSettings.ValidateDotDensityPercent(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumDotDensityPercent - 1)]
    [InlineData(PresenceSettings.MaximumDotDensityPercent + 1)]
    public void ValidateDotDensityPercent_rejects_unsupported_values(int value)
    {
        var action = () => PresenceSettings.ValidateDotDensityPercent(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(25, 38)]
    [InlineData(26, 39)]
    [InlineData(27, 41)]
    [InlineData(99, 149)]
    [InlineData(100, 150)]
    [InlineData(101, 152)]
    [InlineData(199, 299)]
    [InlineData(200, 300)]
    public void GetParticleCount_scales_and_rounds_midpoints_away_from_zero(
        int densityPercent,
        int expectedCount)
    {
        PresenceSettings.GetParticleCount(densityPercent).Should().Be(expectedCount);
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumDotDensityPercent - 1)]
    [InlineData(PresenceSettings.MaximumDotDensityPercent + 1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void GetParticleCount_rejects_unsupported_density(int densityPercent)
    {
        var action = () => PresenceSettings.GetParticleCount(densityPercent);

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

    [Theory]
    [InlineData(PresenceSettings.MinimumSpeechScaleAmountPercent)]
    [InlineData(PresenceSettings.DefaultSpeechScaleAmountPercent)]
    [InlineData(PresenceSettings.MaximumSpeechScaleAmountPercent)]
    public void ValidateSpeechScaleAmountPercent_accepts_supported_values(int value)
    {
        var action = () => PresenceSettings.ValidateSpeechScaleAmountPercent(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumSpeechScaleAmountPercent - 1)]
    [InlineData(PresenceSettings.MaximumSpeechScaleAmountPercent + 1)]
    public void ValidateSpeechScaleAmountPercent_rejects_unsupported_values(int value)
    {
        var action = () => PresenceSettings.ValidateSpeechScaleAmountPercent(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
