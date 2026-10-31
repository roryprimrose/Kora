using Kora.Application.Context;
using Kora.Core.Authorization;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Interaction;

public sealed partial class HostAuthorizationService
{
    /// <summary>Observes the original clipboard-read permission card without changing its authority.</summary>
    public ValueTask<HostInteractionDecision> ExplainPendingClipboardReadAsync(
        HostQuestionRecord expected, PendingOperationExplanation? previous, CancellationToken token) =>
        transaction.RunAsync(expected.Key.Request, "approval.explain-pending-clipboard-read", snapshot =>
        {
            var request = expected.Key.Request;
            var index = InteractionTransaction.FindQuestion(snapshot, expected.Key);
            if (index < 0 || !Eligible(snapshot, request) || snapshot.Permission is not { } policy
                || !ClipboardCommand.IsDeliberateOrigin(request.Origin)
                || snapshot.Admissions.Any(item => item.Proposal == snapshot.Proposal))
            {
                return InteractionTransaction.Reject(snapshot, "cannot-establish-current-pending-clipboard-read");
            }
            var question = snapshot.Questions[index];
            if (question.Status != QuestionStatus.Pending || question.SessionGeneration != snapshot.Session.Generation
                || question.SessionGeneration != expected.SessionGeneration || question.ExpiresAt <= time.GetUtcNow()
                || question.ExpiresAt != expected.ExpiresAt || question.Proposal != expected.Proposal
                || question.Proposal != snapshot.Proposal
                || !string.Equals(question.Spec.Purpose, "authorization", StringComparison.Ordinal)
                || !string.Equals(question.Spec.SourceId, "clipboard-read", StringComparison.Ordinal)
                || question.Proposal!.Effect != HostOperationEffect.BoundedRead
                || question.Proposal.Binding != ClipboardPermissionSourceUsePolicy.CreateReadBinding())
            {
                return InteractionTransaction.Reject(snapshot, "cannot-establish-current-pending-clipboard-read");
            }

            // Use the execution owner's exact matcher, but never ObserveContent, Consume, Admit or Dispatch.
            var grant = FindApplicableGrant(snapshot, request);
            if (grant is { CreatorChannel: not RequestOrigin.LocalUi })
            {
                return InteractionTransaction.Reject(snapshot, "clipboard-read-native-grant-required");
            }
            var explanation = new PendingOperationExplanation(question.Key, question.SessionGeneration,
                question.ExpiresAt, question.Proposal, policy, snapshot.Policy, grant);
            if (previous is not null && previous != explanation)
            {
                return InteractionTransaction.Reject(snapshot, "pending-action-explanation-changed", HostInteractionOutcome.Conflict);
            }
            return (snapshot, new HostInteractionDecision(HostInteractionOutcome.Presented,
                explanation.ApplicableGrant is null ? "clipboard-read-exact-grant-required" : "clipboard-read-exact-grant-observed",
                Explanation: explanation));
        }, token);
}
