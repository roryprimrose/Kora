using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionQueueService
{
    private readonly Dictionary<HostId<SessionIdentity>, (SessionQueueMoveReview Review, RequestOrigin Origin)> moveReviews = [];

    private async Task<SessionQueueMoveReview> ReviewMoveAsync(SessionCommand command, HostId<SessionIdentity> session,
        long admissionRevision, Func<bool> eligible, CancellationToken token)
    {
        var snapshot = await store.ReadQueueAsync(session, token).ConfigureAwait(false);
        RequireEligible(eligible);
        if (snapshot.SessionId != session || snapshot.Generation.Value != command.Generation || snapshot.Revision != command.QueueRevision
            || snapshot.DispatchControl is not { } control || control.RunId != command.QueueRunId
            || control.Revision != command.QueueControlRevision || control.Held)
        {
            throw new InvalidOperationException("Refresh the exact current-run queue, generation and pause receipt before reviewing a move.");
        }
        var selected = snapshot.Entries.SingleOrDefault(entry => entry.Request.TaskId == RequireTask(command));
        var neighbor = snapshot.Entries.SingleOrDefault(entry => entry.Request.TaskId.Value == command.QueueNeighborId);
        if (selected is null || neighbor is null || selected.Revision.Value != command.TaskRevision
            || neighbor.Revision.Value != command.QueueNeighborRevision || selected == neighbor)
        {
            throw new InvalidOperationException("Select the exact displayed pending pair and both entry revisions.");
        }
        var now = time.GetUtcNow();
        var up = command.Operation == SessionCommandOperation.QueueMoveUp;
        var refusal = SessionQueueOrdering.Refusal(SessionQueueOrdering.Pending(snapshot.Entries, session, snapshot.Orders),
            selected, neighbor, up, control.RunId, snapshot.Generation, admissionRevision, now);
        if (refusal is not null) { throw new InvalidOperationException(refusal); }
        token.ThrowIfCancellationRequested();
        RequireEligible(eligible);
        return new(Guid.NewGuid(), snapshot, selected, neighbor, up, admissionRevision, now);
    }

    private SessionQueueMoveReview ConsumeMoveReview(SessionCommand command, HostId<SessionIdentity> session,
        long admissionRevision, RequestOrigin origin)
    {
        lock (causeGate)
        {
            if (!moveReviews.TryGetValue(session, out var pending) || pending.Origin != origin
                || pending.Review.Id != command.QueueMoveReviewId || pending.Review.AdmissionRevision != admissionRevision
                || pending.Review.Queue.Generation.Value != command.Generation || pending.Review.Queue.Revision != command.QueueRevision
                || pending.Review.Queue.DispatchControl!.RunId != command.QueueRunId
                || pending.Review.Queue.DispatchControl.Revision != command.QueueControlRevision
                || pending.Review.Selected.Request.TaskId.Value != command.TaskId
                || pending.Review.Selected.Revision.Value != command.TaskRevision
                || pending.Review.Neighbor.Request.TaskId.Value != command.QueueNeighborId
                || pending.Review.Neighbor.Revision.Value != command.QueueNeighborRevision)
            {
                throw new InvalidOperationException("Exact current review confirmation required. Refresh and review again; no inferred or replayed move.");
            }
            moveReviews.Remove(session);
            return pending.Review;
        }
    }
}
