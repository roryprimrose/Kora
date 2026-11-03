using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class VoiceCaptureTimingCommandTests
{
    [Theory]
    [InlineData("get voice.timing", AppearanceCommandOperation.Get)]
    [InlineData("status voice.command-start-wait", AppearanceCommandOperation.Get)]
    [InlineData("list voice timing settings", AppearanceCommandOperation.List)]
    [InlineData("Kora, set voice.reply-start-wait to 20", AppearanceCommandOperation.Set)]
    [InlineData("Nova set voice.trailing-silence to 0.5", AppearanceCommandOperation.Set)]
    [InlineData("Kora get voice.timing", AppearanceCommandOperation.Get)]
    [InlineData("Kora,get voice.timing", AppearanceCommandOperation.Get)]
    [InlineData("reset voice.command-start-wait", AppearanceCommandOperation.Reset)]
    [InlineData("confirm voice.timing", AppearanceCommandOperation.Set)]
    [InlineData("cancel voice.timing", AppearanceCommandOperation.Set)]
    [InlineData("set voice.timing to 7", AppearanceCommandOperation.Clarify)]
    [InlineData("please voice.timing", AppearanceCommandOperation.Clarify)]
    [InlineData("get voice.timing extra", AppearanceCommandOperation.Clarify)]
    [InlineData("get voice.timing\n", AppearanceCommandOperation.Clarify)]
    public void ExactCommandsDoNotInventUnitsOrAuthority(string text, AppearanceCommandOperation expected)
    {
        VoiceCaptureTimingCommand.Parse(text, text.StartsWith("Nova", StringComparison.Ordinal) ? "Nova" : "Kora")!.Operation.Should().Be(expected);
    }

    [Theory]
    [InlineData("set speech.input-device to system-default")]
    [InlineData("set response.timeout to 20")]
    [InlineData("tell me about voice timing")]
    [InlineData("get voice.timing-extra")]
    [InlineData("help")]
    [InlineData("Kora")]
    [InlineData("Koral get voice.timing")]
    public void UnrelatedRoutesAreNotIntercepted(string text) => VoiceCaptureTimingCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void FixedReviewCommandsAndOversizedInputRemainExact()
    {
        foreach (var phrase in VoiceCaptureTimingCommand.FixedPhrases)
        { VoiceCaptureTimingCommand.Parse(phrase, "Kora")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify); }
        VoiceCaptureTimingCommand.Parse("set " + VoiceCaptureTiming.CommandStartId + " to " + new string('7', 1024), "Kora")!
            .Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }
}
