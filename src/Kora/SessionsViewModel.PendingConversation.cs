using Avalonia.Threading;
using System.Text;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ConversationPendingSnapshot? pendingConversation;
    private IReadOnlyList<ConversationPendingTurn> pendingConversationTurns = [];
    private ConversationPendingTurn? selectedPendingConversation;
    private bool changingPendingConversation;
    private ConversationPendingClearReview? pendingClearReview;
    private string pendingClearText = string.Empty;
    public string PendingClearText => !closed && access.CanInspect ? pendingClearText : string.Empty;
    public bool CanReviewPendingClear => CanRefreshPendingConversation && access.CanControl && selected?.Authority.IsActive == true;
    public bool CanConfirmPendingClear => CanReviewPendingClear && pendingClearReview is not null;
    public bool CanDiscardPendingClear => !changingPendingConversation && pendingClearReview is not null;
    public IReadOnlyList<ConversationPendingTurn> PendingConversationTurns => pendingConversation is { } snapshot
        && !closed && access.CanInspect && snapshot.Session == selected?.Authority.SessionId
        ? pendingConversationTurns : [];
    public ConversationPendingTurn? SelectedPendingConversation
    {
        get => selectedPendingConversation;
        set
        {
            var exact = value is not null && PendingConversationTurns.Any(item => ReferenceEquals(item, value)) ? value : null;
            if (ReferenceEquals(exact, selectedPendingConversation)) { return; }
            selectedPendingConversation = exact;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCancelPendingConversation));
        }
    }
    public string PendingConversationStatus => pendingConversation is { } snapshot && access.CanInspect && !closed
        ? snapshot.DispatchControl is null
            ? "A preceding generation still owns submitted work. Current-generation dispatch controls are unavailable; inspect terminal recovery."
            : (snapshot.ManualHoldReason == ConversationDispatchHoldReason.OriginalUser
            ? "Manual hold: no new ordinary turn will start. A turn already dispatching or running continues unchanged. Original deadlines keep running. Release only this manual hold separately. "
            : string.Empty) + (snapshot.Paused
            ? "Automatic dispatch held after failure, cancellation or uncertainty. Resume is deliberate and revalidates original authority; it does not renew it."
            : PendingConversationTurns.Count + " submitted request(s) waiting in arrival order. Original input and provider are fixed; expired authority is not renewed.")
        : "Refresh submitted work for this exact conversation. Unsent input is not queued.";
    public bool CanRefreshPendingConversation => CanReadConversation && !changingPendingConversation;
    public bool CanCancelPendingConversation => CanRefreshPendingConversation && access.CanControl
        && selectedPendingConversation is not null && PendingConversationTurns.Contains(selectedPendingConversation);
    public bool CanResumePendingConversation => CanRefreshPendingConversation && access.CanControl && pendingConversation?.Paused == true;
    public bool CanHoldConversationDispatch => CanRefreshPendingConversation && access.CanControl
        && selected?.Authority.IsActive == true && pendingConversation?.DispatchControl?.Target == selected
        && pendingConversation.ManualHoldReason == ConversationDispatchHoldReason.None;
    public bool CanReleaseConversationDispatch => CanRefreshPendingConversation && access.CanControl
        && selected?.Authority.IsActive == true && pendingConversation?.DispatchControl?.Target == selected
        && pendingConversation.ManualHoldReason == ConversationDispatchHoldReason.OriginalUser;

    private async Task SubmitOrdinaryConversationAsync(bool localAnswer)
    {
        if (!CanSubmitConversation) { return; }
        var target = RequireSelected();
        var body = composerBody;
        var buffer = composerBufferRevision;
        var control = access.ControlRevision;
        var epoch = selectionEpoch;
        var token = lifetime.Token;
        var sourceAccepted = false;
        bool SourceLive() => !closed && access.CanInspect && access.CanControl && access.ControlRevision == control
            && (sourceAccepted || selected == target && selectionEpoch == epoch);
        bool PresentLive() => SourceLive() && selected == target && selectionEpoch == epoch;
        var released = false;
        conversationBusy = true;
        busy = true;
        Notify();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            var submitted = await conversation!.SubmitQueuedAsync(target, body, SourceLive, token, localAnswer: localAnswer);
            if (!PresentLive()) { throw new OperationCanceledException("Original private submission presentation changed."); }
            sourceAccepted = true;
            if (composerBufferRevision == buffer && string.Equals(composerBody, body, StringComparison.Ordinal))
            { composerBody = string.Empty; }
            RememberUnsent();
            if (PublishCompletedConversationMessage(submitted.Receipt.Message))
            {
                conversationStatus = submitted.Receipt.Message.Outcome is not null
                    ? TerminalConversationStatus(submitted.Receipt)
                    : submitted.Position > 0
                        ? "Submitted and queued at position " + submitted.Position + ". Original request retained; no provider execution yet."
                        : "Original request accepted. Running or waiting for admitted resources; another ordinary request may now be submitted.";
            }
            conversationBusy = false;
            busy = false;
            released = true;
            Notify();
            if (submitted.Position == 0)
            {
                var completed = (await submitted.Completion.WaitAsync(token)).RequireReceipt();
                if (PresentLive() && PublishCompletedConversationMessage(completed.Message))
                {
                    conversationStatus = TerminalConversationStatus(completed);
                }
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (PresentLive())
            {
                conversationHeld |= exception is ConversationSubmissionUnconfirmedException;
                conversationStatus = "Submission unconfirmed: " + exception.Message
                    + " Unsent input is preserved unless real acceptance was confirmed. Inspect exact retained requests; never retry automatically.";
            }
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            if (!released) { conversationBusy = false; busy = false; }
            Notify();
        }
    }

    private static string TerminalConversationStatus(ConversationSubmissionReceipt receipt) =>
        "Original user message retained exactly | HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
        + " | task " + receipt.Task.State
        + (receipt.Message.AssistantResponse is null ? ". No assistant answer retained." : ". Genuine ASSISTANT answer retained separately.")
        + " No fallback or automatic retry. Refresh for the bounded conversation.";

    private void PresentSubmittedConversation(SessionWorkspaceEntry target, ConversationTurnCompletion completion)
    {
        var control = access.ControlRevision;
        var epoch = selectionEpoch;
        var source = selected;
        void Present()
        {
            if (closed || !access.CanInspect || access.ControlRevision != control || selectionEpoch != epoch
                || selected != source || source != target) { return; }
            if (completion.Receipt is { } receipt)
            {
                if (conversationPage is not null || !PublishCompletedConversationMessage(receipt.Message)) { return; }
                conversationStatus = TerminalConversationStatus(receipt);
            }
            else
            {
                conversationHeld = true;
                conversationStatus = "Submitted work has an unconfirmed terminal outcome. Inspect this conversation's exact task receipts; no answer, rollback or retry is inferred.";
            }
            NotifyConversation();
        }
        if (Dispatcher.UIThread.CheckAccess()) { Present(); }
        else { conversationDispatcher.Post(Present); }
    }

    private void PresentPendingConversation(ConversationPendingSnapshot snapshot)
    {
        var control = access.ControlRevision;
        var epoch = selectionEpoch;
        var source = selected;
        void Present()
        {
            if (closed || !access.CanInspect || access.ControlRevision != control || snapshot.ControlRevision != control
                || selectionEpoch != epoch || selected != source || snapshot.Session != source?.Authority.SessionId
                || snapshot.Session != selected?.Authority.SessionId
                || pendingConversation is { } prior && prior.Session == snapshot.Session && prior.Revision > snapshot.Revision) { return; }
            pendingConversation = snapshot;
            pendingConversationTurns = [.. snapshot.Turns.Where(item => item.Phase == ConversationPendingPhase.Pending
                && item.Submission.Message.Generation == selected.Authority.Generation)];
            selectedPendingConversation = null;
            NotifyPendingConversation();
        }
        if (Dispatcher.UIThread.CheckAccess()) { Present(); }
        else { conversationDispatcher.Post(Present); }
    }

    public Task RefreshPendingConversationAsync() => ChangePendingConversationAsync(async (target, live) =>
    {
        var snapshot = await conversation!.ReadPendingConversationAsync(target, live, lifetime.Token);
        if (live()) { PresentPendingConversation(snapshot); }
    });
    public Task CancelPendingConversationAsync() => ChangePendingConversationAsync(async (target, live) =>
    {
        if (pendingConversation is not { } snapshot || selectedPendingConversation is not { } exact)
        { throw new InvalidOperationException("Select an actual submitted pending request before cancelling."); }
        await conversation!.CancelPendingConversationAsync(target, exact, snapshot.Revision, live, lifetime.Token);
    });
    public Task ResumePendingConversationAsync() => ChangePendingConversationAsync(async (target, live) =>
    {
        if (pendingConversation is not { } snapshot) { throw new InvalidOperationException("Refresh submitted work before resuming."); }
        await conversation!.ResumePendingConversationAsync(target, snapshot.Revision, live, lifetime.Token);
    });
    public Task HoldConversationDispatchAsync() => ChangeManualDispatchAsync(ConversationDispatchHoldReason.OriginalUser);
    public Task ReleaseConversationDispatchAsync() => ChangeManualDispatchAsync(ConversationDispatchHoldReason.None);

    private Task ChangeManualDispatchAsync(ConversationDispatchHoldReason reason) => ChangePendingConversationAsync(async (target, live) =>
    {
        var exact = pendingConversation?.DispatchControl;
        if (exact is null || exact.Target != target)
        { throw new InvalidOperationException("Refresh this exact conversation before changing its manual hold."); }
        var snapshot = await conversation!.SetManualDispatchHoldAsync(exact, reason, live, lifetime.Token);
        if (live()) { PresentPendingConversation(snapshot); }
    });

    public Task ReviewPendingClearAsync() => ChangePendingConversationAsync(async (target, live) =>
    {
        DiscardPendingClear();
        var composer = composerBufferRevision;
        var review = await conversation!.ReviewPendingClearAsync(target, RequestOrigin.LocalUi,
            () => live() && composer == composerBufferRevision, lifetime.Token);
        if (!live()) { conversation.DiscardPendingClearReview(review); return; }
        pendingClearReview = review;
        pendingClearText = Encoding.UTF8.GetString(SessionCommandResult.Serialize(
            new("review", SessionConversationService.PendingClearImpact) { PendingClearReview = review.Snapshot }));
    });

    public Task ConfirmPendingClearAsync() => ChangePendingConversationAsync(async (_, live) =>
    {
        var review = pendingClearReview ?? throw new InvalidOperationException("Review ALL current pending requests before confirming Clear pending only.");
        pendingClearReview = null;
        var result = await conversation!.ConfirmPendingClearAsync(review, live, lifetime.Token);
        if (live())
        {
            pendingClearText = Encoding.UTF8.GetString(SessionCommandResult.Serialize(
                new(result.Outcome, "Actual per-entry retention/readback outcomes. RemainingPending describes the host lane, not durable all-clear for unconfirmed originals.")
                { PendingClearResult = result }));
        }
    });

    public void DiscardPendingClear()
    {
        if (pendingClearReview is { } review) { conversation!.DiscardPendingClearReview(review); }
        pendingClearReview = null;
        pendingClearText = string.Empty;
        NotifyPendingConversation();
    }

    private async Task ChangePendingConversationAsync(Func<SessionWorkspaceEntry, Func<bool>, Task> operation)
    {
        if (!CanRefreshPendingConversation) { return; }
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        bool Live() => !closed && access.CanInspect && access.ControlRevision == control && selected == target && selectionEpoch == epoch;
        changingPendingConversation = true;
        NotifyPendingConversation();
        try { await operation(target, Live); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (Live()) { conversationStatus = "Submitted-work control unconfirmed: " + exception.Message + " Refresh exact work; nothing was retried."; }
        }
        finally { changingPendingConversation = false; NotifyConversation(); }
    }

    private void NotifyPendingConversation()
    {
        foreach (var name in new[] { nameof(PendingConversationTurns), nameof(SelectedPendingConversation), nameof(PendingConversationStatus),
            nameof(CanRefreshPendingConversation), nameof(CanCancelPendingConversation), nameof(CanResumePendingConversation),
            nameof(PendingClearText), nameof(CanReviewPendingClear), nameof(CanConfirmPendingClear), nameof(CanDiscardPendingClear),
            nameof(CanHoldConversationDispatch), nameof(CanReleaseConversationDispatch) })
        { OnPropertyChanged(name); }
    }
}
