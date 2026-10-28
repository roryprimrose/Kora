using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualJoinedLocalProofReductionRequiresFreshNativeReviewWithoutCreatingAnIntentOrSend(bool sourceInputMismatch)
    {
        using var f = new InteractionStorageFixture();
        f.Time.UseFrozenTimers = true;
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var local = new LocalUnavailableAdapter();
        var hosted = new CopilotConversationRuntime();
        await using var composed = new Composition(f, adapter: local, copilot: hosted, localFirst: true);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var request = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(request, target.Authority.Generation, 0,
            sourceInputMismatch ? "different original input" : "original with exact excerpts",
            static () => true, f.Token);
        var first = new ModelContextEvidence(new(Guid.NewGuid()), request, new(1), "remove this exact excerpt", ModelEvidenceDisclosure.HostedEligible);
        var second = new ModelContextEvidence(new(Guid.NewGuid()), request, new(1), "keep this exact excerpt", ModelEvidenceDisclosure.HostedEligible);
        var context = new ModelContextEnvelope(request, "host policy", "original with exact excerpts", [first, second],
            f.Time.Now, f.Time.Now.AddSeconds(15), ModelToolMode.None);
        var admission = await composed.Turns.AdmitAsync(ModelTurnChoice.Local, context, null, f.Token);
        var turn = admission.Turn!;
        (await composed.Turns.ReserveConversationResourcesAsync(turn, static () => true, f.Token)).Should().Be(ModelTurnReason.None);
        var dispatch = await f.Store.BeginConversationExecutionAsync(request, turn.Provenance, 0, static () => true, f.Token);
        turn.TaskRevision = dispatch.Task.Revision;
        turn.ConversationTarget = target;
        var result = await ((IModelTurnExecution)composed.Turns).RunAsync(turn, static () => true, f.Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        await composed.Turns.JoinConversationResourcesAsync(turn, f.Token);
        if (sourceInputMismatch)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                composed.HandoffWorkflow!.OfferAfterLocalAsync(turn, context, f.Token));
            f.Count("conversation_messages").Should().Be(1);
            f.Count("host_questions").Should().Be(0);
            hosted.Sends.Should().Be(0);
            await f.Store.CompleteConversationExecutionAsync(request, result, static () => true, f.Token);
            return;
        }
        var offered = await composed.HandoffWorkflow!.OfferAfterLocalAsync(turn, context, f.Token);
        var original = offered.Offer!;
        original.LocalUnavailable.Should().NotBeNull();
        var foreignProviderState = original.Target!.Session with
        {
            Session = original.Target.Session.Session with { Generation = new(2) },
        };
        var foreignRequest = new HostRequest(new(Guid.NewGuid()), request.SessionId, new(Guid.NewGuid()), request.Origin);
        var foreignContext = new ModelContextEnvelope(foreignRequest, context.SystemPolicy, context.UserRequest,
            context.Evidence.Select(item => item with { Request = foreignRequest }), context.CreatedAt, context.ExpiresAt, context.ToolMode);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await f.Store.CommitVerifiedLocalHandoffAsync(original.LocalUnavailable, original.Question, context,
                foreignContext, original.Target.Profile, original.Target.Selection, foreignProviderState, static () => true, f.Token));
        f.Count("conversation_messages").Should().Be(1);
        var review = composed.HandoffPresentation!.Open(original, RequestOrigin.LocalUi, static () => true)!;
        await using (review)
        {
            await review.Refresh(f.Token);
            await review.Remove(original, [first.Id], f.Token);
            var reduced = review.Offer!;
            reduced.Context.Evidence.Should().Equal(second);
            reduced.Context.ExpiresAt.Should().Be(context.ExpiresAt);
            reduced.Context.ToolMode.Should().Be(ModelToolMode.None);
            context.Evidence.Should().Equal(first, second);
            review.HasReviewed.Should().BeFalse();
            await review.Decide(reduced, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, f.Token);
            (await f.Store.ReadQuestionsAsync(request.SessionId, f.Token))
                .Single(item => item.Key.QuestionId == reduced.Question.Key.QuestionId).Status.Should().Be(QuestionStatus.Pending);
            await review.Refresh(f.Token);
            review.Preview.Should().NotContain("remove this exact excerpt").And.Contain("keep this exact excerpt");
            await review.Decide(reduced, ModelHandoffDecision.Decline, RequestOrigin.LocalUi, f.Token);
            (await f.Store.ReadQuestionsAsync(request.SessionId, f.Token))
                .Single(item => item.Key.QuestionId == original.Question.Key.QuestionId).Status.Should().Be(QuestionStatus.Cancelled);
        }
        await f.Store.CompleteConversationExecutionAsync(request, result, static () => true, f.Token);
        f.Count("conversation_messages").Should().Be(1);
        hosted.Sends.Should().Be(0);
        local.Calls.Should().Be(1);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }
}
