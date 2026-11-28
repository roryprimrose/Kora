using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Conversation;

namespace Kora.Core.UnitTests.Commands;

public sealed class TurnCheckInReplyTests
{
    [Theory]
    [InlineData("Kora, keep going", ConversationCheckInOperation.KeepGoing, null)]
    [InlineData("Nova keep going until done.", ConversationCheckInOperation.KeepRunning, null)]
    [InlineData("  STOP THIS TURN!  ", ConversationCheckInOperation.Stop, null)]
    [InlineData("keep going for 10 minutes", ConversationCheckInOperation.KeepGoingFor, 10)]
    [InlineData("Kora keep going for 1 minute", ConversationCheckInOperation.KeepGoingFor, 1)]
    [InlineData("keep going for 60 MINUTES", ConversationCheckInOperation.KeepGoingFor, 60)]
    public void ExactRepliesProduceTypedChoices(string input, ConversationCheckInOperation operation, int? minutes)
    {
        var choice = TurnCheckInReply.Parse(input, "Nova")!.Choice!;
        choice.Operation.Should().Be(operation);
        choice.Minutes?.Minutes.Should().Be(minutes);
        choice.Minutes.HasValue.Should().Be(minutes.HasValue);
    }

    [Theory]
    [InlineData("keep going forever")]
    [InlineData("keep going for 0 minutes")]
    [InlineData("keep going for 61 minutes")]
    [InlineData("keep going for 05 minutes")]
    [InlineData("keep going for 2 minute")]
    [InlineData("keep going for ten minutes")]
    [InlineData("keep going for 5 hours")]
    [InlineData("keep going for 5")]
    [InlineData("keep going\tfor 5 minutes")]
    public void RecognizedButUnsupportedRepliesAreNotAccepted(string input) =>
        TurnCheckInReply.Parse(input, "Kora")!.Choice.Should().BeNull();

    [Fact]
    public void OversizedReplyIsNotAccepted() =>
        TurnCheckInReply.Parse("keep going " + new string('a', 1024), "Kora")!.Choice.Should().BeNull();

    [Theory]
    [InlineData("stop")]
    [InlineData("Kora stop listening")]
    [InlineData("approve once")]
    [InlineData("Koragoing")]
    public void UnrelatedInputIsNotACheckInReply(string input) =>
        TurnCheckInReply.Parse(input, "Kora").Should().BeNull();

    [Fact]
    public void FixedPhrasesAreTheParameterFreeReplies() =>
        TurnCheckInReply.FixedPhrases.Select(phrase => TurnCheckInReply.Parse(phrase, "Kora")!.Choice!.Operation)
            .Should().Equal(ConversationCheckInOperation.KeepGoing, ConversationCheckInOperation.KeepRunning,
                ConversationCheckInOperation.Stop);

    [Theory]
    [InlineData(ConversationCheckInOperation.KeepGoing, false, "keep-going")]
    [InlineData(ConversationCheckInOperation.KeepGoingFor, true, "keep-going-minutes")]
    [InlineData(ConversationCheckInOperation.KeepRunning, false, "keep-running")]
    [InlineData(ConversationCheckInOperation.Stop, false, "stop")]
    public void ChoicesCarryMinutesOnlyForKeepGoingFor(ConversationCheckInOperation operation, bool minutes, string reason)
    {
        new ConversationCheckInChoice(operation, minutes ? new OrdinaryTurnDeadline(3) : null).ReasonCode.Should().Be(reason);
        FluentActions.Invoking(() => new ConversationCheckInChoice(operation, minutes ? null : new OrdinaryTurnDeadline(3)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UnknownOperationIsRejected() =>
        FluentActions.Invoking(() => new ConversationCheckInChoice((ConversationCheckInOperation)99))
            .Should().Throw<ArgumentException>();
}
