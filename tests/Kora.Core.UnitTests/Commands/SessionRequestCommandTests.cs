using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Conversation;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionRequestCommandTests
{
    private const string Id = "12345678-1234-1234-1234-123456789012";

    [Theory]
    [InlineData("session request review ")]
    [InlineData("Nova, SESSION REQUEST REVIEW ")]
    public void ReviewPreservesExactOriginalBodyNotNormalizedCommandText(string prefix)
    {
        const string body = "  MiXeD e\u0301🙂\r\n\t\"quoted\" -- end  ";
        var command = SessionCommand.Parse(prefix + Id + " 2 0 -- " + body, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.RequestReview);
        command.SessionId.Should().Be(Guid.Parse(Id));
        command.Generation.Should().Be(2);
        command.MetadataRevision.Should().Be(0);
        command.RequestBody.Should().Be(body);
    }

    [Theory]
    [InlineData("confirm", SessionCommandOperation.RequestConfirm)]
    [InlineData("CANCEL", SessionCommandOperation.RequestCancel)]
    public void ConfirmationAndCancellationRequireExactReviewIdentity(string verb, SessionCommandOperation expected)
    {
        var command = SessionCommand.Parse("session request " + verb + " " + Id + " " + Id, "Kora")!;
        command.Operation.Should().Be(expected);
        command.RequestReviewId.Should().Be(Guid.Parse(Id));
    }

    [Fact]
    public void ReviewBodyUsesConversationBoundsRatherThanSmallCommandBounds()
    {
        var body = new string('\u0800', ConversationText.MaximumUtf16Characters);
        var input = "session request review " + Id + " 1 0 -- ";
        SessionCommand.Parse(input + body, "Kora")!.RequestBody.Should().Be(body);
        SessionCommand.Parse(input + body + "x", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse(input + "\ud800", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("session request help", "Kora")!.Operation.Should().Be(SessionCommandOperation.Help);
    }

    [Theory]
    [InlineData("")]
    [InlineData("help extra")]
    [InlineData("review selected 1 0 -- original")]
    [InlineData("review " + Id + " 0 0 -- original")]
    [InlineData("review " + Id + " 1 -1 -- original")]
    [InlineData("review " + Id + " 1 0 --")]
    [InlineData("review " + Id + " 1 0 -- ")]
    [InlineData("review " + Id + " 1 0 -- \r\n\t ")]
    [InlineData("review " + Id + " 1 0 -- \0")]
    [InlineData("review " + Id + " 1 0 -- api_key=synthetic-only-value")]
    [InlineData("confirm " + Id)]
    [InlineData("confirm " + Id + " 00000000-0000-0000-0000-000000000000")]
    [InlineData("confirm " + Id + " " + Id + " extra")]
    [InlineData("confirm " + Id + " " + Id + " -- injected")]
    [InlineData("cancel " + Id + " invalid")]
    [InlineData("submit " + Id + " " + Id)]
    [InlineData("review\t" + Id + " 1 0 -- original")]
    public void InvalidRequestNamespaceIsReservedWithoutModelOrQuestionFallback(string arguments) =>
        SessionCommand.Parse("session request " + arguments, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);

    [Fact]
    public void ExcessiveOrControlBearingHeaderNeverChangesBodyParsing()
    {
        SessionCommand.Parse("session request " + new string('x', 1025), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("session request confirm " + Id + "\t " + Id, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
    }
}
