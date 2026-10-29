using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class ConversationExecutionCommandTests
{
    [Theory]
    [InlineData("get conversations.execution-maximum", ConversationExecutionOperation.Get, null)]
    [InlineData("Kora, review conversations.execution-maximum to 1", ConversationExecutionOperation.Review, 1)]
    [InlineData("review conversations.execution-maximum to 2", ConversationExecutionOperation.Review, 2)]
    [InlineData("confirm conversations.execution-maximum", ConversationExecutionOperation.Confirm, null)]
    [InlineData("cancel conversations.execution-maximum", ConversationExecutionOperation.Cancel, null)]
    [InlineData("set conversations.execution-maximum to 2", ConversationExecutionOperation.Clarify, null)]
    [InlineData("review conversations.execution-maximum to 3", ConversationExecutionOperation.Clarify, null)]
    [InlineData("review conversations.execution-maximum to 01", ConversationExecutionOperation.Clarify, null)]
    [InlineData("get conversations.execution-maximum\n", ConversationExecutionOperation.Clarify, null)]
    public void ExactCommandsNeverAuthorizeDispatch(string input, ConversationExecutionOperation operation, int? limit)
    {
        var result = ConversationExecutionCommand.Parse(input, "Kora")!;
        result.Operation.Should().Be(operation);
        result.Limit?.Value.Should().Be(limit);
    }

    [Fact]
    public void UnrelatedInputAndOversizedReservedInputAreNotModelSchedulingAuthority()
    {
        ConversationExecutionCommand.Parse("ordinary user request", "Kora").Should().BeNull();
        ConversationExecutionCommand.Parse(new string('x', 1024) + ConversationExecutionCommand.OptionId, "Kora")!
            .Operation.Should().Be(ConversationExecutionOperation.Clarify);
        ConversationExecutionCommand.Syntax.Should().Contain(ConversationExecutionCommand.OptionId);
        foreach (var phrase in ConversationExecutionCommand.FixedPhrases)
        { ConversationExecutionCommand.Parse(phrase, "Kora")!.Operation.Should().NotBe(ConversationExecutionOperation.Clarify); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void OutOfEnvelopeDemandIsRejected(int limit) =>
        ((Action)(() => _ = new ConversationExecutionLimit(limit))).Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("01")]
    [InlineData("2\n")]
    public void InvalidSavedValuesAreNotDefaults(string value) =>
        ((Action)(() => ConversationExecutionLimit.Parse(value))).Should().Throw<InvalidDataException>();
}
