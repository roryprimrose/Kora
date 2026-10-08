using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Configuration;

public sealed class WindowsSpeechRateTests
{
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
