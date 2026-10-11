using System.Collections.Immutable;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Retained exact original members of one atomic cancellation; not replay authority.</summary>
public sealed record SessionQueueCancelReceipt(Guid ReviewId, long QueueRevision, Guid RunId, long ControlRevision,
    long AdmissionRevision, ImmutableArray<SessionQueueEntry> Entries, DateTimeOffset ObservedAt)
{
    public void Validate()
    {
        if (ReviewId == Guid.Empty || QueueRevision < 0 || RunId == Guid.Empty || ControlRevision < 0
            || AdmissionRevision < 0 || ObservedAt.Offset != TimeSpan.Zero || Entries.IsDefaultOrEmpty
            || Entries.Length > SessionQueueLimits.MaximumPendingPerSession || Entries.Any(entry => entry is null || entry.Request is null)
            || Entries.Select(entry => entry.Request.TaskId).Distinct().Count() != Entries.Length)
        {
            throw new InvalidDataException("The complete exact cancellation receipt is invalid.");
        }
        var first = Entries[0];
        foreach (var entry in Entries)
        {
            entry.Validate();
            if (!entry.IsPending || entry.RunId != RunId || entry.Request.SessionId != first.Request.SessionId
                || entry.Generation != first.Generation)
            {
                throw new InvalidDataException("Cancellation members must be same-session current-run pending entries.");
            }
        }
    }

    public static SessionQueueCancelReceipt FromReview(SessionQueueCancelReview review) => new(review.Id,
        review.Queue.Revision, review.Queue.DispatchControl!.RunId, review.Queue.DispatchControl.Revision,
        review.AdmissionRevision, review.Entries, review.ObservedAt);
}
