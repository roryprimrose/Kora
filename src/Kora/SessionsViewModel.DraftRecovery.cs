using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private CancellationTokenSource? draftRecoveryCancellation;
    private ConversationDraftRecoveryReview? draftRecoveryReview;
    private Func<bool>? draftRecoveryCurrent;
    private long draftRecoveryEpoch;
    private long composerBufferRevision;
    private bool draftSaveUnconfirmed;
    internal event EventHandler? DraftRecoveryRetired;

    public bool CanRecoverConversationDraft => CanCompose && draftRecoveryCancellation is null && !draftSaveUnconfirmed
        && savedConversationDraftControlRevision > 0 && access.ControlRevision == savedConversationDraftControlRevision
        && savedConversationDraft is { } observed && observed.Session == selected?.Authority;

    internal async Task ReviewConversationDraftRecoveryAsync(
        Func<ConversationDraftRecoveryReview, CancellationToken, Task<bool>> exactReview, Func<bool> surfaceCurrent)
    {
        RetireComposerComparison();
        RetireDraftRecovery();
        if (!CanRecoverConversationDraft || !surfaceCurrent())
        {
            conversationStatus = "Draft recovery unavailable. Explicitly reconcile/read the exact saved source under current private control first; an uncertain save is not confirmation.";
            NotifyConversation();
            return;
        }
        var target = RequireSelected();
        var original = savedConversationDraft!;
        var selection = selectionEpoch;
        var control = access.ControlRevision;
        var bufferRevision = Volatile.Read(ref composerBufferRevision);
        var raw = composerBody;
        var epoch = Volatile.Read(ref draftRecoveryEpoch);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        draftRecoveryCancellation = cancellation;
        bool Current() => !closed && !busy && !conversationBusy && !protectingComposer && access.CanInspect && access.CanControl
            && access.ControlRevision == control && savedConversationDraftControlRevision == control
            && !conversationHeld && !conversationSourceHeld && !draftSaveUnconfirmed && surfaceCurrent()
            && Volatile.Read(ref draftRecoveryEpoch) == epoch && selectionEpoch == selection && selected == target
            && ReferenceEquals(savedConversationDraft, original) && Volatile.Read(ref composerBufferRevision) == bufferRevision
            && !cancellation.IsCancellationRequested;
        draftRecoveryCurrent = Current;
        NotifyConversation();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), Kora.Core.Hosting.RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            var latest = await conversation!.ReviewDraftRecoveryAsync(target, original, raw, async (review, token) =>
            {
                if (!Current()) { throw new OperationCanceledException("The draft recovery source expired before review."); }
                draftRecoveryReview = review;
                return await exactReview(review, token);
            }, Current, cancellation.Token);
            if (!Current()) { throw new OperationCanceledException("The exact draft recovery lifetime expired before adoption."); }
            if (latest is null)
            {
                conversationStatus = "Draft recovery cancelled. Old saved baseline and exact raw buffer retained; nothing written or submitted.";
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            savedConversationDraft = latest;
            composerBaseline = latest.Draft.Body ?? string.Empty;
            clearDraftReviewed = false;
            conversationStatus = "Latest exact saved baseline adopted in memory only. Raw composer buffer retained exactly. Nothing saved or submitted; later Save can conflict again.";
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (Volatile.Read(ref draftRecoveryEpoch) == epoch && !closed && access.CanInspect && access.ControlRevision == control)
            {
                conversationSourceHeld |= exception is ConversationDraftRecoverySourceException;
                conversationStatus = "Draft recovery refused/cancelled: " + exception.Message
                    + " Old baseline and exact raw buffer retained; no write, submit or certainty inferred.";
            }
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            // An invalidated operation owns disposal, but never a newer operation's review.
            if (ReferenceEquals(draftRecoveryCancellation, cancellation))
            {
                RetireDraftRecovery();
                draftRecoveryCancellation = null;
            }
            NotifyConversation();
        }
    }

    internal void CancelConversationDraftRecovery() => RetireDraftRecovery();

    private void RetireDraftRecovery()
    {
        Interlocked.Increment(ref draftRecoveryEpoch);
        draftRecoveryCurrent = null;
        draftRecoveryReview?.Dispose();
        draftRecoveryReview = null;
        draftRecoveryCancellation?.Cancel();
        DraftRecoveryRetired?.Invoke(this, EventArgs.Empty);
    }

    private void CheckDraftRecovery()
    {
        if (draftRecoveryCurrent is { } current && !current()) { RetireDraftRecovery(); }
    }
}
