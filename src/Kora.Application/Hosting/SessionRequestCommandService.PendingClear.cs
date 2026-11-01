using System.Diagnostics;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed partial class SessionRequestCommandService
{
    private ConversationPendingClearReview? pendingClear;

    private void RetirePendingClear()
    {
        ConversationPendingClearReview? review;
        lock (gate) { review = pendingClear; pendingClear = null; }
        if (review is not null) { conversation.DiscardPendingClearReview(review); }
    }

    private async Task<SessionCommandResult> ExecutePendingClearAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        var session = new HostId<SessionIdentity>(command.SessionId
            ?? throw new InvalidOperationException("Explicit ordinary conversation identity is required."));
        ActivityLink[] links = HostActivity.Current?.Activity is { } source ? [new(source.Context)] : [];
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request, links);
        try
        {
            var result = await ExecutePendingClearCoreAsync(command, session, origin, admitted, token).ConfigureAwait(false);
            activity.Complete(result.PendingClearResult is { Outcome: not "reviewed-pending-cancelled" }
                ? HostOperationOutcome.Unknown : HostOperationOutcome.Completed);
            return result;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private async Task<SessionCommandResult> ExecutePendingClearCoreAsync(SessionCommand command, HostId<SessionIdentity> session, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        ConversationPendingClearReview? previous;
        long attempt;
        lock (gate) { pending = null; previous = pendingClear; pendingClear = null; attempt = ++epoch; }
        if (command.Operation == SessionCommandOperation.PendingClearReview)
        {
            if (previous is not null) { conversation.DiscardPendingClearReview(previous); }
            var target = await workspace.ReadExactMetadataAsync(session, token).ConfigureAwait(false);
            var review = await conversation.ReviewPendingClearAsync(target, origin, admitted, token).ConfigureAwait(false);
            lock (gate)
            {
                if (epoch != attempt || !admitted())
                {
                    conversation.DiscardPendingClearReview(review);
                    throw new InvalidOperationException("Original pending-clear input changed during review.");
                }
                pendingClear = review;
            }
            return new("review", SessionConversationService.PendingClearImpact
                + " Confirm separately: session pending confirm " + session.Value.ToString("D") + " " + review.Snapshot.Id.ToString("D")
                + " | Discard: session pending cancel " + session.Value.ToString("D") + " " + review.Snapshot.Id.ToString("D"))
            { PendingClearReview = review.Snapshot };
        }
        if (previous is null || previous.Snapshot.Id != command.PendingClearReviewId || previous.Snapshot.Session != session
            || previous.Snapshot.Origin != origin)
        {
            if (previous is not null) { conversation.DiscardPendingClearReview(previous); }
            throw new InvalidOperationException("No matching original-channel pending-only review. Review the current exact conversation again.");
        }
        if (command.Operation == SessionCommandOperation.PendingClearCancel)
        {
            conversation.DiscardPendingClearReview(previous);
            return new("review-discarded", "Pending-only review discarded. No cancellation, dispatch hold, history or running-work change.");
        }
        var result = await conversation.ConfirmPendingClearAsync(previous, admitted, token).ConfigureAwait(false);
        return new(result.Outcome, "Per-entry joined retention/readback outcomes follow. RemainingPending is the current host lane, "
            + "not a durable all-clear when any original is unconfirmed. Running work was not cancelled; existing holds remain. No replay or rollback.")
        { PendingClearResult = result };
    }
}
