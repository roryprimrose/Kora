using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionQueueService
{
    private readonly Dictionary<HostId<SessionIdentity>, (SessionQueueCancelReview Review, RequestOrigin Origin)> cancelReviews = [];

    private async Task<SessionCommandResult> ReviewCancellationAsync(SessionCommand command, HostId<SessionIdentity> session,
        long admissionRevision, RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        lock (causeGate) { cancelReviews.Clear(); }
        var snapshot = await store.ReadQueueAsync(session, token).ConfigureAwait(false);
        RequireEligible(eligible);
        if (snapshot.SessionId != session || snapshot.Generation.Value != command.Generation || snapshot.Revision != command.QueueRevision
            || snapshot.DispatchControl is not { } control || control.RunId != command.QueueRunId
            || control.Revision != command.QueueControlRevision
            || control.Revision > 0 && control.ControlRevision != admissionRevision)
        {
            throw new InvalidOperationException("Refresh the exact generation, queue, run and pause receipt before reviewing selected cancellation.");
        }
        var entries = SessionQueueCancellation.Select(snapshot, command.QueueSelection);
        var preview = new SessionQueueCancelReview(Guid.NewGuid(), snapshot, entries, admissionRevision, time.GetUtcNow());
        var result = new SessionCommandResult("reviewed",
            "Review every selected pending ID in dispatch order. Cancel these pending entries only requires separate exact confirmation. "
            + "Unselected work and dependents remain; dependents of cancelled prerequisites block under existing policy, not an automatic cascade. "
            + "No dispatch, pause change, deadline/activity renewal, running-work termination or history deletion.")
        { Queue = snapshot, QueueCancelReview = preview };
        _ = SessionCommandResult.Serialize(result);
        token.ThrowIfCancellationRequested();
        RequireEligible(eligible);
        lock (causeGate) { cancelReviews[session] = (preview, origin); }
        return result;
    }

    private SessionQueueCancelReview ConsumeCancellationReview(SessionCommand command, HostId<SessionIdentity> session,
        long admissionRevision, RequestOrigin origin)
    {
        lock (causeGate)
        {
            if (!cancelReviews.Remove(session, out var pending) || pending.Origin != origin
                || pending.Review.Id != command.QueueCancelReviewId || pending.Review.AdmissionRevision != admissionRevision
                || pending.Review.Queue.Generation.Value != command.Generation || pending.Review.Queue.Revision != command.QueueRevision
                || pending.Review.Queue.DispatchControl!.RunId != command.QueueRunId
                || pending.Review.Queue.DispatchControl.Revision != command.QueueControlRevision
                || !pending.Review.Entries.Select(entry => new SessionQueueSelection(entry.Request.TaskId, entry.Revision))
                    .SequenceEqual(command.QueueSelection))
            {
                throw new InvalidOperationException("The whole exact selected-set review is stale or changed. Refresh and review again; no partial cancellation.");
            }
            return pending.Review;
        }
    }

    private void DiscardCancellationReview(SessionCommand command, HostId<SessionIdentity> session,
        long admissionRevision, RequestOrigin origin)
    {
        lock (causeGate)
        {
            if (!cancelReviews.TryGetValue(session, out var pending) || pending.Origin != origin
                || pending.Review.AdmissionRevision != admissionRevision || pending.Review.Id != command.QueueCancelReviewId)
            {
                throw new InvalidOperationException("The exact selected-set review is no longer current; no pending work changed.");
            }
            cancelReviews.Remove(session);
        }
    }
}
