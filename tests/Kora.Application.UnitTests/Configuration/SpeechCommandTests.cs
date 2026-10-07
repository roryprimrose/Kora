using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class SpeechCommandTests
{
    [Theory]
    [InlineData("Kora, list speech settings", AppearanceCommandOperation.List)]
    [InlineData("Kora get speech.provider", AppearanceCommandOperation.Get)]
    [InlineData("get speech voice", AppearanceCommandOperation.Get)]
    [InlineData("reset speech.voice", AppearanceCommandOperation.Reset)]
    [InlineData("reset speech provider", AppearanceCommandOperation.Reset)]
    [InlineData("set speech.voice to Voice One", AppearanceCommandOperation.Set)]
    [InlineData("set speech provider to kokoro", AppearanceCommandOperation.Set)]
    [InlineData("set speech.summary-words to 40", AppearanceCommandOperation.Set)]
    [InlineData("get speech summary sentences", AppearanceCommandOperation.Get)]
    [InlineData("reset speech.summary-sentences", AppearanceCommandOperation.Reset)]
    [InlineData("list speech", AppearanceCommandOperation.Clarify)]
    [InlineData("get speech.arbitrary", AppearanceCommandOperation.Clarify)]
    [InlineData("set speech.voice", AppearanceCommandOperation.Clarify)]
    [InlineData("set speech.voice to ", AppearanceCommandOperation.Clarify)]
    public void Exact_commands_resolve_ids_and_spoken_names(string text, AppearanceCommandOperation operation)
    {
        var parsed = SpeechCommand.Parse(text, "Kora");
        parsed.Should().NotBeNull();
        parsed!.Operation.Should().Be(operation);
        if (operation == AppearanceCommandOperation.Set)
        {
            parsed.Value.Should().NotBeNullOrWhiteSpace();
            parsed.Descriptor.Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData("what speech settings")]
    [InlineData("Korax, set speech.voice to voice")]
    [InlineData("Nova, list speech settings")]
    [InlineData("Kora")]
    public void Unrelated_requests_are_not_configuration_commands(string text) =>
        SpeechCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void Command_bound_and_voice_bound_are_independent_and_exact()
    {
        var text = "set speech.voice to " + new string('v', SpeechCommand.MaximumLength - 20);
        text.Length.Should().Be(SpeechCommand.MaximumLength);
        SpeechCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Set);
        SpeechCommand.Parse(text + "v", "Kora")!.Error.Should().Contain("too long");
        SpeechCommand.Parse("Nova, get speech.voice", "Nova")!.Descriptor!.Option.Should().Be(SpeechOption.Voice);
        SpeechCommand.Syntax.Should().Contain("reset");
    }
}
