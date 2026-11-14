using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class VoiceCaptureTimingTests
{
    [Theory]
    [InlineData(VoiceCaptureTiming.CommandStartId, "2", 2, 15, 10000000L)]
    [InlineData(VoiceCaptureTiming.CommandStartId, "10", 10, 15, 10000000L)]
    [InlineData(VoiceCaptureTiming.ReplyStartId, "5", 5, 5, 10000000L)]
    [InlineData(VoiceCaptureTiming.ReplyStartId, "60", 5, 60, 10000000L)]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "0.5", 5, 15, 5000000L)]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "3", 5, 15, 30000000L)]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "0.5000001", 5, 15, 5000001L)]
    public void ExactBoundsAndFractionalSecondsRoundTrip(string id, string value, double command, double reply, long silenceTicks)
    {
        var timing = VoiceCaptureTiming.Default.With(id, value);
        timing.CommandStart.Should().Be(TimeSpan.FromSeconds(command));
        timing.ReplyStart.Should().Be(TimeSpan.FromSeconds(reply));
        timing.TrailingSilence.Should().Be(TimeSpan.FromTicks(silenceTicks));
        VoiceCaptureTiming.Parse(timing.Serialize()).Should().Be(timing);
        timing.Describe().Should().Contain(" s");
        timing.With(id, null).Should().Be(VoiceCaptureTiming.Default);
    }

    [Theory]
    [InlineData("10", 100000000L)]
    [InlineData("60", 600000000L)]
    [InlineData("40.0000001", 400000001L)]
    public void MaximumCaptureUsesExactTicksAndResetPreservesEveryCompanion(string seconds, long ticks)
    {
        var companions = new VoiceCaptureTiming(7, 20, 2);
        var timing = companions.With(VoiceCaptureTiming.MaximumCaptureId, seconds);
        timing.MaximumCapture.Should().Be(TimeSpan.FromTicks(ticks));
        timing.With(VoiceCaptureTiming.MaximumCaptureId, null).Should().Be(companions);
        VoiceCaptureTiming.Parse(timing.Serialize()).Should().Be(timing);
        timing.Describe().Should().Contain($"maximum capture {seconds} s");
        VoiceInputLimits.MaximumCommandDuration.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void LegacyThreeFieldPreferenceRecoversOnlyItsExistingSixtySecondBehavior()
    {
        VoiceCaptureTiming.Parse("1\n7\n20\n2").Should().Be(new VoiceCaptureTiming(7, 20, 2, 60));
        VoiceCaptureTiming.Default.Serialize().Should().Be("2\n5\n15\n1\n60");
    }

    [Theory]
    [InlineData(VoiceCaptureTiming.CommandStartId, "1.9999999")]
    [InlineData(VoiceCaptureTiming.CommandStartId, "10.0000001")]
    [InlineData(VoiceCaptureTiming.ReplyStartId, "4.9999999")]
    [InlineData(VoiceCaptureTiming.ReplyStartId, "60.0000001")]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "0.4999999")]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "3.0000001")]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "0.50000001")]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "0.50000000000000000000000000001")]
    [InlineData(VoiceCaptureTiming.CommandStartId, "NaN")]
    [InlineData(VoiceCaptureTiming.CommandStartId, "-5")]
    [InlineData(VoiceCaptureTiming.CommandStartId, "5 s")]
    [InlineData(VoiceCaptureTiming.CommandStartId, "5,5")]
    [InlineData(VoiceCaptureTiming.CommandStartId, " 5")]
    [InlineData(VoiceCaptureTiming.MaximumCaptureId, "9.9999999")]
    [InlineData(VoiceCaptureTiming.MaximumCaptureId, "60.0000001")]
    [InlineData(VoiceCaptureTiming.MaximumCaptureId, "40.00000001")]
    [InlineData(VoiceCaptureTiming.MaximumCaptureId, "40.00000000000000000000000000001")]
    [InlineData("unknown", "5")]
    [InlineData("unknown", null)]
    public void InvalidTimingCannotBeClampedOrReinterpreted(string id, string? value) =>
        FluentActions.Invoking(() => VoiceCaptureTiming.Default.With(id, value)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("")]
    [InlineData("2\n5\n15\n1")]
    [InlineData("1\n5\n15\n1\n")]
    [InlineData("1\nNaN\n15\n1")]
    [InlineData("1\n11\n15\n1")]
    [InlineData("1\n5\n4\n1")]
    [InlineData("1\n5\n15\n0.1")]
    [InlineData("1\n5\n15\n1\n40")]
    [InlineData("2\n5\n15\n1\n")]
    [InlineData("2\n5\n15\n1\n9")]
    [InlineData("2\n5\n15\n1\n61")]
    [InlineData("2\n5\n15\n1\nNaN")]
    [InlineData("2\n5\n15\n1\n40.00000001")]
    [InlineData("2\n5\n15\n1\n40\n")]
    public void MalformedSavedDataFailsExplicitly(string contents) =>
        FluentActions.Invoking(() => VoiceCaptureTiming.Parse(contents)).Should().Throw<InvalidDataException>();
}
