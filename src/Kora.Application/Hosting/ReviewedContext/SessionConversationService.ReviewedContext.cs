using Kora.Application.Hosting.ReviewedContext;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal MixedContextWorkflow CreateMixedContextWorkflow(
        Kora.Application.Memory.SessionMemoryContext memories, ScopedInstructionUseBroker instructions) =>
        new(this, memories, instructions, time);

    internal Task ValidateMixedHistoryAsync(SessionWorkspaceEntry target,
        IReadOnlyList<ConversationHistorySelection> history, Func<bool> admitted, CancellationToken token) =>
        workspace.ValidateConversationHistoryAsync(target, history, admitted, token);

    internal async Task<ConversationSubmissionReceipt> SubmitMixedContextAsync(MixedContextReview review,
        Func<bool> admitted, Func<CancellationToken, Task> validate, CancellationToken token)
    {
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), review.Target.Authority.SessionId,
            new(Guid.NewGuid()), review.Origin), HostActivityLayer.Application, HostOperation.Request, [review.Cause]);
        try
        {
            var receipt = await SubmitReviewedContextAsync(review.Target, review.Body, review.CreateEnvelope,
                () => admitted() && review.Current, validate, [review.Cause], review.Origin, token).ConfigureAwait(false);
            activity.Complete(receipt.Message.Outcome switch
            {
                Kora.Core.Dependencies.ModelTurnOutcome.Succeeded => HostOperationOutcome.Completed,
                Kora.Core.Dependencies.ModelTurnOutcome.Cancelled => HostOperationOutcome.Cancelled,
                Kora.Core.Dependencies.ModelTurnOutcome.Unknown => HostOperationOutcome.Unknown,
                _ => HostOperationOutcome.Failed,
            });
            return receipt;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
