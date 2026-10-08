using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class SpeechTextCommandTests
{
    [Theory]
    [InlineData("list speech text settings", AppearanceCommandOperation.List, null)]
    [InlineData("get display.speech-text", AppearanceCommandOperation.Get, null)]
    [InlineData("status display.speech-text", AppearanceCommandOperation.Get, null)]
    [InlineData("reset display.speech-text", AppearanceCommandOperation.Reset, null)]
    [InlineData("set display.speech-text to Off", AppearanceCommandOperation.Set, SpeechTextMode.Off)]
    [InlineData("set display.speech-text to CurrentUtterance", AppearanceCommandOperation.Set, SpeechTextMode.CurrentUtterance)]
    public void Exact_current_name_and_bare_commands_share_one_contract(string input, AppearanceCommandOperation operation, SpeechTextMode? value)
    {
        foreach (var text in new[] { input, "Nova, " + input, "nova " + input.ToUpperInvariant() })
        {
            SpeechTextCommand.Parse(text, "Nova").Should().Be(new SpeechTextCommand(operation, value));
        }
        SpeechTextCommand.Parse("Kora, " + input, "Nova").Should().BeNull();
        SpeechTextCommand.FixedPhrases.Should().Contain(input);
    }

    [Theory]
    [InlineData("set display.speech-text to current sentence")]
    [InlineData("set display.speech-text to 1")]
    [InlineData("set display.speech-text to Off trailing")]
    [InlineData("list speech text")]
    [InlineData("get display.speech-text.extra")]
    [InlineData("reset display.speech-text\n")]
    public void Unsupported_or_malformed_commands_clarify_without_widening(string input)
    {
        SpeechTextCommand.Parse(input, "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Fact]
    public void Limits_and_unrelated_commands_are_not_reinterpreted()
    {
        SpeechTextCommand.Parse("get display.speech-text " + new string('a', 1024), "Nova")!.Error.Should().Contain("byte limit");
        SpeechTextCommand.Parse("Kora", "Kora").Should().BeNull();
        SpeechTextCommand.Parse("Korax, get display.speech-text", "Kora").Should().BeNull();
        SpeechTextCommand.Parse("show speech text", "Kora").Should().BeNull();
        SpeechTextCommand.Parse("list speech text settings", "Kora")!.Error.Should().BeNull();
    }
}
