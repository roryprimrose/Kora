using System.Globalization;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionQueueRequeueReview? queueRequeueReview;
    private long queueRequeueEpoch;
    private Func<bool> queueRequeueAdmission = static () => true;
    public bool CanReviewExpiredRequeue => CanChangeQueuePause && queueSnapshot?.DispatchControl?.Held == false
        && selectedQueueEntry is { State: SessionQueueState.Expired, DispatchOrder: 0 } entry
        && entry.RunId == queueSnapshot.DispatchControl.RunId && entry.Generation == queueSnapshot.Generation
        && entry.AdmissionRevision == access.ControlRevision;
    public bool CanConfirmExpiredRequeue => CanChangeQueuePause && queueRequeueAdmission() && queueRequeueReview is { } review
        && queueRequeueEpoch == selectionEpoch && review.Queue.SessionId == selected?.Authority.SessionId
        && review.Queue.Generation == selected.Authority.Generation && review.Queue.Revision == queueSnapshot?.Revision
        && review.Queue.DispatchControl == queueSnapshot.DispatchControl && queueSnapshot.DispatchControl?.Held == false
        && review.AdmissionRevision == access.ControlRevision;
    public bool CanDiscardExpiredRequeue => queueRequeueReview is not null;
    public string ExpiredRequeueStatus => queueRequeueReview is { } review
        ? (CanConfirmExpiredRequeue ? "Separate explicit confirmation required." : "Review stale/unavailable; cancel, refresh and review again.")
            + "\nREMOVE (history retained): " + Describe(review.Original)
            + "\nOld request " + review.Original.Request.RequestId.Value.ToString("D")
            + "; task revision " + review.OriginalTask.Revision.Value.ToString(CultureInfo.InvariantCulture)
            + "; original private admission " + review.AdmissionRevision.ToString(CultureInfo.InvariantCulture)
            + "; retained task outcome Cancelled; retained queue outcome Removed."
            + "\nNEW same-profile tail request " + review.FreshRequest.RequestId.Value.ToString("D")
            + " task " + review.FreshRequest.TaskId.Value.ToString("D")
            + " enqueued " + review.EnqueuedAt.ToString("O", CultureInfo.InvariantCulture)
            + " expires " + review.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            + " lifetime " + review.Limits.PendingLifetimeMinutes.ToString(CultureInfo.InvariantCulture) + " minutes."
            + "\nNEW profile " + SessionQueueReadProfiles.Describe(review.Original.ReadProfile, review.Original.Target)
            + "; configuration revision " + review.ConfigurationRevision.ToString(CultureInfo.InvariantCulture)
            + "; generation " + review.Queue.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + "; queue revision " + review.Queue.Revision.ToString(CultureInfo.InvariantCulture)
            + "; run " + review.Queue.DispatchControl!.RunId.ToString("D")
            + "; pause revision " + review.Queue.DispatchControl.Revision.ToString(CultureInfo.InvariantCulture)
            + "; paused " + review.Queue.DispatchControl.Paused + "; held " + review.Queue.DispatchControl.Held
            + "; resulting pending capacity " + SessionQueueCapacity.PendingCount(review.Queue.Entries,
                review.Queue.DispatchControl.RunId).ToString(CultureInfo.InvariantCulture)
            + "/" + review.Limits.PendingPerSession.ToString(CultureInfo.InvariantCulture)
            + "; execution slots " + review.Limits.ExecutionSlots.ToString(CultureInfo.InvariantCulture)
            + "; future active budget " + review.Limits.ActiveBudgetMinutes.ToString(CultureInfo.InvariantCulture)
            + " minutes captured only at later manual dispatch."
            + "\nExact prerequisite " + (review.Original.Dependency?.Value.ToString("D") ?? "none")
            + "; existing dependents remain blocked on the old task; no retarget or cascade."
            + "\nReview " + review.Id.ToString("D") + "; no old deadline extension, replay, dispatch or pause change."
        : "Select one expired current-run pending fixed read and review removal plus a NEW same-profile decision at the tail. History retained; no automatic dispatch.";

    public Task ReviewExpiredRequeueAsync() => ReviewExpiredRequeueAsync(static () => true);

    private Task ReviewExpiredRequeueAsync(Func<bool> admission)
    {
        queueRequeueAdmission = admission;
        var epoch = selectionEpoch;
        var snapshot = queueSnapshot;
        var entry = selectedQueueEntry;
        var admitted = CanReviewExpiredRequeue;
        return RunAsync(async () =>
        {
            if (!admitted || !admission() || snapshot is null || entry is null) { throw new InvalidOperationException("Select exact expired current-run pending work under unchanged private control."); }
            var command = RequeueCommand(SessionCommandOperation.QueueRequeueExpiredReview, snapshot, entry);
            var result = await service.ExecuteCommandAsync(command, RequestOrigin.LocalUi,
                () => !closed && access.CanControl && admission() && selectionEpoch == epoch && selected?.Authority.SessionId == snapshot.SessionId, lifetime.Token);
            if (!access.CanControl || !admission() || selectionEpoch != epoch || selected?.Authority.SessionId != snapshot.SessionId)
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
                () => !closed && access.CanControl && queueRequeueAdmission() && selectionEpoch == epoch
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
