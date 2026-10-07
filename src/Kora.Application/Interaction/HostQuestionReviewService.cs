using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.Interaction;

// Reading the native review is an audited interaction, never approval or grant use.
public sealed class HostQuestionReviewService(IHostInteractionStore store, TimeProvider time)
{
    private readonly InteractionTransaction transaction = new(store);

    public ValueTask<HostInteractionDecision> ReviewAsync(HostQuestionKey key, CancellationToken cancellationToken) =>
        transaction.RunAsync(key.Request, "question.review", snapshot => Review(snapshot, key), cancellationToken);

    private (HostInteractionSnapshot, HostInteractionDecision) Review(HostInteractionSnapshot snapshot, HostQuestionKey key)
    {
        var index = InteractionTransaction.FindQuestion(snapshot, key);
        if (!InteractionTransaction.CanInteract(snapshot) || index < 0)
        {
            return InteractionTransaction.Reject(snapshot, "question-conflict", HostInteractionOutcome.Conflict);
        }
        var question = snapshot.Questions[index];
        if (question.Status != QuestionStatus.Pending || question.SessionGeneration != snapshot.Session.Generation)
        {
            return InteractionTransaction.Reject(snapshot, "question-closed", HostInteractionOutcome.Conflict);
        }
        if (question.ExpiresAt <= time.GetUtcNow())
        {
            return HostQuestionService.Replace(snapshot, question with { Key = key.Next(), Status = QuestionStatus.Expired },
                HostInteractionOutcome.Expired, "question-expired");
        }
        if (question.Proposal is { } proposal
            && (proposal != snapshot.Proposal || proposal.Request != key.Request
                || !snapshot.Policy.OtherMandatoryGatesSatisfied))
        {
            return InteractionTransaction.Reject(snapshot, "review-not-applicable");
        }
        return (snapshot, new(HostInteractionOutcome.Presented, "exact-question-reviewed", question));
    }
}
