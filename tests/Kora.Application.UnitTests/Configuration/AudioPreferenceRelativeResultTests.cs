using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class AudioPreferenceRelativeResultTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RelativeResultDisclosesPreviousAndActualCurrentScalarEvenWhenUnavailable(bool rate, bool available)
    {
        var json = rate
            ? WindowsSpeechRateState.Serialize(new(available ? new WindowsSpeechRate(2) : null, null, SpeechProviderIds.Windows,
                SpeechRateSupport.WindowsNative, 1, "unavailable", 2, null), 3, "denied", new(1))
            : PlaybackVolumeState.Serialize(new(available ? new PlaybackVolume(100) : null, null, "unavailable", 2, null),
                3, "denied", new(95));
        using var result = JsonDocument.Parse(json);
        var adjustment = result.RootElement.GetProperty("adjustment");
        adjustment.GetProperty(rate ? "previousNativeRate" : "previousPercent").GetInt32().Should().Be(rate ? 1 : 95);
        var actual = adjustment.GetProperty(rate ? "currentNativeRate" : "currentPercent");
        if (available) { actual.GetInt32().Should().Be(rate ? 2 : 100); }
        else { actual.ValueKind.Should().Be(JsonValueKind.Null); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingExactResultEnvelopeDoesNotAcquireRelativeFields(bool rate)
    {
        var json = rate
            ? WindowsSpeechRateState.Serialize(new(new(2), new(2), SpeechProviderIds.Windows,
                SpeechRateSupport.WindowsNative, 1, "saved", 2, null), 3, "saved")
            : PlaybackVolumeState.Serialize(new(new(100), new(100), "saved", 2, null), 3, "saved");
        using var result = JsonDocument.Parse(json);
        result.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("state", "callRevision", "outcome");
    }
}
