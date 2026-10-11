using System.Globalization;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionQueueRequeueReview? queueRequeueReview;
    private long queueRequeueEpoch;
    public bool CanReviewExpiredRequeue => CanChangeQueuePause && queueSnapshot?.DispatchControl?.Held == false
        && selectedQueueEntry is { State: SessionQueueState.Expired, DispatchOrder: 0 } entry
        && entry.RunId == queueSnapshot.DispatchControl.RunId && entry.Generation == queueSnapshot.Generation
        && entry.AdmissionRevision == access.ControlRevision;
    public bool CanConfirmExpiredRequeue => CanChangeQueuePause && queueRequeueReview is { } review
        && queueRequeueEpoch == selectionEpoch && review.Queue.SessionId == selected?.Authority.SessionId
        && review.Queue.Generation == selected.Authority.Generation && review.Queue.Revision == queueSnapshot?.Revision
        && review.Queue.DispatchControl == queueSnapshot.DispatchControl && queueSnapshot.DispatchControl?.Held == false
        && review.AdmissionRevision == access.ControlRevision;
    public bool CanDiscardExpiredRequeue => queueRequeueReview is not null;
    public string ExpiredRequeueStatus => queueRequeueReview is { } review
        ? (CanConfirmExpiredRequeue ? "Separate explicit confirmation required." : "Review stale/unavailable; cancel, refresh and review again.")
            + "\nREMOVE (history retained): " + Describe(review.Original)
            + "\nNEW same-profile tail request " + review.FreshRequest.RequestId.Value.ToString("D")
            + " task " + review.FreshRequest.TaskId.Value.ToString("D")
            + " enqueued " + review.EnqueuedAt.ToString("O", CultureInfo.InvariantCulture)
            + " expires " + review.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            + " lifetime " + review.Limits.PendingLifetimeMinutes.ToString(CultureInfo.InvariantCulture) + " minutes."
            + "\nExact prerequisite " + (review.Original.Dependency?.Value.ToString("D") ?? "none")
            + "; existing dependents remain blocked on the old task; no retarget or cascade."
            + "\nReview " + review.Id.ToString("D") + "; no old deadline extension, replay, dispatch or pause change."
        : "Select one expired current-run pending fixed read and review removal plus a NEW same-profile decision at the tail. History retained; no automatic dispatch.";

    public Task ReviewExpiredRequeueAsync()
    {
        var epoch = selectionEpoch;
        var snapshot = queueSnapshot;
        var entry = selectedQueueEntry;
        var admitted = CanReviewExpiredRequeue;
        return RunAsync(async () =>
        {
            if (!admitted || snapshot is null || entry is null) { throw new InvalidOperationException("Select exact expired current-run pending work under unchanged private control."); }
            var command = RequeueCommand(SessionCommandOperation.QueueRequeueExpiredReview, snapshot, entry);
            var result = await service.ExecuteCommandAsync(command, RequestOrigin.LocalUi,
                () => !closed && access.CanControl && selectionEpoch == epoch && selected?.Authority.SessionId == snapshot.SessionId, lifetime.Token);
            if (!access.CanControl || selectionEpoch != epoch || selected?.Authority.SessionId != snapshot.SessionId)
            {
                throw new OperationCanceledException("Selection changed during expired replacement review.");
            }
            queueRequeueReview = result.QueueRequeueReview ?? throw new InvalidDataException("The exact expired replacement review is missing.");
            queueRequeueEpoch = epoch;
            status = "Review only; old source, order, drafts and activity unchanged.";
            NotifyExpiredRequeue();
        }, dispatchControl: true);
    }

    public Task ConfirmExpiredRequeueAsync()
    {
        var review = queueRequeueReview;
        var epoch = queueRequeueEpoch;
        var admitted = CanConfirmExpiredRequeue;
        return RunAsync(async () =>
        {
            if (!admitted || review is null) { throw new InvalidOperationException("Review exact expired removal and fresh tail decision first."); }
            var command = RequeueCommand(SessionCommandOperation.QueueRequeueExpiredConfirm, review.Queue, review.Original) with
            {
                FreshRequestId = review.FreshRequest.RequestId.Value, FreshTaskId = review.FreshRequest.TaskId.Value,
                QueueRequeueReviewId = review.Id,
            };
            var result = await service.ExecuteCommandAsync(command, RequestOrigin.LocalUi,
                () => !closed && access.CanControl && selectionEpoch == epoch
                    && selected?.Authority.SessionId == review.Queue.SessionId && selected.Authority.Generation == review.Queue.Generation, lifetime.Token);
            if (!access.CanControl || selectionEpoch != epoch || selected?.Authority.SessionId != review.Queue.SessionId)
            {
                throw new OperationCanceledException("Selection changed after commit; refresh that exact session for the durable receipt.");
            }
            _ = result.QueueRequeueReceipt ?? throw new InvalidDataException("The committed replacement lineage is missing.");
            queueRequeueReview = null;
            await RefreshSelectedWorkAsync();
            if (selectionEpoch != epoch || selected?.Authority.SessionId != review.Queue.SessionId)
            {
                throw new OperationCanceledException("Selection changed during committed replacement observation.");
            }
            status = "Old expired source Removed, history retained; new reviewed same-profile task Pending at tail. Dispatch remains manual; dependencies and pause unchanged.";
            NotifyExpiredRequeue();
        }, dispatchControl: true);
    }

    public Task DiscardExpiredRequeueAsync()
    {
        var review = queueRequeueReview;
        var epoch = selectionEpoch;
        queueRequeueReview = null;
        NotifyExpiredRequeue();
        return RunAsync(async () =>
        {
            if (review is not null)
            {
                await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueRequeueExpiredDiscard, review.Queue.SessionId.Value)
                    { QueueRequeueReviewId = review.Id }, RequestOrigin.LocalUi,
                    () => !closed && access.CanControl && selectionEpoch == epoch && selected?.Authority.SessionId == review.Queue.SessionId, lifetime.Token);
            }
            if (selectionEpoch == epoch) { status = "Replacement review cancelled; no source removal or new admission. Drafts unchanged."; }
        }, dispatchControl: true);
    }

    private static SessionCommand RequeueCommand(SessionCommandOperation operation, SessionQueueSnapshot snapshot, SessionQueueEntry entry) =>
        new(operation, snapshot.SessionId.Value, snapshot.Generation.Value)
        {
            QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId,
            QueueControlRevision = snapshot.DispatchControl.Revision, TaskId = entry.Request.TaskId.Value,
            TaskRevision = entry.Revision.Value,
        };

    private void NotifyExpiredRequeue()
    {
        OnPropertyChanged(nameof(CanReviewExpiredRequeue));
        OnPropertyChanged(nameof(CanConfirmExpiredRequeue));
        OnPropertyChanged(nameof(CanDiscardExpiredRequeue));
        OnPropertyChanged(nameof(ExpiredRequeueStatus));
    }
}
