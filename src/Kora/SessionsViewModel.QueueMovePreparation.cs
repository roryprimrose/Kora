using System.Diagnostics;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private Func<bool> queueMoveAdmission = static () => true;

    internal Func<bool> CaptureQueueMoveNavigationEligibility()
    {
        var epoch = selectionEpoch;
        var buffer = Volatile.Read(ref composerBufferRevision);
        var snapshot = queueSnapshot;
        return () => !closed && selectionEpoch == epoch && buffer == Volatile.Read(ref composerBufferRevision)
            && ReferenceEquals(queueSnapshot, snapshot);
    }

    internal async Task<bool> PrepareQueueMoveReviewAsync(SessionQueueMoveReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, Func<bool> nativeAdmission, CancellationToken token)
    {
        if (!CanRead || refreshingWork || changingQueueControl || draftRecoveryCancellation is not null)
        { throw new InvalidOperationException("Finish the current native operation before requesting an exact move review."); }
        var epoch = selectionEpoch;
        var revision = access.ControlRevision;
        var buffer = Volatile.Read(ref composerBufferRevision);
        var sourceSnapshot = queueSnapshot;
        bool SourceCurrent() => !closed && !token.IsCancellationRequested && admitted() && nativeAdmission() && access.CanControl && access.CanInspect
            && access.ControlRevision == revision && selectionEpoch == epoch
            && buffer == Volatile.Read(ref composerBufferRevision) && ReferenceEquals(sourceSnapshot, queueSnapshot);
        ActivityLink[] causes = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), new(command.SessionId), new(Guid.NewGuid()), origin),
            HostActivityLayer.Desktop, HostOperation.Request, causes);
        busy = true;
        Notify();
        try
        {
            queueMoveReview = null;
            if (!SourceCurrent()) { throw new OperationCanceledException("Original native source changed."); }
            if (!await ProtectComposerAsync())
            {
                status = "Queue move review not prepared. The original composer decision was cancelled; no pair or order changed.";
                activity.Complete(HostOperationOutcome.Cancelled);
                return false;
            }
            if (!SourceCurrent()) { throw new OperationCanceledException("Original native source changed during the draft decision."); }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            var prepared = await service.PrepareNativeMoveReviewAsync(command, origin, SourceCurrent, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!prepared.IsCurrent()) { throw new OperationCanceledException("Exact move review publication retired."); }
            LeaveListSearch();
            ClearSelection();
            selected = prepared.Work.Session;
            sessions = new([selected], null);
            sessionFilter = SessionListFilter.All;
            nameDraft = selected.Metadata?.Name.Value ?? string.Empty;
            historySessionId = command.SessionId.ToString("D");
            ApplyWork(prepared.Work, revision);
            selectedQueueEntry = prepared.Review.Selected;
            queueMoveReview = prepared.Review;
            queueMoveEpoch = selectionEpoch;
            // Selection changes above are this admitted navigation, not permission to accept later changes.
            epoch = selectionEpoch;
            sourceSnapshot = queueSnapshot;
            queueMoveAdmission = prepared.IsCurrent;
            status = "Exact pair prepared only. Review complete current/proposed order; confirmation is a separate native action.";
            OnPropertyChanged(nameof(SessionFilter));
            activity.Complete(HostOperationOutcome.Completed);
            return true;
        }
        catch (OperationCanceledException)
        {
            queueMoveReview = null;
            status = "Queue move preparation cancelled/stale. Request a fresh exact pair; no order or draft mutation was requested.";
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            queueMoveReview = null;
            Failure(logger, exception.GetType().Name);
            status = "Queue move preparation unavailable. Inspect the exact current source and request a fresh command; no alternate neighbor or retry.";
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally
        {
            busy = false;
            Notify();
            if (closed) { lifetime.Dispose(); }
        }
    }
}
