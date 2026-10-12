using System.Globalization;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string composerBody = string.Empty;
    private string composerBaseline = string.Empty;
    private ConversationDraftSnapshot? savedConversationDraft;
    private ConversationPage? conversationPage;
    private bool conversationHeld;
    private bool conversationSourceHeld;
    private bool conversationBusy;
    private bool clearDraftReviewed;
    private string conversationStatus = "Select an exact session. Drafts stay in memory unless explicitly saved.";
    internal Func<Task<ComposerDraftDecision>>? DecideComposerDraft { get; set; }
    internal void ReportComposerCloseFailure()
    {
        conversationStatus = "Draft save/close is unconfirmed; the memory buffer remains open. Inspect saved revision before another decision.";
        NotifyConversation();
    }
    public bool ComposerDirty => !string.Equals(composerBody, composerBaseline, StringComparison.Ordinal);
    public string ComposerBody
    {
        get => composerBody;
        set
        {
            if (closed || !access.CanInspect || conversationBusy) { return; }
            composerBody = value;
            clearDraftReviewed = false;
            NotifyConversation();
        }
    }
    public string ConversationStatus => conversationStatus;
    public string SavedConversationDraftReview => clearDraftReviewed ? savedConversationDraft?.Draft.Body ?? string.Empty : string.Empty;
    public string ComposerTarget => selected is { } target
        ? "Message exact session " + target.Authority.SessionId.Value.ToString("D")
            + " | name: " + (target.Metadata?.Name.Value ?? "Unnamed")
            + " | generation " + target.Authority.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | metadata revision " + (target.Metadata?.Revision.Value ?? 0).ToString(CultureInfo.InvariantCulture)
            + " | private control epoch " + access.ControlRevision.ToString(CultureInfo.InvariantCulture)
            + " | saved draft revision " + (savedConversationDraft?.Draft.Revision ?? 0).ToString(CultureInfo.InvariantCulture)
        : "Select an exact session; no global or question-reply target.";
    public IReadOnlyList<ConversationMessage> ConversationMessages => conversationPage?.Messages ?? [];
    public bool CanReadConversation => CanRead && !conversationBusy && selected is not null && conversation is not null;
    public bool CanNextConversation => CanReadConversation && conversationPage?.Next is not null;
    public bool CanCompose => CanReadConversation && access.CanControl && selected?.Authority.IsActive == true
        && !conversationHeld && !conversationSourceHeld;
    public bool CanSaveConversationDraft => CanCompose && savedConversationDraft is not null;
    public bool CanReloadConversationDraft => CanReadConversation && savedConversationDraft?.Draft.Body is not null;
    public bool CanReviewClearConversationDraft => CanSaveConversationDraft && savedConversationDraft?.Draft.Body is not null;
    public bool CanClearConversationDraft => CanReviewClearConversationDraft && clearDraftReviewed;
    public bool CanSubmitConversation => CanCompose && !string.IsNullOrWhiteSpace(composerBody);
    public void DiscardComposerBuffer()
    {
        if (!CanReadConversation) { return; }
        composerBody = string.Empty;
        composerBaseline = string.Empty;
        clearDraftReviewed = false;
        conversationStatus = "Memory-only composer buffer explicitly discarded. Saved draft and immutable submitted messages are unchanged.";
        NotifyConversation();
    }

    public Task ReadConversationAsync(bool next = false) => RunConversationAsync(async (target, live) =>
    {
        var page = await conversation!.ReadPageAsync(target.Authority.SessionId,
            next ? conversationPage?.Next ?? throw new InvalidOperationException("No conversation continuation; refresh the exact source.") : null,
            25, lifetime.Token);
        var draft = await conversation.ReadDraftAsync(target.Authority.SessionId, lifetime.Token);
        if (!live() || page.Session != target.Authority || draft.Session != target.Authority)
        { throw new OperationCanceledException("Conversation source or exact selected generation changed."); }
        conversationPage = page;
        savedConversationDraft = draft;
        conversationSourceHeld = false;
        clearDraftReviewed = false;
        conversationStatus = "Observed " + page.Messages.Length.ToString(CultureInfo.InvariantCulture)
            + " exact immutable user messages. Host status is not an assistant answer. "
            + (page.Next is null ? "End of this sequence snapshot." : "More bounded records available.")
            + (conversationHeld ? " Submission uncertainty remains held; inspect task receipts/restart recovery, never retry automatically." : string.Empty);
    }, sourceRead: true);

    public Task SaveConversationDraftAsync() => RunConversationAsync(async (target, live) =>
    {
        var expected = savedConversationDraft ?? await conversation!.ReadDraftAsync(target.Authority.SessionId, lifetime.Token);
        var body = composerBody;
        var saved = await conversation!.SaveDraftAsync(expected, body, live, lifetime.Token);
        if (!live() || saved.Session != target.Authority || !string.Equals(saved.Draft.Body, body, StringComparison.Ordinal))
        { throw new IOException("Saved draft receipt is unconfirmed; inspect exact durable revision."); }
        savedConversationDraft = saved;
        conversationSourceHeld = false;
        composerBaseline = body;
        clearDraftReviewed = false;
        conversationStatus = "Exact draft explicitly saved at revision " + saved.Draft.Revision.ToString(CultureInfo.InvariantCulture)
            + ". Not a submitted message, task dispatch, approval or retention heartbeat.";
    });

    public Task ReloadConversationDraftAsync() => RunConversationAsync(async (target, live) =>
    {
        if (ComposerDirty && !await ProtectComposerAsync())
        { return; }
        var saved = await conversation!.ReadDraftAsync(target.Authority.SessionId, lifetime.Token);
        if (!live() || saved.Session != target.Authority) { throw new OperationCanceledException("The draft target changed."); }
        savedConversationDraft = saved;
        conversationSourceHeld = false;
        composerBody = saved.Draft.Body ?? string.Empty;
        composerBaseline = composerBody;
        clearDraftReviewed = false;
        conversationStatus = "Exact saved draft reloaded as data only. A new explicit Submit is required under current admission.";
    }, sourceRead: true);

    public void ReviewClearConversationDraft()
    {
        if (!CanReviewClearConversationDraft) { return; }
        clearDraftReviewed = true;
        conversationStatus = "Review clear of saved body for " + ComposerTarget
            + ". Only this exact saved revision will be removed; the memory buffer and submitted messages stay unchanged.";
        NotifyConversation();
    }

    public Task ClearSavedConversationDraftAsync() => RunConversationAsync(async (target, live) =>
    {
        if (!clearDraftReviewed) { throw new InvalidOperationException("Review the exact saved body and revision before confirming Clear."); }
        clearDraftReviewed = false;
        var saved = await conversation!.SaveDraftAsync(savedConversationDraft
            ?? throw new InvalidOperationException("Read the exact saved draft first."), null, live, lifetime.Token);
        if (!live() || saved.Session != target.Authority || saved.Draft.Body is not null)
        { throw new IOException("Draft clear receipt is unconfirmed; inspect durable state without retry."); }
        savedConversationDraft = saved;
        composerBaseline = string.Empty;
        conversationStatus = "Exact saved draft cleared; memory-only edits remain, with no implicit save or submission.";
    });

    public Task SubmitConversationAsync() => RunConversationAsync(async (target, live) =>
    {
        if (conversationHeld) { throw new InvalidOperationException("An unconfirmed submission is held; inspect without retry."); }
        var body = composerBody;
        var receipt = await conversation!.SubmitAsync(target, body, live, lifetime.Token);
        if (!live()) { throw new OperationCanceledException("The selected private presentation changed."); }
        if (string.Equals(composerBody, body, StringComparison.Ordinal)) { composerBody = string.Empty; composerBaseline = string.Empty; }
        conversationPage = null;
        conversationStatus = "Original user message retained exactly; request " + receipt.Message.Request.RequestId.Value.ToString("D")
            + " | sequence " + receipt.Message.Sequence.ToString(CultureInfo.InvariantCulture)
            + " | HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
            + " | task " + receipt.Task.State + ". No assistant answer, dispatch, fallback or automatic retry. Saved drafts remain independently retained.";
    }, submission: true);

    internal async Task<bool> ProtectComposerAsync()
    {
        if (!ComposerDirty) { return true; }
        var decision = DecideComposerDraft is null ? ComposerDraftDecision.Cancel : await DecideComposerDraft();
        if (closed || !access.CanInspect) { return false; }
        if (decision == ComposerDraftDecision.Cancel) { Notify(); return false; }
        if (decision == ComposerDraftDecision.Discard)
        { composerBody = string.Empty; composerBaseline = string.Empty; NotifyConversation(); return true; }
        var target = RequireSelected();
        var revision = access.ControlRevision;
        var body = composerBody;
        var draft = savedConversationDraft ?? await conversation!.ReadDraftAsync(target.Authority.SessionId, lifetime.Token);
        var saved = await conversation!.SaveDraftAsync(draft, body,
            () => !closed && access.CanControl && access.ControlRevision == revision && selected == target, lifetime.Token);
        savedConversationDraft = saved;
        composerBaseline = body;
        NotifyConversation();
        return true;
    }

    private async Task RunConversationAsync(Func<Kora.Core.Storage.SessionWorkspaceEntry, Func<bool>, Task> operation,
        bool submission = false, bool sourceRead = false)
    {
        if (!CanReadConversation) { return; }
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var revision = access.ControlRevision;
        bool Live() => !closed && access.CanInspect && access.ControlRevision == revision
            && selectionEpoch == epoch && selected == target;
        conversationBusy = true;
        busy = true;
        Notify();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            await operation(target, Live);
            if (!Live()) { throw new OperationCanceledException("Private conversation presentation was revoked."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (sourceRead && exception is ConversationSnapshotChangedException && Live())
            {
                conversationPage = null;
                clearDraftReviewed = false;
                conversationStatus = "Terminal snapshot changed; Refresh conversation. Memory draft retained; no retry or submission.";
            }
            else if (sourceRead)
            {
                ClearConversation();
                conversationSourceHeld = true;
                conversationStatus = "Exact conversation source unavailable or changed. Private page/draft text revoked; refresh the exact session before restoring data.";
            }
            else if (Live())
            {
                conversationHeld |= submission && exception is ConversationSubmissionUnconfirmedException;
                conversationStatus = "Unconfirmed: " + exception.Message
                    + " Memory draft retained; no retry, rollback or successful submission inferred. Read exact conversation/task receipts.";
                if (exception is ConversationSubmissionUnconfirmedException uncertain)
                {
                    conversationStatus += " Request " + uncertain.Request.RequestId.Value.ToString("D")
                        + " | task " + uncertain.Request.TaskId.Value.ToString("D") + ".";
                }
            }
            else if (closed || !access.CanInspect || access.ControlRevision != revision) { ClearConversation(); }
            else
            {
                conversationPage = null;
                conversationHeld |= submission;
                conversationStatus = "The original selection changed during admission; its memory draft remains held, never retargeted. Inspect exact receipts.";
            }
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            conversationBusy = false;
            busy = false;
            Notify();
            if (closed) { lifetime.Dispose(); }
        }
    }

    private void ClearConversation()
    {
        composerBody = string.Empty;
        composerBaseline = string.Empty;
        savedConversationDraft = null;
        conversationPage = null;
        conversationHeld = false;
        conversationSourceHeld = false;
        clearDraftReviewed = false;
        conversationStatus = "Private conversation cleared. Explicitly read an exact session before restoring saved data.";
    }

    private void NotifyConversation()
    {
        foreach (var name in new[] { nameof(ComposerBody), nameof(ComposerDirty), nameof(ComposerTarget),
            nameof(ConversationMessages), nameof(ConversationStatus), nameof(SavedConversationDraftReview), nameof(CanReadConversation), nameof(CanNextConversation),
            nameof(CanCompose), nameof(CanSaveConversationDraft), nameof(CanReloadConversationDraft),
            nameof(CanReviewClearConversationDraft), nameof(CanClearConversationDraft), nameof(CanSubmitConversation) })
        { OnPropertyChanged(name); }
    }
}
