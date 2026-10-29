using AwesomeAssertions;

using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed class ComputerPowerVoiceConfirmationTests
{
    [Theory]
    [InlineData("Confirm shutdown")]
    [InlineData("Confirm computer restart")]
    public void Computer_power_requires_the_whole_named_phrase_and_never_a_generic_choice(string phrase)
    {
        var spec = new QuestionSpec("Separately confirm the exact computer operation.", QuestionKind.SingleChoice,
            [new("confirm", phrase), new("cancel", "Cancel computer operation")],
            purpose: "computer-power", sourceId: "host-computer-power");
        QuestionVoiceAnswer.Parse(spec, phrase)!.Choices.Should().Equal("confirm");
        QuestionVoiceAnswer.Parse(spec, "Kora, " + phrase, "Kora")!.Choices.Should().Equal("confirm");
        foreach (var other in new[] { "1", "option 1", "yes", "confirm", "2", "option 2", "Kora, 1", "cancel" })
        { QuestionVoiceAnswer.Parse(spec, other, "Kora").Should().BeNull(); }
        QuestionVoiceAnswer.Parse(spec, "Cancel computer operation")!.Choices.Should().Equal("cancel");
    }
}