using Kora.Application.Dependencies;
using Kora.Application.Diagnostics.EvidenceAnswers;
using Kora.Application.Hosting;
using Kora.Core.Context.EvidenceAnswers;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.EvidenceAnswers;

/// <summary>Reuses canonical fresh-session control, original-input admission and validated final retention.</summary>
internal sealed class EvidenceAnswerConversation(SessionWorkspaceService sessions, SessionConversationService conversations,
    IModelTurnExecution execution) : IEvidenceAnswerConversation
{
    public async Task<ConversationSubmissionReceipt> SubmitAsync(EvidenceAnswerReview review, Func<bool> current,
        Func<CancellationToken, Task> validate, CancellationToken token)
    {
        using var destination = HostActivity.BeginRoot(review.Destination, HostActivityLayer.Application,
            HostOperation.Request, [new(HostActivity.RequireCurrent().Activity!.Context)]);
        try
        {
            await validate(token).ConfigureAwait(false);
            var target = await sessions.CreateLocalContextSessionAsync(review.Destination, current, token).ConfigureAwait(false);
            await validate(token).ConfigureAwait(false);
            HostRequest? original = null;
            ModelContextEnvelope Bind(HostRequest request, DateTimeOffset now)
            {
                if (original is not null) { throw new InvalidOperationException("The reviewed context is single-use."); }
                original = request;
                return review.Bind(request, target.Authority.Generation, now);
            }
            var receipt = await conversations.SubmitReviewedContextAsync(target, review.Question, Bind, current,
                validate, [new(destination.Activity!.Context)], RequestOrigin.LocalUi, token,
                resolveResponse: result => EvidenceAnswerResponse.Resolve(review,
                    original ?? throw new InvalidOperationException("The reviewed context was not bound."), result)).ConfigureAwait(false);
            destination.Complete(receipt.Message.Outcome == ModelTurnOutcome.Succeeded
                ? HostOperationOutcome.Completed : receipt.Message.Outcome == ModelTurnOutcome.Cancelled
                    ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            return receipt;
        }
        finally
        {
            await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public Task JoinAsync() => execution.WaitForQuiescenceAsync(CancellationToken.None);
}
