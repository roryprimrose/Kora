using System.Text;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class HostedClaudeSessionRequestTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, "exact-output", ModelTurnOutcome.Succeeded, 2)]
    [InlineData(RequestOrigin.ActivatedVoice, "exact-output", ModelTurnOutcome.Succeeded, 2)]
    [InlineData(RequestOrigin.LocalUi, "output-overflow", ModelTurnOutcome.Unknown, 2)]
    [InlineData(RequestOrigin.ActivatedVoice, "output-overflow", ModelTurnOutcome.Unknown, 2)]
    [InlineData(RequestOrigin.LocalUi, "count-exhausted", ModelTurnOutcome.DeniedEgress, 1)]
    [InlineData(RequestOrigin.ActivatedVoice, "count-exhausted", ModelTurnOutcome.DeniedEgress, 1)]
    public async Task Buffered_physical_budget_controls_complete_reviewed_submission_and_retained_outcome(
        RequestOrigin origin, string fault, ModelTurnOutcome outcome, int posts)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f);
        await c.InitializeAsync();
        var commands = new SessionRequestCommandService(c.Workspace, c.Conversation, c.Access, f.Time);
        const string body = "  original bounded Claude request\r\n  ";
        var review = (await commands.ExecuteAsync(SessionCommand.Parse("session request review active claude -- " + body, "Kora")!,
            origin, () => true, f.Token)).RequestReview!;
        review.Origin.Should().Be(origin);
        review.Body.Should().Be(body);
        var count = fault is "count-exhausted"
            ? "{\"input_tokens\":25}".PadRight(ModelContextEnvelope.MaximumOutputUtf8Bytes)
            : "{\"input_tokens\":25}";
        var answer = ClaudeMessagesRuntimeTests.Answer.PadRight(ModelContextEnvelope.MaximumOutputUtf8Bytes
            - Encoding.UTF8.GetByteCount(count) + (fault is "output-overflow" ? 1 : 0));
        c.Http.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(
            message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal) ? count : answer);
        var reviews = 0;
        c.Reviews.ClaudeOffered += offered =>
        {
            c.Http.Requests.Count(request => request.Method is "POST").Should().Be(reviews++);
            using var presentation = offered.Presentation();
            c.Reviews.Confirm(offered, true, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(Kora.Core.Diagnostics.HostOperationOutcome.Completed);
        };
        var result = (await commands.ExecuteAsync(SessionCommand.Parse("session request confirm", "Kora")!,
            RequestOrigin.LocalUi, () => true, f.Token)).RequestSubmission!;
        result.Message.Body.Should().Be(body);
        result.Message.Request.Origin.Should().Be(origin);
        result.Message.Request.SessionId.Should().Be(f.Request.SessionId);
        result.Message.Outcome.Should().Be(outcome);
        result.Message.AssistantResponse?.Body.Should().Be(outcome == ModelTurnOutcome.Succeeded ? "genuine exact answer" : null);
        (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, result.Message.Request.RequestId, f.Token))
            .Should().Be(result.Message);
        c.Http.Requests.Count(request => request.Method is "POST").Should().Be(posts);
        reviews.Should().Be(posts);
        c.Runtime.IsQuiescent.Should().BeTrue();
        c.Turns.IsQuiescent.Should().BeTrue();
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body).And.NotContain("genuine exact answer")
            .And.NotContain(ClaudeAccountBrokerTests.Fixture.Credential);
    }
}
