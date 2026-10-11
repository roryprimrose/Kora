using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Committed exact pair lineage; original entry bytes are retained as evidence, never restart authority.</summary>
public sealed record SessionQueueMoveReceipt(Guid ReviewId, long QueueRevision, Guid RunId, long ControlRevision,
    long AdmissionRevision, SessionQueueEntry Selected, SessionQueueEntry Neighbor, bool Up, DateTimeOffset ObservedAt)
{
    public void Validate()
    {
        if (ReviewId == Guid.Empty || QueueRevision < 0 || ControlRevision < 0 || AdmissionRevision < 0
            || ObservedAt.Offset != TimeSpan.Zero || Selected is null || Neighbor is null)
        {
            throw new InvalidDataException("The committed queue move receipt is incomplete.");
        }
        Selected.Validate();
        Neighbor.Validate();
        var pair = Up ? new[] { Neighbor, Selected } : new[] { Selected, Neighbor };
        if (SessionQueueOrdering.Refusal(pair, Selected, Neighbor, Up, RunId, Selected.Generation, AdmissionRevision, ObservedAt) is not null)
        {
            throw new InvalidDataException("The committed queue move receipt is not an eligible exact adjacent pair.");
        }
    }

    public static SessionQueueMoveReceipt FromReview(SessionQueueMoveReview review) => new(review.Id, review.Queue.Revision,
        review.Queue.DispatchControl!.RunId, review.Queue.DispatchControl.Revision, review.AdmissionRevision,
        review.Selected, review.Neighbor, review.Up, review.ObservedAt);
}
