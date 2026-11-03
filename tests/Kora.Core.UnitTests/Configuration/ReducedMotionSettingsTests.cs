using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Configuration;

public sealed class ReducedMotionSettingsTests
{
    [Theory]
    [InlineData(false, MotionPreference.AnimationsEnabled, true)]
    [InlineData(true, MotionPreference.AnimationsEnabled, false)]
    [InlineData(false, MotionPreference.ReducedMotion, false)]
    [InlineData(true, MotionPreference.ReducedMotion, false)]
    public void ExplicitReductionNeverOverridesSystemAccessibility(bool reduce, MotionPreference system, bool enabled)
    {
        ReducedMotionSettings.DefaultEnabled.Should().BeFalse();
        ReducedMotionSettings.IsMotionEnabled(reduce, system).Should().Be(enabled);
        ReducedMotionSettings.ParseSaved(ReducedMotionSettings.FormatSaved(reduce)).Should().Be(reduce);
    }

    [Theory]
    [InlineData("")]
    [InlineData("system")]
    [InlineData("1")]
    [InlineData("false\ntrue")]
    public void InvalidSavedValuesAreNotDefaults(string value)
    {
        FluentActions.Invoking(() => ReducedMotionSettings.ParseSaved(value)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownSystemStateIsNotPermissionToAnimate(bool reduce)
    {
        FluentActions.Invoking(() => ReducedMotionSettings.IsMotionEnabled(reduce, (MotionPreference)999))
            .Should().Throw<InvalidOperationException>();
    }
}
