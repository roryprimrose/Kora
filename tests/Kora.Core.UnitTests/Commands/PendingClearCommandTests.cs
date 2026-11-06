using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class PendingClearCommandTests
{
    [Theory]
    [InlineData("clear", false, SessionCommandOperation.PendingClearReview)]
    [InlineData("confirm", true, SessionCommandOperation.PendingClearConfirm)]
    [InlineData("cancel", true, SessionCommandOperation.PendingClearCancel)]
    public void ExactOrdinaryPendingGrammarKeepsSessionAndSeparateReviewIdentity(string verb, bool review,
        SessionCommandOperation operation)
    {
        var session = Guid.NewGuid();
        var id = Guid.NewGuid();
        var text = "session pending " + verb + " " + session.ToString("D") + (review ? " " + id.ToString("D") : "");
        foreach (var prefix in new[] { "", "Kora, " })
        {
            var parsed = SessionCommand.Parse(prefix + text, "Kora")!;
            parsed.Operation.Should().Be(operation);
            parsed.SessionId.Should().Be(session);
            parsed.PendingClearReviewId.Should().Be(review ? id : null);
            parsed.IsPendingClear.Should().BeTrue();
        }
        new SessionCommand(SessionCommandOperation.QueueClear).IsPendingClear.Should().BeFalse();
    }

    [Theory]
    [InlineData("session pending")]
    [InlineData("session pending clear")]
    [InlineData("session pending clear active")]
    [InlineData("session pending clear 00000000-0000-0000-0000-000000000000")]
    [InlineData("session pending confirm 11111111-1111-1111-1111-111111111111")]
    [InlineData("session pending cancel 11111111-1111-1111-1111-111111111111 invalid")]
    [InlineData("session pending stop 11111111-1111-1111-1111-111111111111 22222222-2222-2222-2222-222222222222")]
    [InlineData("session pending clear 11111111-1111-1111-1111-111111111111 extra")]
    [InlineData("session pending clear 11111111-1111-1111-1111-111111111111\n")]
    public void MalformedCommandsNeverFallThroughToDiagnosticOrModelAuthority(string text)
    {
        SessionCommand.Parse(text, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("session pending " + new string('x', 1024), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("ordinary non-command", "Kora").Should().BeNull();
    }
}
