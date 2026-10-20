using AwesomeAssertions;
using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionAttachmentCommandTests
{
    private const string Id = "abcdefab-1234-1234-1234-123456789012";

    [Theory]
    [InlineData("session attachment help", SessionCommandOperation.Help)]
    [InlineData("Nova, session attachment inspect " + Id, SessionCommandOperation.AttachmentInspect)]
    [InlineData("SESSION ATTACHMENT INSPECT " + Id, SessionCommandOperation.AttachmentInspect)]
    public void ExactGrammarPreservesOnlyImmutableSessionAddress(string text, SessionCommandOperation operation)
    {
        var command = SessionCommand.Parse(text, "Nova")!;
        command.Operation.Should().Be(operation);
        command.SessionId.Should().Be(operation == SessionCommandOperation.Help ? null : Guid.Parse(Id));
        SessionCommand.DiscoveryPhrases.Should().Contain("session attachment help");
        SessionCommand.Syntax.Should().Contain(SessionCommand.AttachmentSyntax);
    }

    [Theory]
    [InlineData("session attachment")]
    [InlineData("session attachment help extra")]
    [InlineData("session attachment help \"name\"")]
    [InlineData("session attachment inspect")]
    [InlineData("session attachment inspect selected")]
    [InlineData("session attachment inspect \"Same name\"")]
    [InlineData("session attachment inspect C:\\private\\file.md")]
    [InlineData("session attachment inspect file:///C:/private/file.md")]
    [InlineData("session attachment inspect https://example.test/file")]
    [InlineData("session attachment inspect " + Id + " extra")]
    [InlineData("session attachment inspect " + Id + " \"name\"")]
    [InlineData("session attachment inspect ABCDEFAB-1234-1234-1234-123456789012")]
    [InlineData("session attachment inspect 00000000-0000-0000-0000-000000000000")]
    [InlineData("session attachment inspect {abcdefab-1234-1234-1234-123456789012}")]
    [InlineData("session attachment inspect abcdefab123412341234123456789012")]
    [InlineData("session attachment remove " + Id)]
    [InlineData("session attachment attach " + Id)]
    [InlineData("session attachment replace " + Id)]
    public void ReservedInvalidInputNeverBecomesUnmatchedOrANameSelector(string text) =>
        SessionCommand.Parse(text, "Nova")!.Operation.Should().Be(SessionCommandOperation.Invalid);
}
