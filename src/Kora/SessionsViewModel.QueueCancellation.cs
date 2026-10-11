using System.Collections.Immutable;
using System.Globalization;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ImmutableArray<SessionQueueSelection> pendingCancellationSelection = [];
    private SessionQueueCancelReview? queueCancelReview;
    private long pendingCancellationEpoch;
    private long cancellationSessionEpoch;
    public IReadOnlyList<SessionQueueEntry> PendingCancellationEntries => queueSnapshot is { DispatchControl: { } control } snapshot
        ? snapshot.Entries.Where(entry => entry.RunId == control.RunId && entry.Generation == snapshot.Generation
            && entry.State is SessionQueueState.Pending or SessionQueueState.Expired).ToArray() : [];
    public bool CanSelectPendingCancellation => CanChangeQueuePause;
    public bool CanReviewPendingCancellation => CanSelectPendingCancellation && !pendingCancellationSelection.IsDefaultOrEmpty;
    public bool CanConfirmPendingCancellation => CanReviewPendingCancellation && queueCancelReview is { } review
        && cancellationSessionEpoch == selectionEpoch && review.AdmissionRevision == access.ControlRevision
        && review.Queue.SessionId == selected?.Authority.SessionId && review.Queue.Generation == selected.Authority.Generation
        && review.Queue.Revision == queueSnapshot?.Revision && review.Queue.DispatchControl == queueSnapshot.DispatchControl
        && review.Entries.Select(entry => new SessionQueueSelection(entry.Request.TaskId, entry.Revision)).SequenceEqual(pendingCancellationSelection);
    public bool CanDiscardPendingCancellation => queueCancelReview is not null;
    public string PendingCancellationStatus => queueCancelReview is { } review
        ? (CanConfirmPendingCancellation ? "Review complete selected pending subset: " : "Stale review; refresh and review the complete subset again: ")
            + review.Entries.Length.ToString(CultureInfo.InvariantCulture) + "\n"
            + string.Join("\n", review.Entries.Select((entry, index) => (index + 1).ToString(CultureInfo.InvariantCulture) + ". " + Describe(entry)
                + " | original source " + entry.Request.Origin + " | request " + entry.Request.RequestId.Value.ToString("D")))
            + "\nReview " + review.Id.ToString("D") + " | session " + review.Queue.SessionId.Value.ToString("D")
            + " | generation " + review.Queue.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | queue revision " + review.Queue.Revision.ToString(CultureInfo.InvariantCulture)
            + " | run " + review.Queue.DispatchControl!.RunId.ToString("D")
            + " | pause revision " + review.Queue.DispatchControl.Revision.ToString(CultureInfo.InvariantCulture)
            + " | paused " + review.Queue.DispatchControl.Paused + " | held " + review.Queue.DispatchControl.Held
            + " | private control epoch " + review.AdmissionRevision.ToString(CultureInfo.InvariantCulture)
            + "\nUnselected entries/dependents retained in order; cancelled prerequisites block dependents, not a cascade. "
            + "No pause/resume/dispatch, running-work termination, deadline renewal or history deletion."
        : "Selected " + pendingCancellationSelection.Length.ToString(CultureInfo.InvariantCulture)
            + " pending entries only. Review every ID before separate atomic confirmation. Current/question work selection is independent.";

    public void SelectPendingCancellationEntries(IReadOnlyList<SessionQueueEntry> entries)
    {
        if (!CanSelectPendingCancellation || entries.Count > SessionQueueLimits.MaximumPendingPerSession
            || entries.Distinct().Count() != entries.Count || entries.Any(entry => !PendingCancellationEntries.Contains(entry))) { return; }
        var selectedIds = entries.Select(entry => entry.Request.TaskId).ToHashSet();
        var next = PendingCancellationEntries.Where(entry => selectedIds.Contains(entry.Request.TaskId))
            .Select(entry => new SessionQueueSelection(entry.Request.TaskId, entry.Revision)).ToImmutableArray();
        if (next.SequenceEqual(pendingCancellationSelection)) { return; }
        pendingCancellationSelection = next;
        pendingCancellationEpoch++;
        queueCancelReview = null;
        NotifyPendingCancellation();
    }

    public Task ReviewPendingCancellationAsync()
    {
        var snapshot = queueSnapshot;
        var members = pendingCancellationSelection;
        var sessionEpoch = selectionEpoch;
        var subsetEpoch = pendingCancellationEpoch;
        return RunAsync(async () =>
        {
            if (snapshot is null || members.IsDefaultOrEmpty) { throw new InvalidOperationException("Select exact current pending IDs first."); }
            var command = CancellationCommand(SessionCommandOperation.QueueCancelSelectedReview, snapshot, members);
            var result = await service.ExecuteCommandAsync(command, RequestOrigin.LocalUi,
                () => CancellationEligible(snapshot, sessionEpoch, subsetEpoch), lifetime.Token);
            if (!CancellationEligible(snapshot, sessionEpoch, subsetEpoch)) { throw new OperationCanceledException("Pending selection changed during review."); }
            queueCancelReview = result.QueueCancelReview ?? throw new InvalidDataException("Complete selected cancellation review is missing.");
            cancellationSessionEpoch = sessionEpoch;
            status = "Review only; no write. Confirm these pending entries only or cancel review to keep selection.";
            NotifyPendingCancellation();
        }, dispatchControl: true);
    }

    public Task ConfirmPendingCancellationAsync()
    {
        var review = queueCancelReview;
        var admitted = CanConfirmPendingCancellation;
        var sessionEpoch = selectionEpoch;
        var subsetEpoch = pendingCancellationEpoch;
        return RunAsync(async () =>
        {
            if (!admitted || review is null) { throw new InvalidOperationException("Refresh and review the exact complete pending subset first."); }
            var command = CancellationCommand(SessionCommandOperation.QueueCancelSelectedConfirm, review.Queue, pendingCancellationSelection)
                with { QueueCancelReviewId = review.Id };
            var result = await service.ExecuteCommandAsync(command, RequestOrigin.LocalUi,
                () => CancellationEligible(review.Queue, sessionEpoch, subsetEpoch), lifetime.Token);
            if (!CancellationEligible(review.Queue, sessionEpoch, subsetEpoch))
            {
                throw new OperationCanceledException("Selection changed after commit; refresh that exact session for the retained cancellation receipt.");
            }
            queueCancelReview = null;
            pendingCancellationSelection = [];
            await RefreshSelectedWorkAsync();
            if (closed || selectionEpoch != sessionEpoch || selected?.Authority.SessionId != review.Queue.SessionId) { return; }
            status = result.Message;
            NotifyPendingCancellation();
        }, dispatchControl: true);
    }

    public Task DiscardPendingCancellationAsync()
    {
        var review = queueCancelReview;
        var epoch = selectionEpoch;
        queueCancelReview = null;
        NotifyPendingCancellation();
        return RunAsync(async () =>
        {
            if (review is null) { return; }
            await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueCancelSelectedDiscard, review.Queue.SessionId.Value)
                { QueueCancelReviewId = review.Id }, RequestOrigin.LocalUi,
                () => !closed && access.CanControl && selectionEpoch == epoch && selected?.Authority.SessionId == review.Queue.SessionId,
                lifetime.Token);
            if (!closed && selectionEpoch == epoch) { status = "Review cancelled; selection and input drafts kept. No pending work changed."; }
        }, dispatchControl: true);
    }

    private bool CancellationEligible(SessionQueueSnapshot snapshot, long epoch, long subsetEpoch) =>
        !closed && access.CanControl && selectionEpoch == epoch && pendingCancellationEpoch == subsetEpoch
        && selected?.Authority.SessionId == snapshot.SessionId && selected.Authority.Generation == snapshot.Generation;

    private static SessionCommand CancellationCommand(SessionCommandOperation operation, SessionQueueSnapshot snapshot,
        ImmutableArray<SessionQueueSelection> selection) => new(operation, snapshot.SessionId.Value, snapshot.Generation.Value)
    {
        QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId,
        QueueControlRevision = snapshot.DispatchControl.Revision, QueueSelection = selection,
    };

    private void NotifyPendingCancellation()
    {
        OnPropertyChanged(nameof(CanSelectPendingCancellation));
        OnPropertyChanged(nameof(CanReviewPendingCancellation));
        OnPropertyChanged(nameof(CanConfirmPendingCancellation));
        OnPropertyChanged(nameof(CanDiscardPendingCancellation));
        OnPropertyChanged(nameof(PendingCancellationStatus));
    }
}
