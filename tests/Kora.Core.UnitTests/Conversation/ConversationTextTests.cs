using System.Text;
using System.Text.Json;

using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    [InlineData("e\u0301 \ud83d\ude42\n  exact\r\n")]
    [InlineData("👩‍💻")]
    public void ExactDraftUnicodeAndWhitespaceAreNeverNormalized(string text)
    {
        ConversationText.Validate(text);
        var draft = new SavedConversationDraft(new(Guid.NewGuid()), 1, text);
        draft.Validate();
        JsonSerializer.Deserialize<SavedConversationDraft>(JsonSerializer.Serialize(draft))!.Body.Should().Be(text);
    }

    [Fact]
    public void BothIndependentBudgetsAreMeasuredWithoutTruncation()
    {
        var chars = new string('x', 4096);
        ConversationText.Validate(chars, submission: true);
        var emoji = string.Concat(Enumerable.Repeat("🙂", 2048));
        emoji.Length.Should().Be(4096);
        Encoding.UTF8.GetByteCount(emoji).Should().Be(8192);
        ConversationText.Validate(emoji, submission: true);
        ConversationText.Validate(new string('\u0800', 4096), submission: true);
        var characterOverflow = () => ConversationText.Validate(chars + "x");
        characterOverflow.Should().Throw<InvalidDataException>().WithMessage("*UTF-16*");
        var byteOverflow = () => ConversationText.Validate(new string('x', 16385));
        byteOverflow.Should().Throw<InvalidDataException>().WithMessage("*UTF-8*");
        var byteBoundary = () => ConversationText.Validate(new string('x', 16384));
        byteBoundary.Should().Throw<InvalidDataException>().WithMessage("*UTF-16*");
        // With valid Unicode, 4096 UTF-16 units cannot consume 16384 UTF-8 bytes; both approved guards still apply.
        ConversationText.MaximumUtf16Characters.Should().Be(4096);
        ConversationText.MaximumUtf8Bytes.Should().Be(16384);
        ConversationText.MaximumSessionMessages.Should().Be(100);
        ConversationText.MaximumPageRecords.Should().Be(50);
        ConversationText.MaximumPageUtf8Bytes.Should().Be(65536);
    }

    [Theory]
    [InlineData("high-surrogate", "Unicode")]
    [InlineData("low-surrogate", "Unicode")]
    [InlineData("\0", "control")]
    [InlineData("\u0001", "control")]
    [InlineData("\u007f", "control")]
    [InlineData("\ufffe", "binary")]
    [InlineData("\uffff", "binary")]
    public void MalformedUnicodeAndBinaryControlFailuresRemainDistinct(string body, string reason)
    {
        body = body switch { "high-surrogate" => "\ud800", "low-surrogate" => "\udc00", _ => body };
        var act = () => ConversationText.Validate(body);
        act.Should().Throw<InvalidDataException>().WithMessage("*" + reason + "*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void EmptySubmissionsAreNotSavedAsMessages(string body)
    {
        var act = () => ConversationText.Validate(body, submission: true);
        act.Should().Throw<InvalidDataException>().WithMessage("*empty*");
    }

    [Fact]
    public void DraftRevisionAndNullBodyAreCanonical()
    {
        var id = new HostId<SessionIdentity>(Guid.NewGuid());
        new SavedConversationDraft(id, 0, null).Validate();
        new SavedConversationDraft(id, 2, null).Validate();
        foreach (var invalid in new[] { new SavedConversationDraft(id, -1, null),
            new SavedConversationDraft(id, 0, ""), new SavedConversationDraft(default, 1, "body"),
            new SavedConversationDraft(id, 1, "body") { Version = 99 } })
        {
            invalid.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
        }
        var nullText = () => ConversationText.Validate(null!);
        nullText.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UnconfirmedSubmissionHasExactRequestIdentityAndCapacityIsARejectionNotEviction()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var cause = new IOException("required commit lost");
        var uncertainty = new ConversationSubmissionUnconfirmedException(request, cause);
        uncertainty.Request.Should().BeSameAs(request);
        uncertainty.InnerException.Should().BeSameAs(cause);
        uncertainty.Message.Should().Contain("without retry").And.Contain("rollback");
        new ConversationCapacityException().Message.Should().Contain("100 per session").And.Contain("no content was evicted");
        new ConversationSnapshotChangedException().Message.Should().Contain("terminal status changed").And.Contain("without replay");
    }
    [Fact]
    public void MessageAndCompletePageHaveExactSourceAndSeparateHostStatus()
    {
        var message = Message();
        message.Validate();
        message.ExpectedTaskState.Should().Be(HostTaskState.IntentRecorded);
        var terminal = message with { Revision = 2, Outcome = ModelTurnOutcome.Unavailable, Reason = ModelTurnReason.UnregisteredModel };
        terminal.Validate();
        terminal.ExpectedTaskState.Should().Be(HostTaskState.Unavailable);
        (terminal with { Outcome = ModelTurnOutcome.Cancelled }).ExpectedTaskState.Should().Be(HostTaskState.Cancelled);
        (terminal with { Outcome = ModelTurnOutcome.Unknown, Reason = ModelTurnReason.HostInterrupted }).ExpectedTaskState.Should().Be(HostTaskState.Interrupted);
        (terminal with { Outcome = ModelTurnOutcome.Unknown }).ExpectedTaskState.Should().Be(HostTaskState.Unknown);
        (terminal with { Outcome = ModelTurnOutcome.Denied }).ExpectedTaskState.Should().Be(HostTaskState.Denied);
        var session = new Kora.Core.Authorization.WorkSessionAuthorization(message.Request.SessionId, message.Generation, false);
        var cursor = new ConversationCursor(session.SessionId, session.Generation, 2, 1, 2);
        var page = new ConversationPage(session, 2, 2, [terminal], cursor);
        var copy = JsonSerializer.Deserialize<ConversationPage>(ConversationPage.Serialize(page))!;
        copy.Messages.Single().Should().Be(terminal);
        copy.Next.Should().Be(cursor);
        new ConversationDraftSnapshot(session, new(session.SessionId, 1, "draft")).Draft.Body.Should().Be("draft");
        new ConversationSubmissionReceipt(terminal, new(message.Request, new(2), HostTaskState.Unavailable))
            .Task.State.Should().Be(HostTaskState.Unavailable);
        terminal.Body.Should().Be(message.Body);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("version")]
    [InlineData("success")]
    [InlineData("missing-request")]
    [InlineData("sequence-zero")]
    [InlineData("sequence-overflow")]
    [InlineData("revision-zero")]
    [InlineData("revision-overflow")]
    [InlineData("offset")]
    [InlineData("origin")]
    [InlineData("invocation")]
    [InlineData("missing-reason")]
    [InlineData("missing-outcome")]
    [InlineData("unknown-outcome")]
    [InlineData("unknown-reason")]
    [InlineData("premature-status")]
    [InlineData("missing-terminal")]
    public void UnsupportedSourceOrStatusCannotMasqueradeAsConversation(string failure)
    {
        var message = Message();
        var invalid = failure switch
        {
            "missing-request" => message with { Request = null! },
            "version" => message with { Version = 99 },
            "success" => message with { Revision = 2, Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.None },
            "generation" => message with { Generation = default },
            "sequence-zero" => message with { Sequence = 0 },
            "sequence-overflow" => message with { Sequence = 101 },
            "revision-zero" => message with { Revision = 0 },
            "revision-overflow" => message with { Revision = 3 },
            "offset" => message with { SubmittedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) },
            "origin" => message with { Request = HostRequest.Create(RequestOrigin.HostSystem) },
            "invocation" => message with { Request = new(message.Request.RequestId, message.Request.SessionId,
                message.Request.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())) },
            "missing-reason" => message with { Outcome = ModelTurnOutcome.Denied },
            "missing-outcome" => message with { Reason = ModelTurnReason.HostAdmissionClosed },
            "unknown-outcome" => message with { Outcome = (ModelTurnOutcome)999, Reason = ModelTurnReason.None },
            "unknown-reason" => message with { Outcome = ModelTurnOutcome.Denied, Reason = (ModelTurnReason)999 },
            "premature-status" => message with { Outcome = ModelTurnOutcome.Unavailable, Reason = ModelTurnReason.UnregisteredModel },
            _ => message with { Revision = 2 },
        };
        invalid.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    private static ConversationMessage Message() => new(HostRequest.Create(RequestOrigin.LocalUi), new(1), 1, 1,
        DateTimeOffset.UnixEpoch, "  original e\u0301🙂\r\n", null, null);
}
