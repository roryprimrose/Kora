using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Conversation;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionRequestCommandTests
{
    private const string Id = "12345678-1234-1234-1234-123456789012";

    [Theory]
    [InlineData("active", "", SessionRequestReview.ProviderChoice.Default)]
    [InlineData("ACTIVE", "COPILOT", SessionRequestReview.ProviderChoice.Copilot)]
    [InlineData("active", "claude", SessionRequestReview.ProviderChoice.Claude)]
    [InlineData("\"Readable topic\"", "", SessionRequestReview.ProviderChoice.Default)]
    [InlineData("\"Readable topic\"", "copilot", SessionRequestReview.ProviderChoice.Copilot)]
    [InlineData("\"Readable topic\"", "claude", SessionRequestReview.ProviderChoice.Claude)]
    public void Readable_review_preserves_complete_original_body_and_explicit_provider(string target, string provider,
        SessionRequestReview.ProviderChoice expected)
    {
        const string body = "  MiXeD e\u0301🙂\r\n\t\"quoted\" -- end  ";
        var command = SessionCommand.Parse("Nova, session request review " + target
            + (provider.Length == 0 ? "" : " " + provider) + " -- " + body, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.RequestReview);
        command.RequestBody.Should().Be(body);
        command.RequestProvider.Should().Be(expected);
        command.RequestActiveTarget.Should().Be(target[0] != '"');
        (command.RequestTargetName?.Value).Should().Be(target[0] == '"' ? "Readable topic" : null);
        command.SessionId.Should().BeNull();
        command.RequestUsesPendingReview.Should().BeFalse();
    }

    [Fact]
    public void Doubled_quotes_are_readable_name_data_and_do_not_change_body_or_provider()
    {
        var command = SessionCommand.Parse("session request review \"A \"\"quoted\"\" topic\" claude -- exact", "Kora")!;
        command.Operation.Should().Be(SessionCommandOperation.RequestReview);
        command.RequestTargetName!.Value.Should().Be("A \"quoted\" topic");
        command.RequestBody.Should().Be("exact");
        SessionCommand.Parse("session request review \"name -- data\" claude -- body -- data", "Kora")!
            .RequestTargetName!.Value.Should().Be("name -- data");
    }

    [Theory]
    [InlineData("confirm", SessionCommandOperation.RequestConfirm)]
    [InlineData("CANCEL", SessionCommandOperation.RequestCancel)]
    public void Readable_confirmation_consumes_only_host_held_pending_review(string verb, SessionCommandOperation operation)
    {
        var command = SessionCommand.Parse("session request " + verb, "Kora")!;
        command.Operation.Should().Be(operation);
        command.RequestUsesPendingReview.Should().BeTrue();
        command.RequestReviewId.Should().BeNull();
        command.SessionId.Should().BeNull();
    }

    [Theory]
    [InlineData("review active hosted -- original")]
    [InlineData("review active claude extra -- original")]
    [InlineData("review \"\" claude -- original")]
    [InlineData("review \" \" claude -- original")]
    [InlineData("review \"\"\" claude -- original")]
    [InlineData("review \"A\"\" claude -- original")]
    [InlineData("review \"a\"unescaped\" claude -- original")]
    [InlineData("review \"a\"unescaped\"b\" claude -- original")]
    [InlineData("review \"Readable topic -- original")]
    [InlineData("review \"Readable topic\" claude -- ")]
    [InlineData("review active claude -- api_key=synthetic-only-value")]
    [InlineData("review active claude -- original\0")]
    [InlineData("confirm claude")]
    [InlineData("unknown")]
    [InlineData("cancel -- data")]
    public void Malformed_readable_requests_remain_reserved_without_fallback(string arguments) =>
        SessionCommand.Parse("session request " + arguments, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);

    [Fact]
    public void Malformed_UTF16_original_body_is_rejected_without_xunit_attribute_serialization()
    {
        var body = new string((char)0xd800, 1);
        SessionCommand.Parse("session request review active claude -- " + body, "Kora")!.Operation
            .Should().Be(SessionCommandOperation.Invalid);
    }

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
        command.RequestProvider.Should().Be(SessionRequestReview.ProviderChoice.Default);
    }

    [Theory]
    [InlineData("session request review ", "copilot")]
    [InlineData("Nova, SESSION REQUEST REVIEW ", "COPILOT")]
    public void ExplicitCopilotReviewPinsProviderWithoutChangingOriginalBody(string prefix, string provider)
    {
        const string body = "  Original\r\n\t-- copilot  ";
        var command = SessionCommand.Parse(prefix + Id + " 2 0 " + provider + " -- " + body, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.RequestReview);
        command.RequestProvider.Should().Be(SessionRequestReview.ProviderChoice.Copilot);
        command.RequestBody.Should().Be(body);
        command.Generation.Should().Be(2);
        command.MetadataRevision.Should().Be(0);
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
    [InlineData("review " + Id + " 1 0 hosted -- original")]
    [InlineData("review " + Id + " 1 0 copilot extra -- original")]
    [InlineData("review " + Id + " 1 0 copilot -- api_key=synthetic-only-value")]
    [InlineData("review " + Id + " 1 0 copilot -- ")]
    [InlineData("confirm " + Id + " " + Id + " copilot")]
    public void InvalidRequestNamespaceIsReservedWithoutModelOrQuestionFallback(string arguments) =>
        SessionCommand.Parse("session request " + arguments, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);

    [Fact]
    public void ExcessiveOrControlBearingHeaderNeverChangesBodyParsing()
    {
        SessionCommand.Parse("session request " + new string('x', 1025), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("session request confirm " + Id + "\t " + Id, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
    }
}
