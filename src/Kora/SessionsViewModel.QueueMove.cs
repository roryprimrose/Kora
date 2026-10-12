using System.Globalization;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionQueueMoveReview? queueMoveReview;
    private long queueMoveEpoch;
    public bool CanMoveQueueUp => CanReviewQueueMove && QueueMoveRefusal(true) is null;
    public bool CanMoveQueueDown => CanReviewQueueMove && QueueMoveRefusal(false) is null;
    private bool CanReviewQueueMove => CanChangeQueuePause
        && selectedQueueEntry?.State is SessionQueueState.Pending or SessionQueueState.Expired;
    public bool CanConfirmQueueMove => CanChangeQueuePause && queueMoveReview is { } review
        && queueSnapshot?.DispatchControl?.Held == false
        && queueMoveEpoch == selectionEpoch && review.Queue.SessionId == selected?.Authority.SessionId
        && review.Queue.Generation == selected.Authority.Generation && review.Queue.Revision == queueSnapshot?.Revision
        && review.Queue.DispatchControl == queueSnapshot.DispatchControl
        && review.AdmissionRevision == access.ControlRevision;
    public bool CanCancelQueueMove => queueMoveReview is not null;
    public string QueueMoveStatus => queueMoveReview is { } review
        ? (CanConfirmQueueMove ? "Review exact adjacent move; confirmation is separate." : "Review stale/unavailable. Cancel, refresh and review again.")
            + "\nCurrent: " + Pair(review.Up ? review.Neighbor : review.Selected, review.Up ? review.Selected : review.Neighbor)
            + "\nProposed: " + Pair(review.Up ? review.Selected : review.Neighbor, review.Up ? review.Neighbor : review.Selected)
            + "\nReview " + review.Id.ToString("D") + " | queue revision " + review.Queue.Revision.ToString(CultureInfo.InvariantCulture)
            + " | original pending deadlines and dependencies unchanged; no pause/resume/dispatch or activity renewal."
        : "Move up: " + (QueueMoveRefusal(true) ?? "exact adjacent review available")
            + "\nMove down: " + (QueueMoveRefusal(false) ?? "exact adjacent review available");

    private static string Pair(SessionQueueEntry first, SessionQueueEntry second) => Describe(first) + " then " + Describe(second);
    private static string Describe(SessionQueueEntry entry) => SessionQueueReadProfiles.Describe(entry.ReadProfile, entry.Target)
        + " task " + entry.Request.TaskId.Value.ToString("D") + " revision " + entry.Revision.Value.ToString(CultureInfo.InvariantCulture)
        + " original arrival " + entry.Position.ToString(CultureInfo.InvariantCulture)
        + " enqueued " + entry.EnqueuedAt.ToString("O", CultureInfo.InvariantCulture)
        + " expires " + entry.ExpiresAt.ToString("O", CultureInfo.InvariantCulture);

    private SessionQueueEntry? MoveNeighbor(bool up)
    {
        var pending = QueueRecords.Where(entry => entry.State is SessionQueueState.Pending or SessionQueueState.Expired).ToList();
        var index = selectedQueueEntry is null ? -1 : pending.IndexOf(selectedQueueEntry);
        var next = up ? index - 1 : index + 1;
        return index >= 0 && next >= 0 && next < pending.Count ? pending[next] : null;
    }

    private string? QueueMoveRefusal(bool up)
    {
        if (queueSnapshot?.DispatchControl?.Held == true) { return "Queue admission is held/unconfirmed. Refresh exact control/recovery before reviewing a move."; }
        if (!CanReviewQueueMove) { return "Select exact current-run pending work under current private control."; }
        var neighbor = MoveNeighbor(up);
        if (neighbor is null) { return up ? "First pending entry has no previous neighbor." : "Last pending entry has no next neighbor."; }
        return SessionQueueOrdering.Refusal(QueueRecords.Where(entry => entry.State is SessionQueueState.Pending or SessionQueueState.Expired).ToArray(),
            selectedQueueEntry!, neighbor, up, queueSnapshot!.DispatchControl!.RunId, queueSnapshot.Generation,
            access.ControlRevision, workSnapshot?.ObservedAt ?? DateTimeOffset.MaxValue);
    }

    public Task ReviewQueueMoveUpAsync() => ReviewQueueMoveAsync(true);
    public Task ReviewQueueMoveDownAsync() => ReviewQueueMoveAsync(false);

    private Task ReviewQueueMoveAsync(bool up)
    {
        var epoch = selectionEpoch;
        var target = selectedQueueEntry;
        var neighbor = MoveNeighbor(up);
        var snapshot = queueSnapshot;
        var refusal = QueueMoveRefusal(up);
        return RunAsync(async () =>
        {
            if (refusal is not null) { throw new InvalidOperationException(refusal); }
            var command = new SessionCommand(up ? SessionCommandOperation.QueueMoveUp : SessionCommandOperation.QueueMoveDown,
                snapshot!.SessionId.Value, snapshot.Generation.Value)
            {
                QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId,
                QueueControlRevision = snapshot.DispatchControl.Revision, TaskId = target!.Request.TaskId.Value,
                TaskRevision = target.Revision.Value, QueueNeighborId = neighbor!.Request.TaskId.Value,
                QueueNeighborRevision = neighbor.Revision.Value,
            };
            var result = await service.ExecuteCommandAsync(command, RequestOrigin.LocalUi,
                () => !closed && access.CanControl && selectionEpoch == epoch && selected?.Authority.SessionId == snapshot.SessionId,
                lifetime.Token);
            if (!access.CanControl || selectionEpoch != epoch || selected?.Authority.SessionId != snapshot.SessionId
                || selected.Authority.Generation != snapshot.Generation)
            {
                throw new OperationCanceledException("Selection changed during adjacent-pair review.");
            }
            queueMoveReview = result.QueueMoveReview ?? throw new InvalidDataException("The exact queue move review is missing.");
            queueMoveEpoch = epoch;
            status = "Review only; queue order and input draft unchanged. Confirm exactly this pair or cancel.";
            NotifyQueueMove();
        }, dispatchControl: true);
    }

    public Task ConfirmQueueMoveAsync()
    {
        var review = queueMoveReview;
        var epoch = queueMoveEpoch;
        var admitted = CanConfirmQueueMove;
        return RunAsync(async () =>
        {
            if (!admitted || review is null) { throw new InvalidOperationException("Refresh and review the exact unchanged adjacent pair first."); }
            var command = new SessionCommand(SessionCommandOperation.QueueMoveConfirm, review.Queue.SessionId.Value, review.Queue.Generation.Value)
            {
                QueueRevision = review.Queue.Revision, QueueRunId = review.Queue.DispatchControl!.RunId,
                QueueControlRevision = review.Queue.DispatchControl.Revision, TaskId = review.Selected.Request.TaskId.Value,
                TaskRevision = review.Selected.Revision.Value, QueueNeighborId = review.Neighbor.Request.TaskId.Value,
                QueueNeighborRevision = review.Neighbor.Revision.Value, QueueMoveReviewId = review.Id,
            };
            await service.ExecuteCommandAsync(command, RequestOrigin.LocalUi,
                () => !closed && access.CanControl && selectionEpoch == epoch
                    && selected?.Authority.SessionId == review.Queue.SessionId
                    && selected.Authority.Generation == review.Queue.Generation, lifetime.Token);
            if (selectionEpoch != epoch || selected?.Authority.SessionId != review.Queue.SessionId)
            {
                throw new OperationCanceledException("Selection changed after the committed pair receipt; refresh that exact session.");
            }
            queueMoveReview = null;
            await RefreshSelectedWorkAsync();
            if (selectionEpoch != epoch || selected?.Authority.SessionId != review.Queue.SessionId)
            {
                throw new OperationCanceledException("Selection changed during the committed order observation.");
            }
            status = "Exact adjacent order committed with required audit. Original arrival/deadlines retained; dispatch and explicit pause unchanged.";
            NotifyQueueMove();
        }, dispatchControl: true);
    }

    public Task CancelQueueMoveAsync()
    {
        var review = queueMoveReview;
        var epoch = selectionEpoch;
        var subject = selected?.Authority.SessionId;
        queueMoveReview = null;
        NotifyQueueMove();
        return RunAsync(async () =>
        {
            if (review is not null)
            {
                await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueMoveCancel, review.Queue.SessionId.Value)
                    { QueueMoveReviewId = review.Id }, RequestOrigin.LocalUi,
                    () => !closed && selectionEpoch == epoch && selected?.Authority.SessionId == subject, lifetime.Token);
            }
            if (selectionEpoch == epoch && selected?.Authority.SessionId == subject)
            {
                status = "Queue move review cancelled; order and draft unchanged.";
            }
        }, dispatchControl: true);
    }

    private void NotifyQueueMove()
    {
        OnPropertyChanged(nameof(CanMoveQueueUp));
        OnPropertyChanged(nameof(CanMoveQueueDown));
        OnPropertyChanged(nameof(CanConfirmQueueMove));
        OnPropertyChanged(nameof(CanCancelQueueMove));
        OnPropertyChanged(nameof(QueueMoveStatus));
    }
}
