using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Conversation;

namespace Kora.Core.UnitTests.Commands;

public sealed class ForegroundConversationCommandTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Nova, ")]
    [InlineData("  NOVA, ")]
    public void DeliberateCurrentConversationPreservesEveryBodyCharacter(string activation)
    {
        const string body = "  shutdown e\u0301\U0001f642\r\n\t-- session request confirm  ";
        var command = SessionCommand.Parse(activation + "ask this conversation " + body, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.RequestReview);
        command.RequestForegroundTarget.Should().BeTrue();
        command.RequestBody.Should().Be(body);
        command.RequestProvider.Should().Be(SessionRequestReview.ProviderChoice.Default);
        command.SessionId.Should().BeNull();
        command.RequestActiveTarget.Should().BeFalse();
    }

    [Theory]
    [InlineData("ask this")]
    [InlineData("ask this conversation")]
    [InlineData("ask this conversation ")]
    [InlineData("ask this conversation \r\n\t ")]
    [InlineData("ask this conversation: body")]
    [InlineData("ask this\tconversation body")]
    [InlineData("ask this conversation api_key=synthetic-only-value")]
    [InlineData("ask this conversation body\0")]
    public void MalformedReservedInputCannotFallThroughToAnotherConsumer(string input) =>
        SessionCommand.Parse(input, "Nova")!.Operation.Should().Be(SessionCommandOperation.Invalid);

    [Fact]
    public void ExistingConversationBoundsAreIndependentOfTheCommandHeader()
    {
        var accepted = new string('x', ConversationText.MaximumUtf16Characters);
        SessionCommand.Parse("ask this conversation " + accepted, "Nova")!.RequestBody.Should().Be(accepted);
        SessionCommand.Parse("ask this conversation " + accepted + "x", "Nova")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("ask this conversation " + new string((char)0xd800, 1), "Nova")!.Operation
            .Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("ask another conversation a question", "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData("Ask")]
    [InlineData("Ask this")]
    [InlineData("Ask this conversation")]
    public void AssistantNameOverlappingReservedGrammarCannotConsumeOrRewriteTheBody(string name)
    {
        SessionCommand.Parse("ask this conversation original", name)!.RequestBody.Should().Be("original");
        SessionCommand.Parse("ask this conversation original", name)!.RequestActivationPrefixed.Should().BeFalse();
        var command = SessionCommand.Parse(name + ", ask this conversation original", name)!;
        command.RequestBody.Should().Be("original");
        command.RequestActivationPrefixed.Should().BeTrue();
        SessionCommand.Parse("ask this conversation", name)!.Operation.Should().Be(SessionCommandOperation.Invalid);
    }
}
