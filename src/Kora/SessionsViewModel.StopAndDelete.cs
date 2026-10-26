using System.Globalization;
using System.Text;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionStopAndDeleteReview? stopAndDeleteReview;
    private Func<bool> stopAndDeleteAdmission = static () => false;
    private bool stopAndDeleteBusy;
    private int stopAndDeleteSurfaceOpen;
    private long stopAndDeleteSurfaceEpoch;
    private long stopAndDeletePresentationEpoch;
    private string stopAndDeleteStatus = "Review stops nothing. Confirmation stops only this conversation's reviewed running work, waits for owned resources, then logically deletes it.";

    public string StopAndDeleteStatus => !closed && access.CanInspect ? stopAndDeleteStatus : "Private stop-and-delete presentation cleared.";
    public bool CanReviewStopAndDelete => !closed && access.CanInspect && access.CanControl
        && Volatile.Read(ref stopAndDeleteSurfaceOpen) == 1 && selected is { Authority.IsActive: true }
        && conversation is not null && !stopAndDeleteBusy && !protectingComposer && (!busy || conversationBusy);
    public bool CanConfirmStopAndDelete => CanReviewStopAndDelete && stopAndDeleteReview is not null && stopAndDeleteAdmission();
    public bool CanCancelStopAndDelete => !closed && access.CanInspect && (stopAndDeleteReview is not null || stopAndDeleteBusy);

    internal void SetStopAndDeleteSurfaceOpen(bool open)
    {
        Volatile.Write(ref stopAndDeleteSurfaceOpen, open ? 1 : 0);
        if (!open) { CancelStopAndDeleteReview(); Interlocked.Increment(ref stopAndDeleteSurfaceEpoch); }
        NotifyStopAndDelete();
    }

    public void CancelStopAndDeleteReview()
    {
        Interlocked.Increment(ref stopAndDeletePresentationEpoch);
        if (stopAndDeleteReview is { } review) { conversation?.DiscardStopAndDeleteReview(review); }
        stopAndDeleteReview = null;
        stopAndDeleteAdmission = static () => false;
        stopAndDeleteStatus = stopAndDeleteBusy
            ? "Deletion cancelled. An accepted stop still owns its physical join; no delayed deletion will run."
            : "Review cancelled. No work stopped, no input discarded, and nothing deleted.";
        NotifyStopAndDelete();
    }

    public Task ReviewStopAndDeleteAsync() => PrepareStopAndDeleteAsync(RequestOrigin.LocalUi, static () => true, lifetime.Token);

    private async Task PrepareStopAndDeleteAsync(RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        if (!CanReviewStopAndDelete) { return; }
        CancelStopAndDeleteReview();
        var target = RequireSelected();
        var selection = selectionEpoch;
        var surface = Interlocked.Read(ref stopAndDeleteSurfaceEpoch);
        var presentation = Interlocked.Read(ref stopAndDeletePresentationEpoch);
        var buffer = Interlocked.Read(ref composerBufferRevision);
        var control = access.ControlRevision;
        bool Live() => !closed && access.CanInspect && access.CanControl && access.ControlRevision == control
            && Volatile.Read(ref stopAndDeleteSurfaceOpen) == 1
            && Interlocked.Read(ref stopAndDeleteSurfaceEpoch) == surface
            && Interlocked.Read(ref stopAndDeletePresentationEpoch) == presentation
            && selectionEpoch == selection && selected == target
            && Interlocked.Read(ref composerBufferRevision) == buffer && admitted();
        stopAndDeleteBusy = true;
        NotifyStopAndDelete();
        try
        {
            if (conversationHeld || conversationSourceHeld || Encoding.UTF8.GetByteCount(composerBody) > 32768)
            { throw new InvalidOperationException("Unconfirmed or oversized input requires exact source recovery before deletion review."); }
            var input = composerBody;
            var review = await conversation!.ReviewStopAndDeleteAsync(target, origin, Live, token);
            if (!Live() || token.IsCancellationRequested)
            {
                conversation.DiscardStopAndDeleteReview(review);
                throw new OperationCanceledException("The exact native review source changed.");
            }
            var impact = review.Disposition;
            stopAndDeleteStatus = "Conversation: " + (target.Metadata?.Name.Value ?? "Unnamed conversation")
                + (review.Phase == SessionStopAndDeletePhase.Running
                    ? "\nStop this conversation's one reviewed running request, wait for its actual owned provider/tool resources, then delete this conversation."
                    : "\nThis conversation's previously reviewed work physically stopped. This fresh review authorizes only its logical deletion; it will not stop another request.")
                + "\nUnsent/pending input shown exactly below is discarded only after confirmed deletion. Other conversations' input is unchanged."
                + "\n--- input ---\n" + (input.Length == 0 ? "(empty)" : input) + "\n--- end input ---"
                + "\nRetained messages: " + impact.ConversationMessages.ToString(CultureInfo.InvariantCulture)
                + "; legacy saved text: " + impact.SavedConversationDrafts.ToString(CultureInfo.InvariantCulture)
                + "; attachments: " + impact.Attachments.ToString(CultureInfo.InvariantCulture)
                + "; session grants: " + impact.ScopedGrants.ToString(CultureInfo.InvariantCulture)
                + ".\n" + Kora.Core.Storage.SessionDispositionPreview.Scope
                + "\nIf stopping succeeds but deletion cannot complete, work stays stopped and the conversation is not confirmed deleted. No rollback, automatic retry, secure physical erasure or remote deletion is promised."
                + "\nReview only. Cancel is the default; use the separate Confirm stop and delete action to proceed.";
            stopAndDeleteReview = review;
            stopAndDeleteAdmission = Live;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (!closed && access.CanInspect)
            { stopAndDeleteStatus = "Stop-and-delete review unavailable: " + exception.Message + " No stop or deletion requested; input unchanged."; }
        }
        finally { stopAndDeleteBusy = false; NotifyStopAndDelete(); }
    }

    internal async Task<bool> ReviewStopAndDeleteCommandAsync(SessionDispositionReviewCommand command,
        RequestOrigin origin, Func<bool> admitted, Func<bool> nativeAdmission, CancellationToken token)
    {
        if (origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("Voice-origin stop and delete is unavailable. No stop requested; an independent explicit Stop remains available."); }
        var target = selected;
        if (target is null || target.Authority.SessionId.Value != command.SessionId
            || target.Authority.Generation.Value != command.Generation
            || (target.Metadata?.Revision.Value ?? 0) != command.MetadataRevision)
        { throw new InvalidOperationException("Select and inspect the exact named conversation first. This operation never replaces the selected target."); }
        await PrepareStopAndDeleteAsync(origin, () => admitted() && nativeAdmission(), token);
        return CanConfirmStopAndDelete;
    }

    public async Task ConfirmStopAndDeleteAsync()
    {
        if (!CanConfirmStopAndDelete || stopAndDeleteReview is not { } review) { return; }
        var admitted = stopAndDeleteAdmission;
        stopAndDeleteReview = null;
        stopAndDeleteBusy = true;
        stopAndDeleteStatus = "Stopping reviewed work; waiting for actual owned resources. Conversation not deleted yet. Cancel deletion does not abandon that join.";
        NotifyStopAndDelete();
        try
        {
            var result = await conversation!.ConfirmStopAndDeleteAsync(review, admitted, lifetime.Token);
            if (closed || !access.CanInspect) { return; }
            if (result.Outcome == SessionStopAndDeleteOutcome.Deleted)
            {
                RetireUnsent(review.Disposition.Session.Authority.SessionId);
                sessions = null;
                ClearSelection();
            }
            stopAndDeleteStatus = result.Outcome switch
            {
                SessionStopAndDeleteOutcome.Deleted => "Reviewed work stopped; this conversation logically deleted. Independent audit, diagnostics, perpetual grants and other conversations are unchanged.",
                SessionStopAndDeleteOutcome.WorkStoppedNotDeleted => "Work stopped; not deleted. Deletion failed, expired, was cancelled or its source changed. Refresh and obtain a fresh review; no automatic retry or rollback.",
                SessionStopAndDeleteOutcome.StopUnconfirmed => "Stop requested; physical completion unconfirmed. Not deleted. Owned work remains held; inspect exact receipts and resource recovery. No force kill or retry.",
                SessionStopAndDeleteOutcome.DeletionUnconfirmed => "Work stopped; deletion outcome unconfirmed. Inspect authoritative receipts before any fresh review; do not retry or infer rollback.",
                _ => "Nothing deleted. No stop accepted. The review is consumed; refresh the exact source before another review.",
            };
            if (result.Recovery is { } recovery) { stopAndDeleteStatus += "\nRecovery: " + recovery; }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (!closed && access.CanInspect)
            { stopAndDeleteStatus = "Stop/deletion outcome unconfirmed. Inspect exact receipts; no success, rollback or replay is inferred."; }
        }
        finally { stopAndDeleteBusy = false; Notify(); }
    }

    private void NotifyStopAndDelete()
    {
        OnPropertyChanged(nameof(StopAndDeleteStatus));
        OnPropertyChanged(nameof(CanReviewStopAndDelete));
        OnPropertyChanged(nameof(CanConfirmStopAndDelete));
        OnPropertyChanged(nameof(CanCancelStopAndDelete));
    }
}
