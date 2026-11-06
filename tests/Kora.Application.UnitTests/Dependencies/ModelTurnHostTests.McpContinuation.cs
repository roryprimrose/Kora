using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Context;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, "once", true)]
    [InlineData(RequestOrigin.LocalUi, "session", true)]
    [InlineData(RequestOrigin.LocalUi, "perpetual", true)]
    [InlineData(RequestOrigin.ActivatedVoice, "once", true)]
    [InlineData(RequestOrigin.LocalUi, "once", false)]
    public async Task McpContinuationWaitsForSeparateExactResultConsentAfterPhysicalJoin(
        RequestOrigin origin, string invocationScope, bool structured)
    {
        using var f = new Fixture(origin);
        using var source = new McpSource();
        if (invocationScope is "session") { f.Metadata = new(f.Session.SessionId, new(1), new("Exact MCP conversation")); }
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var model = new McpModelTransport();
        await using var loop = new BoundedModelTurnAdapter(model, f.Time, f);
        var host = f.ConfiguredProfiles(adapter: loop);
        var approvals = new McpApprovals(f) { Choice = invocationScope };
        var broker = new McpBroker(source.Account) { Fault = structured ? null : "no-structured" };
        var service = McpService(f, host, source, broker, approvals, await source.Policy());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = 0;
        async Task<McpApprovalPresentation> Present(HostOperationProposal proposal, HostRevision generation,
            McpReview review, Func<bool> admitted, CancellationToken token)
        {
            if (review.Stage == McpReviewStage.ResultContinuation)
            {
                broker.Disposed.Should().BeTrue();
                model.Requests.Should().ContainSingle("successful MCP execution is not model result-use consent");
                observed.Should().Be(0);
                var exact = review.Continuation!;
                exact.ToString().Should().Be(nameof(McpContinuationReview));
                exact.Origin.Should().Be(origin);
                exact.PolicyRevision.Should().Be(LocalSourceRiskPolicy.Revision);
                exact.Question.Should().Be("Explain the exact reference.");
                exact.ConversationName.Should().Be(f.Metadata?.Name.Value ?? "Unnamed conversation");
                review.NativeText.Should().Contain(exact.Result.Text).And.Contain(exact.Result.StructuredJson ?? "(none)")
                    .And.Contain(source.File).And.Contain(McpSchema).And.Contain("SEPARATE ONE-USE")
                    .And.Contain("Decline or cancellation does not roll back");
                foreach (var altered in new[]
                {
                    review with { Continuation = exact with { Result = exact.Result with { Text = "substitute" } } },
                    review with { Continuation = exact with { Result = exact.Result with { StructuredJson = "{}" } } },
                    review with { Continuation = exact with { Result = exact.Result with { ObservedAt = exact.Result.ObservedAt.AddSeconds(1) } } },
                    review with { Continuation = exact with { Result = exact.Result with { SourceRevision = new string('b', 64) } } },
                    review with { Continuation = exact with { Question = "another task" } },
                    review with { Continuation = exact with { ConversationName = "another conversation" } },
                    review with { Continuation = exact with { Origin = RequestOrigin.HostSystem } },
                    review with { Continuation = exact with { PolicyRevision = exact.PolicyRevision + 1 } },
                    review with { Continuation = null },
                    review with { Stage = McpReviewStage.Invocation },
                }) { altered.IsBoundTo(proposal).Should().BeFalse(); }
                entered.SetResult();
                await release.Task;
            }
            return await approvals.Present(proposal, generation, review, admitted, token);
        }
        var submission = service.SubmitMcpAsync(new(f.Session, f.Metadata), "Explain the exact reference.",
            source.ServerId, source.Account, Present, McpSelect, static () => true, Token, origin,
            (_, _) => observed++);
        await entered.Task.WaitAsync(Token);
        try
        {
            submission.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            model.Requests.Should().ContainSingle();
            broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        }
        finally { release.TrySetResult(); }
        var receipt = await submission;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("The reviewed MCP reference says value 42.");
        observed.Should().Be(1);
        model.Requests.Should().HaveCount(2);
        approvals.Questions.Should().Be(4);
        approvals.Snapshots[approvals.LastProposal!.Request].Grants.Last().Scope.Should().Be(OperationGrantScope.Once);
        approvals.LastReview!.Continuation!.Result.Text.Should().Contain("value 42");
        f.Messages.Should().NotContain(item => item.Contains("value 42", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("deny")]
    [InlineData("session-consent")]
    [InlineData("perpetual-consent")]
    [InlineData("reused-consent")]
    [InlineData("source")]
    [InlineData("target-before-review")]
    [InlineData("control")]
    [InlineData("permission")]
    [InlineData("account")]
    [InlineData("generation")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task McpContinuationCannotDiscloseAfterDeclineOrAuthorityChangesDuringResultReview(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var model = new McpModelTransport();
        await using var loop = new BoundedModelTurnAdapter(model, f.Time, f);
        var host = f.ConfiguredProfiles(adapter: loop);
        var approvals = new McpApprovals(f)
        {
            DeniedStage = fault is "deny" ? McpReviewStage.ResultContinuation : null,
            ContinuationChoice = fault switch { "session-consent" => "session", "perpetual-consent" => "perpetual", _ => "once" },
        };
        var broker = new McpBroker(source.Account);
        if (fault is "target-before-review")
        { broker.AfterDispose = () => f.Metadata = new(f.Session.SessionId, new(1), new("Changed before review")); }
        var service = McpService(f, host, source, broker, approvals, await source.Policy());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var observed = 0;
        async Task<McpApprovalPresentation> Present(HostOperationProposal proposal, HostRevision generation,
            McpReview review, Func<bool> admitted, CancellationToken token)
        {
            var answer = await approvals.Present(proposal, generation, review, admitted, token);
            if (review.Stage == McpReviewStage.ResultContinuation)
            {
                broker.Disposed.Should().BeTrue();
                switch (fault)
                {
                    case "source": File.AppendAllText(source.File, " "); break;
                    case "control": f.ControlRevision++; break;
                    case "permission": approvals.Revoke(); break;
                    case "account": broker.Fault = "denied-account"; break;
                    case "generation": f.Session = f.Session with { Generation = new(2) }; break;
                    case "expiry": f.Time.Now = f.Time.Now.AddMinutes(3); break;
                    case "cancel": await cancellation.CancelAsync(); break;
                }
            }
            if (review.Stage == McpReviewStage.ResultContinuation && fault is "reused-consent")
            { return new(null, answer.Completed with { Outcome = Kora.Core.Interaction.HostInteractionOutcome.Consumed }); }
            return answer;
        }
        var submit = () => service.SubmitMcpAsync(new(f.Session, null), "Explain the reference.",
            source.ServerId, source.Account, Present, McpSelect, static () => true, cancellation.Token,
            onResult: (_, _) => observed++);
        if (fault is "deny" or "session-consent" or "perpetual-consent" or "reused-consent")
        {
            var receipt = await submit();
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
            receipt.Message.Reason.Should().Be(ModelTurnReason.ToolResultRejected);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        else { await submit.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>(); }
        await host.WaitForQuiescenceAsync(CancellationToken.None);
        model.Requests.Should().ContainSingle();
        observed.Should().Be(0);
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        broker.Disposed.Should().BeTrue();
        f.Retained!.AssistantResponse.Should().BeNull();
        f.Messages.Should().NotContain(item => item.Contains("value 42", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("risk-result", ModelTurnOutcome.Denied)]
    [InlineData("unknown-result", ModelTurnOutcome.Unavailable)]
    public async Task McpContinuationScreensDecodedCompleteStructuredResultBeforeConsentOrDisclosure(string fault, ModelTurnOutcome outcome)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var model = new McpModelTransport();
        await using var loop = new BoundedModelTurnAdapter(model, f.Time, f);
        var approvals = new McpApprovals(f);
        var broker = new McpBroker(source.Account) { Fault = fault };
        var service = McpService(f, f.ConfiguredProfiles(adapter: loop), source, broker, approvals, await source.Policy());
        var observed = 0;
        var receipt = await service.SubmitMcpAsync(new(f.Session, null), "Explain the reference.",
            source.ServerId, source.Account, approvals.Present, McpSelect, static () => true, Token,
            onResult: (_, _) => observed++);
        receipt.Message.Outcome.Should().Be(outcome);
        receipt.Message.Reason.Should().Be(ModelTurnReason.ToolResultRejected);
        receipt.Message.AssistantResponse.Should().BeNull();
        broker.Disposed.Should().BeTrue();
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        approvals.Stages.Should().Equal(McpReviewStage.Connection, McpReviewStage.ToolSelection, McpReviewStage.Invocation);
        model.Requests.Should().ContainSingle();
        observed.Should().Be(0);
        f.Messages.Should().NotContain(item => item.Contains("example", StringComparison.Ordinal));
    }
}
