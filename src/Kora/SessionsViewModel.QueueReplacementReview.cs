using System.Diagnostics;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal Func<bool> CaptureReplacementNavigationEligibility()
    {
        var navigation = CaptureNavigationEligibility();
        var buffer = Interlocked.Read(ref composerBufferRevision);
        var source = workSnapshot;
        var queue = queueSnapshot;
        var revision = access.ControlRevision;
        var draft = savedConversationDraft;
        var draftRevision = savedConversationDraftControlRevision;
        return () => navigation() && access.CanControl && revision == access.ControlRevision
            && buffer == Interlocked.Read(ref composerBufferRevision)
            && ReferenceEquals(draft, savedConversationDraft) && draftRevision == savedConversationDraftControlRevision
            && ReferenceEquals(source, workSnapshot) && ReferenceEquals(queue, queueSnapshot);
    }

    internal async Task<bool> PrepareExactReplacementReviewAsync(SessionQueueReplacementReviewCommand command,
        RequestOrigin origin, Func<bool> admitted, Func<bool> nativeAdmission, CancellationToken token)
    {
        if (command.Error is not null || command.IsHelp || command.SessionId == Guid.Empty || command.TaskId == Guid.Empty
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !CanRead || refreshingWork || draftRecoveryCancellation is not null || HasReplacementCompetingReview())
        { throw new InvalidOperationException("Finish the existing native operation or review first; exact replacement preparation is unavailable."); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        ActivityLink[] causes = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), new(command.SessionId), new(Guid.NewGuid()), origin),
            HostActivityLayer.Desktop, HostOperation.Presentation, causes);
        var epoch = selectionEpoch;
        var revision = access.ControlRevision;
        var buffer = Interlocked.Read(ref composerBufferRevision);
        var source = workSnapshot;
        var originalQueue = queueSnapshot;
        bool Live() => !closed && !cancellation.IsCancellationRequested && access.CanInspect && access.CanControl
            && access.ControlRevision == revision && admitted() && nativeAdmission()
            && selectionEpoch == epoch && buffer == Interlocked.Read(ref composerBufferRevision)
            && ReferenceEquals(source, workSnapshot) && ReferenceEquals(originalQueue, queueSnapshot)
            && !HasReplacementCompetingReview();
        busy = true;
        Notify();
        try
        {
            if (!Live()) { throw new OperationCanceledException("Exact replacement navigation revoked."); }
            var snapshot = await service.ReadWorkAsync(new(command.SessionId), cancellation.Token);
            if (!Live()) { throw new OperationCanceledException("Exact replacement observation revoked."); }
            snapshot.RequireSubject(new(command.SessionId));
            var entry = snapshot.Queue.Entries.SingleOrDefault(item => item.Request.TaskId.Value == command.TaskId)
                ?? throw new InvalidOperationException("The exact expired queue task is missing; no alternate target is used.");
            if (!snapshot.Session.Authority.IsActive || snapshot.Queue.DispatchControl is not { Held: false } control
                || entry.State != SessionQueueState.Expired || entry.DispatchOrder != 0 || entry.RunId != control.RunId
                || entry.Generation != snapshot.Queue.Generation || entry.AdmissionRevision != revision)
            { throw new InvalidOperationException("The exact task is not eligible expired current-run pending work."); }
            if (!await ProtectComposerAsync(Live)) { activity.Complete(HostOperationOutcome.Cancelled); return false; }
            if (!Live()) { throw new OperationCanceledException("Original composer decision is no longer current."); }
            var current = await service.ReadWorkAsync(new(command.SessionId), cancellation.Token);
            if (!Live() || !SameReplacementSource(snapshot, current))
            { throw new InvalidOperationException("The exact source changed during navigation; no automatic fresh retry."); }
            if (selected != snapshot.Session) { ClearSelection(); }
            sessionFilter = SessionListFilter.All;
            sessions = new([snapshot.Session], null);
            selected = snapshot.Session;
            nameDraft = snapshot.Session.Metadata?.Name.Value ?? string.Empty;
            historySessionId = command.SessionId.ToString("D");
            ApplyWork(snapshot, revision);
            selectedQueueEntry = entry;
            selectedWork = workRecords.Single(row => row.Queue?.Entry.Request.TaskId == entry.Request.TaskId);
            selectedTask = null;
            inspectedTask = null;
            selectionEpoch++;
            var reviewEpoch = selectionEpoch;
            var reviewBuffer = Interlocked.Read(ref composerBufferRevision);
            bool ReviewLive() => !closed && !token.IsCancellationRequested && !lifetime.IsCancellationRequested && access.ControlRevision == revision
                && admitted() && nativeAdmission() && selectionEpoch == reviewEpoch
                && reviewBuffer == Interlocked.Read(ref composerBufferRevision)
                && ReferenceEquals(workSnapshot, snapshot) && ReferenceEquals(queueSnapshot, snapshot.Queue)
                && selectedQueueEntry == entry;
            busy = false;
            Notify();
            await ReviewExpiredRequeueAsync(ReviewLive);
            var displayed = CanConfirmExpiredRequeue && queueRequeueReview?.Original.Request.TaskId == entry.Request.TaskId;
            activity.Complete(displayed ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            return displayed;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally { busy = false; Notify(); }
    }

    private bool HasReplacementCompetingReview() => queueRequeueReview is not null || queueCancelReview is not null
        || queueMoveReview is not null || dispositionPreview is not null || retentionReview is not null
        || diagnosticsPlanReview is not null || providerProposal is not null || memoryBatchReview is not null
        || draftRecoveryReview is not null || composerComparison is not null;

    private static bool SameReplacementSource(SessionWorkSnapshot first, SessionWorkSnapshot second) =>
        first.Session == second.Session && first.AuthorityRevision == second.AuthorityRevision
        && first.PendingCapacity == second.PendingCapacity && first.ExecutionSlots == second.ExecutionSlots
        && first.Queue.SessionId == second.Queue.SessionId && first.Queue.Generation == second.Queue.Generation
        && first.Queue.Revision == second.Queue.Revision && first.Queue.DispatchControl == second.Queue.DispatchControl
        && first.Queue.Entries.SequenceEqual(second.Queue.Entries)
        && first.Tasks.SequenceEqual(second.Tasks) && first.PendingQuestions.SequenceEqual(second.PendingQuestions);
}
