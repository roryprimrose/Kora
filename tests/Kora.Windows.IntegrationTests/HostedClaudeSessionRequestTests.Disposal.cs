using System.Text;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class HostedClaudeSessionRequestTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    public async Task Buffered_physical_disposal_retains_unconfirmed_original_and_existing_recovery_never_replays(
        RequestOrigin origin, bool failCount)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var c = new Composition(f);
        await c.InitializeAsync();
        try
        {
            var responses = new List<ClaudeMessagesRuntimeTests.BufferedDisposalResponse>();
            c.Http.Respond = message =>
            {
                var count = message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal);
                const string countBody = "{\"input_tokens\":25}";
                var answer = ClaudeMessagesRuntimeTests.Answer.PadRight(ModelContextEnvelope.MaximumOutputUtf8Bytes
                    - Encoding.UTF8.GetByteCount(countBody));
                (Encoding.UTF8.GetByteCount(countBody) + Encoding.UTF8.GetByteCount(answer)).Should().Be(ModelContextEnvelope.MaximumOutputUtf8Bytes);
                var response = new ClaudeMessagesRuntimeTests.BufferedDisposalResponse(
                    count ? countBody : answer, count == failCount);
                responses.Add(response);
                return response;
            };
            const string body = "  original disposal request\r\nunchanged  ";
            var commands = new SessionRequestCommandService(c.Workspace, c.Conversation, c.Access, f.Time);
            var review = (await commands.ExecuteAsync(SessionCommand.Parse(
                "session request review active claude -- " + body, "Kora")!, origin, () => true, f.Token)).RequestReview!;
            var offers = 0;
            c.Reviews.ClaudeOffered += offered =>
            {
                c.Http.Requests.Count(item => item.Method is "POST").Should().Be(offers++);
                offered.Review.Transmission.Provenance.Request.SessionId.Should().Be(f.Request.SessionId);
                offered.Review.Transmission.Provenance.Request.Origin.Should().Be(origin);
                Encoding.UTF8.GetString(offered.Body.AsSpan()).Should().Contain("original disposal request\\r\\nunchanged");
                using var presentation = offered.Presentation();
                c.Reviews.Confirm(offered, true, RequestOrigin.ActivatedVoice).Should().BeFalse();
                c.Reviews.Confirm(offered, true, RequestOrigin.LocalUi).Should().BeTrue();
                presentation.Complete(HostOperationOutcome.Completed);
            };
            var phases = new List<ConversationExecutionPhase>();
            c.Conversation.RunningConversationChanged += status => phases.Add(status.Phase);
            var exception = await Assert.ThrowsAsync<ConversationSubmissionUnconfirmedException>(() => commands.ExecuteAsync(
                SessionCommand.Parse("session request confirm", "Kora")!, RequestOrigin.LocalUi, () => true, f.Token));
            exception.Request.SessionId.Should().Be(review.Target.Authority.SessionId);
            exception.Request.Origin.Should().Be(origin);
            exception.InnerException.Should().BeOfType<InvalidOperationException>();
            var retained = await f.Store.ReadConversationMessageAsync(exception.Request.SessionId, exception.Request.RequestId, f.Token);
            retained!.Request.Should().Be(exception.Request);
            retained.Body.Should().Be(body);
            retained.AssistantResponse.Should().BeNull();
            retained.Outcome.Should().BeNull("physical release was not confirmed, so no terminal success may be committed");
            (await f.Store.ReadTaskAsync(exception.Request.SessionId, exception.Request.TaskId, f.Token))!.Task.State
                .Should().Be(HostTaskState.DispatchRecorded);
            phases.Should().Contain(ConversationExecutionPhase.Unconfirmed);
            c.Runtime.IsQuiescent.Should().BeFalse();
            var sends = failCount ? 1 : 2;
            offers.Should().Be(sends);
            c.Http.Requests.Count(item => item.Method is "POST").Should().Be(sends);
            responses.Should().HaveCount(sends).And.OnlyContain(response => response.DisposeAttempts == 1
                && response.Reader.AsyncDisposeAttempts == 1 && response.Reader.SyncDisposeAttempts >= 1);
            c.Http.Audits.Should().Contain(item => string.Equals(item.ActionId, "runtime.resource.quarantine", StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Unknown);
            c.Http.Audits.Should().NotContain(item => string.Equals(item.ActionId, "runtime.resource.release", StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Succeeded);

            var recovered = await new HostTaskCoordinator(f.Tasks).RecoverAsync(100, f.Token);
            recovered.Should().ContainSingle().Which.State.Should().Be(HostTaskState.Unknown);
            var terminal = await f.Store.ReadConversationMessageAsync(exception.Request.SessionId, exception.Request.RequestId, f.Token);
            terminal!.Outcome.Should().Be(ModelTurnOutcome.Unknown);
            terminal.AssistantResponse.Should().BeNull();
            terminal.Request.Should().Be(exception.Request);
            terminal.Body.Should().Be(body);
            var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var requests = c.Http.Requests.Count;
            var blocked = await Assert.ThrowsAsync<ConversationSubmissionUnconfirmedException>(() => c.Conversation.SubmitClaudeAsync(
                target, "must not send another turn", () => true, f.Token));
            blocked.InnerException.Should().BeOfType<InvalidOperationException>();
            c.Runtime.IsQuiescent.Should().BeFalse();
            offers.Should().Be(sends);
            c.Http.Requests.Should().HaveCount(requests);
            (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages
                .Should().ContainSingle().Which.Should().Be(terminal);
            WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body).And.NotContain("genuine exact answer")
                .And.NotContain(ClaudeAccountBrokerTests.Fixture.Credential);
        }
        finally
        {
            if (c.Runtime.IsQuiescent) { await c.DisposeAsync(); }
            else { await Assert.ThrowsAsync<IOException>(() => c.DisposeAsync().AsTask()); }
        }
    }
}
