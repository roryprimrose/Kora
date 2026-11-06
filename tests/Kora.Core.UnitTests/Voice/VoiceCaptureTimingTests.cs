using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class VoiceCaptureTimingTests
{
    [Theory]
    [InlineData(VoiceCaptureTiming.CommandStartId, "2", 2, 15, 1)]
    [InlineData(VoiceCaptureTiming.CommandStartId, "10", 10, 15, 1)]
    [InlineData(VoiceCaptureTiming.ReplyStartId, "5", 5, 5, 1)]
    [InlineData(VoiceCaptureTiming.ReplyStartId, "60", 5, 60, 1)]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "0.5", 5, 15, 0.5)]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "3", 5, 15, 3)]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, "0.5000001", 5, 15, 0.5000001)]
    public void ExactBoundsAndFractionalSecondsRoundTrip(string id, string value, double command, double reply, double silence)
    {
        var timing = VoiceCaptureTiming.Default.With(id, value);
        timing.CommandStart.Should().Be(TimeSpan.FromSeconds(command));
        timing.ReplyStart.Should().Be(TimeSpan.FromSeconds(reply));
        timing.TrailingSilence.Should().Be(TimeSpan.FromTicks(decimal.ToInt64((decimal)silence * TimeSpan.TicksPerSecond)));
        VoiceCaptureTiming.Parse(timing.Serialize()).Should().Be(timing);
        timing.Describe().Should().Contain(" s");
        timing.With(id, null).Should().Be(VoiceCaptureTiming.Default);
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
    public void MalformedSavedDataFailsExplicitly(string contents) =>
        FluentActions.Invoking(() => VoiceCaptureTiming.Parse(contents)).Should().Throw<InvalidDataException>();
}
