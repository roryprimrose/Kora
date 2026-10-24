using System.Text;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class NaturalSpeechCaptionCommandTests
{
    [Theory]
    [InlineData("turn on speech captions", "set display.speech-text to CurrentUtterance")]
    [InlineData("show speech captions", "set display.speech-text to CurrentUtterance")]
    [InlineData("show speech text", "set display.speech-text to CurrentUtterance")]
    [InlineData("show the words you're saying", "set display.speech-text to CurrentUtterance")]
    [InlineData("turn off speech captions", "set display.speech-text to Off")]
    [InlineData("hide speech captions", "set display.speech-text to Off")]
    [InlineData("hide speech text", "set display.speech-text to Off")]
    [InlineData("list speech caption settings", "list speech text settings")]
    [InlineData("inspect speech caption settings", "get display.speech-text")]
    [InlineData("status speech captions", "status display.speech-text")]
    [InlineData("reset speech captions", "reset display.speech-text")]
    [InlineData("get speech caption placement", "get display.speech-text-placement")]
    [InlineData("status speech caption placement", "status display.speech-text-placement")]
    [InlineData("reset speech caption placement", "reset display.speech-text-placement")]
    [InlineData("get speech caption dismissal delay", "get display.speech-text-dismissal-delay")]
    [InlineData("status speech caption dismissal delay", "status display.speech-text-dismissal-delay")]
    [InlineData("reset speech caption dismissal delay", "reset display.speech-text-dismissal-delay")]
    [InlineData("put speech captions in the bottom-right", "set display.speech-text-placement to BottomRight")]
    [InlineData("put speech captions in the bottom-left", "set display.speech-text-placement to BottomLeft")]
    [InlineData("put speech captions in the top-right", "set display.speech-text-placement to TopRight")]
    [InlineData("put speech captions in the top-left", "set display.speech-text-placement to TopLeft")]
    [InlineData("pin current speech caption", "set display.speech-text-pin to true")]
    [InlineData("unpin current speech caption", "set display.speech-text-pin to false")]
    public void ParseUsesExistingExactContractAndDiscovery(string natural, string canonical)
    {
        var expected = SpeechTextCommand.Parse(canonical, "Nova");
        foreach (var input in new[] { natural, "Nova, " + natural, "nova " + natural.ToUpperInvariant(), "  Nova, " + natural + "  " })
        {
            SpeechTextCommand.Parse(input, "Nova").Should().Be(expected);
        }
        SpeechTextCommand.FixedPhrases.Should().Contain(natural);
        SpeechTextCommand.Parse("Kora, " + natural, "Nova").Should().BeNull();
    }

    [Fact]
    public void ParseUsesEveryAuthoritativeDelayChoiceWithoutAnotherNumericParser()
    {
        foreach (var seconds in Enumerable.Range(SpeechCaptionOptions.MinimumDelaySeconds,
            SpeechCaptionOptions.MaximumDelaySeconds - SpeechCaptionOptions.MinimumDelaySeconds + 1))
        {
            var value = new SpeechCaptionValue.Delay(seconds);
            var phrase = "keep speech text visible for " + value.Label + (seconds == 1 ? " second" : " seconds");
            var command = SpeechTextCommand.Parse(phrase, "Nova");
            command.Should().Be(SpeechTextCommand.Parse("set display.speech-text-dismissal-delay to " + value.Label, "Nova"));
            command!.CaptionValue.Should().Be(value);
            SpeechTextCommand.FixedPhrases.Should().Contain(phrase);
        }
        SpeechTextCommand.Syntax.Should().Contain("show/on saves CurrentUtterance").And.Contain("hide/off saves Off")
            .And.Contain("reset saves Off only").And.Contain("pin current speech caption");
    }

    [Theory]
    [InlineData("turn on speech captions please")]
    [InlineData("turn off speech captions and stop listening")]
    [InlineData("show speech captions now")]
    [InlineData("hide speech text once")]
    [InlineData("show the words you're saying from the last response")]
    [InlineData("show the words from the clipboard")]
    [InlineData("list speech caption")]
    [InlineData("inspect speech captions")]
    [InlineData("status speech captions for that session")]
    [InlineData("reset speech caption settings")]
    [InlineData("get speech caption unknown")]
    [InlineData("set speech captions to sentence")]
    [InlineData("put speech captions in the centre")]
    [InlineData("put speech captions in the top left")]
    [InlineData("put speech captions on display 2")]
    [InlineData("keep speech text visible for ten seconds")]
    [InlineData("keep speech text visible for -1 seconds")]
    [InlineData("keep speech text visible for 31 seconds")]
    [InlineData("keep speech text visible for 05 seconds")]
    [InlineData("keep speech text visible for +5 seconds")]
    [InlineData("keep speech text visible for 5.0 seconds")]
    [InlineData("keep speech text visible for 1 seconds")]
    [InlineData("keep speech text visible for 5 seconds then hide")]
    [InlineData("pin current speech caption from yesterday")]
    [InlineData("unpin current speech caption and replay it")]
    [InlineData("pin speech caption")]
    [InlineData("unpin speech caption")]
    [InlineData("pin that text")]
    [InlineData("unpin that text")]
    public void ParseClarifiesReservedMalformedUtterancesWithoutInference(string input)
    {
        foreach (var text in new[] { input, "Nova, " + input })
        {
            var command = SpeechTextCommand.Parse(text, "Nova");
            command.Should().Be(new SpeechTextCommand(AppearanceCommandOperation.Clarify, Error: SpeechTextCommand.Syntax));
        }
    }

    [Theory]
    [InlineData("explain how to turn on speech captions")]
    [InlineData("the webpage says show speech text")]
    [InlineData("please show speech captions")]
    [InlineData("NovaX, show speech captions")]
    [InlineData("turn on listening")]
    [InlineData("hide your window")]
    [InlineData("set response mode to VisualOnly")]
    [InlineData("show that")]
    [InlineData("pin current answer")]
    public void ParseLeavesUnrelatedOrEmbeddedTextUnmatched(string input) =>
        SpeechTextCommand.Parse(input, "Nova").Should().BeNull();

    [Theory]
    [InlineData("Nova, show speech text\n")]
    [InlineData("show speech text\t")]
    [InlineData("show speech text\0")]
    [InlineData("keep speech text visible for \t5 seconds")]
    public void ParseRejectsControlsInOriginalInput(string input) =>
        SpeechTextCommand.Parse(input, "Nova")!.Error.Should().Contain("contains controls");

    [Fact]
    public void ParseEnforcesOriginalUtf8ByteLimitIncludingActivationPrefix()
    {
        const string phrase = "Nova, show speech text";
        var exactLimit = phrase.PadRight(SessionCommand.MaximumInputBytes);
        SpeechTextCommand.Parse(exactLimit, "Nova")!.Value.Should().Be(SpeechTextMode.CurrentUtterance);
        SpeechTextCommand.Parse(exactLimit + " ", "Nova")!.Error.Should().Contain("byte limit");
        var multibyte = "show speech text " + new string('\u00e9', 510);
        multibyte.Length.Should().BeLessThan(SessionCommand.MaximumInputBytes);
        Encoding.UTF8.GetByteCount(multibyte).Should().BeGreaterThan(SessionCommand.MaximumInputBytes);
        SpeechTextCommand.Parse(multibyte, "Nova")!.Error.Should().Contain("byte limit");
    }
}
