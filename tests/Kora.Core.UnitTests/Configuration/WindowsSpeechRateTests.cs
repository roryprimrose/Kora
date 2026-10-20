using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Configuration;

public sealed class WindowsSpeechRateTests
{
    [Theory]
    [InlineData(0, AudioPreferenceAdjustment.Increase, 1)]
    [InlineData(0, AudioPreferenceAdjustment.Decrease, -1)]
    [InlineData(9, AudioPreferenceAdjustment.Increase, 10)]
    [InlineData(-9, AudioPreferenceAdjustment.Decrease, -10)]
    [InlineData(10, AudioPreferenceAdjustment.Increase, 10)]
    [InlineData(-10, AudioPreferenceAdjustment.Decrease, -10)]
    [InlineData(-10, AudioPreferenceAdjustment.Increase, -9)]
    [InlineData(10, AudioPreferenceAdjustment.Decrease, 9)]
    public void AdjustUsesBoundedNativeUnitSteps(int current, AudioPreferenceAdjustment adjustment, int expected) =>
        new WindowsSpeechRate(current).Adjust(adjustment).Value.Should().Be(expected);

    [Fact]
    public void AdjustRejectsUnknownDirection() =>
        FluentActions.Invoking(() => WindowsSpeechRate.Default.Adjust((AudioPreferenceAdjustment)999))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData(-10)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void Canonical_native_units_round_trip_and_normal_is_zero(int value)
    {
        WindowsSpeechRate.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Should().Be(new WindowsSpeechRate(value));
        new WindowsSpeechRate(value).Value.Should().Be(value);
        WindowsSpeechRate.Default.Should().Be(default(WindowsSpeechRate));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("01")]
    [InlineData("-01")]
    [InlineData("+1")]
    [InlineData("-0")]
    [InlineData("1.0")]
    [InlineData("1%")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("-11")]
    [InlineData("11")]
    [InlineData("2147483648")]
    public void Noncanonical_or_out_of_range_values_are_not_reinterpreted(string value) =>
        FluentActions.Invoking(() => WindowsSpeechRate.Parse(value)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Provider_support_is_explicit_and_unknown_defaults_to_unsupported()
    {
        var provider = new SpeechProvider("unknown", "Other", "Unknown", true, false, null, null);
        provider.RateSupport.Should().Be(SpeechRateSupport.Unsupported);
        (provider with { RateSupport = SpeechRateSupport.WindowsNative }).RateSupport.Should().Be(SpeechRateSupport.WindowsNative);
        new WindowsSpeechRateUnavailableException("held").Message.Should().Be("held");
    }
}
