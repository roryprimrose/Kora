using AwesomeAssertions;
using Kora.Core.Clock;
using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class LocalClockCommandTests
{
    [Theory]
    [InlineData("what time is it in utc", LocalClockOperation.Time)]
    [InlineData("what is today's date in utc", LocalClockOperation.Date)]
    [InlineData("what is the date and time in utc", LocalClockOperation.DateAndTime)]
    public void ExplicitUtcSuffixUsesTheSameBoundedGrammarAndCurrentNameActivation(string phrase, LocalClockOperation operation)
    {
        LocalClockCommand.FixedPhrases.Should().Contain(phrase);
        LocalClockCommand.Parse(phrase, "Nova")!.Should().Be(new LocalClockCommand(operation, false, LocalClockZone.Utc));
        LocalClockCommand.Parse(" Nova, " + phrase.ToUpperInvariant() + "? ", "Nova")!
            .Should().Be(new LocalClockCommand(operation, true, LocalClockZone.Utc));
        LocalClockCommand.Parse("Kora, " + phrase, "Nova").Should().BeNull();
        LocalClockCommand.Parse(phrase.Replace(" in utc", "", StringComparison.Ordinal), "Nova")!.Zone.Should().Be(LocalClockZone.HostLocal);
    }

    [Theory]
    [InlineData("what time is it", LocalClockOperation.Time)]
    [InlineData("what is today's date", LocalClockOperation.Date)]
    [InlineData("what is the date and time", LocalClockOperation.DateAndTime)]
    [InlineData("help local clock", LocalClockOperation.Help)]
    public void ExactPhrasesUseExistingCasePunctuationAndCurrentNameConventions(string phrase, LocalClockOperation operation)
    {
        LocalClockCommand.FixedPhrases.Should().Contain(phrase);
        LocalClockCommand.Parse(phrase, "Nova Prime")!.Operation.Should().Be(operation);
        LocalClockCommand.Parse(phrase, "What Time")!.Operation.Should().Be(operation);
        LocalClockCommand.Parse(phrase, "Nova Prime")!.IsActivated.Should().BeFalse();
        LocalClockCommand.Parse(" Nova Prime, " + phrase.ToUpperInvariant() + "? ", "Nova Prime")!.Operation.Should().Be(operation);
        LocalClockCommand.Parse(" Nova Prime, " + phrase, "Nova Prime")!.IsActivated.Should().BeTrue();
        LocalClockCommand.Parse("Kora, " + phrase, "Nova Prime").Should().BeNull();
        LocalClockCommand.Syntax.Should().Contain(phrase);
    }

    [Theory]
    [InlineData("what time")]
    [InlineData("what time is")]
    [InlineData("what time is it in Paris")]
    [InlineData("what is today's date tomorrow")]
    [InlineData("what is the date and time and restart")]
    [InlineData("help local clock please")]
    [InlineData("what\ttime is it")]
    [InlineData("what time is it\n")]
    [InlineData("what time is it in utc tomorrow")]
    [InlineData("what time is it in utc+10")]
    [InlineData("what is today's date in GMT")]
    [InlineData("what is the date and time in utc and restart")]
    public void OnlyBoundedReservedFamiliesClarifyInsteadOfFallingThrough(string text)
    {
        LocalClockCommand.Parse(text, "Nova")!.Operation.Should().Be(LocalClockOperation.Invalid);
        LocalClockCommand.Parse("Nova, " + text, "Nova")!.Operation.Should().Be(LocalClockOperation.Invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Nova")]
    [InlineData("what timezone is Paris")]
    [InlineData("what timepiece is this")]
    [InlineData("please tell me what time is it")]
    [InlineData("what is today's weather")]
    [InlineData("Nova, explain this")]
    public void UnrelatedRequestsAreNotClockCommands(string text) =>
        LocalClockCommand.Parse(text, "Nova").Should().BeNull();

    [Fact]
    public void OversizeReservedCommandsAreInvalid() =>
        LocalClockCommand.Parse("what time is it" + new string(' ', 256), "Nova")!.Operation.Should().Be(LocalClockOperation.Invalid);

    [Fact]
    public void AnAssistantNameCannotConsumeAReservedMalformedClockRequest()
    {
        LocalClockCommand.Parse("what time is it please", "What Time")!.Operation.Should().Be(LocalClockOperation.Invalid);
        LocalClockCommand.Parse("What Time, what time is it please", "What Time")!.Operation.Should().Be(LocalClockOperation.Invalid);
        LocalClockCommand.Parse("What Time, what time is it", "What Time")!.Operation.Should().Be(LocalClockOperation.Time);
    }
}
