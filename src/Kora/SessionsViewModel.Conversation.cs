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
    private ConversationMessage? completedConversationMessage;
    private bool conversationHeld;
    private bool conversationSourceHeld;
    private bool conversationBusy;
    private bool protectingComposer;
    private bool clearDraftReviewed;
    private string conversationStatus = "Select an exact session. Drafts stay in memory unless explicitly saved.";
    internal Func<Task<ComposerDraftDecision>>? DecideComposerDraft { get; set; }
    internal void ReportComposerCloseFailure(Exception exception)
    {
        RetireDraftRecovery();
        draftSaveUnconfirmed = true;
        Failure(logger, exception.GetType().Name);
        conversationStatus = "Draft save/close is unconfirmed; the memory buffer remains open. Inspect saved revision before another decision.";
        NotifyConversation();
    }
    public bool ComposerDirty => !string.Equals(composerBody, composerBaseline, StringComparison.Ordinal);
    public string ComposerBody
    {
        get => composerBody;
        set
        {
            if (closed || !access.CanInspect || conversationBusy || protectingComposer) { return; }
            RetireComposerComparison();
            RetireDraftRecovery();
            Interlocked.Increment(ref composerBufferRevision);
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
            + " | saved draft revision " + (savedConversationDraft?.Draft.Revision ?? catalogueDraft?.DraftRevision ?? 0).ToString(CultureInfo.InvariantCulture)
        : "Select an exact session; no global or question-reply target.";
    public IReadOnlyList<ConversationMessage> ConversationMessages => conversationPage is { } page ? page.Messages
        : completedConversationMessage is { } completed ? [completed] : [];
    public bool CanReadConversation => CanRead && !conversationBusy && selected is not null && conversation is not null;
    public bool CanNextConversation => CanReadConversation && conversationPage?.Next is not null;
    public bool CanOpenConversationDetails => CanReadConversation && openConversationDetail is not null;
    public Task OpenConversationDetailAsync(ConversationMessage message) => RunConversationAsync(async (target, live) =>
    {
        if (!ConversationMessages.Concat(ConversationSearchMessages).Any(record => ReferenceEquals(record, message)))
        { throw new InvalidOperationException("Select an actual record from the currently observed exact conversation page."); }
        var searchMatch = ConversationSearchMessages.Any(record => ReferenceEquals(record, message));
        var searchEpoch = conversationSearchEpoch;
        bool DetailLive() => live() && (!searchMatch || conversationSearchEpoch == searchEpoch);
        if (searchMatch) { conversationSearchDetailTarget = target; }
        var content = await conversation!.ReadDetailAsync(target, message, DetailLive, lifetime.Token);
        if (!DetailLive()) { throw new OperationCanceledException("The original conversation selection or search changed."); }
        var beginValidation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Storage);
        async Task<Kora.Core.Presentation.AdmittedDetailContent> Resolve(CancellationToken token)
        {
            using var validation = beginValidation();
            try
            {
                var exact = await conversation.ReadDetailAsync(target, message, DetailLive, token);
                validation.Complete(HostOperationOutcome.Completed);
                return exact;
            }
            catch (OperationCanceledException)
            {
                validation.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch
            {
                validation.Complete(HostOperationOutcome.Failed);
                throw;
            }
        }
        var detailStatus = await (openConversationDetail
            ?? throw new InvalidOperationException("The native conversation detail viewer is unavailable."))(
                content, DetailLive, Resolve, lifetime.Token);
        if (!DetailLive()) { throw new OperationCanceledException("Search match navigation was revoked."); }
        conversationStatus = detailStatus;
    }, sourceRead: true, searchDetailEpoch:
        ConversationSearchMessages.Any(record => ReferenceEquals(record, message)) ? conversationSearchEpoch : null);
    public bool CanCompose => CanReadConversation && access.CanControl && selected?.Authority.IsActive == true
        && !conversationHeld && !conversationSourceHeld && !draftSaveUnconfirmed;
    public bool CanSaveConversationDraft => CanCompose && savedConversationDraft is not null;
    public bool CanReloadConversationDraft => CanReadConversation
        && (savedConversationDraft?.Draft.Body is not null || catalogueDraft is not null);
    public bool CanReviewClearConversationDraft => CanSaveConversationDraft && savedConversationDraft?.Draft.Body is not null;
    public bool CanClearConversationDraft => CanReviewClearConversationDraft && clearDraftReviewed;
    public bool CanSubmitConversation => CanCompose && !string.IsNullOrWhiteSpace(composerBody);
    public void DiscardComposerBuffer()
    {
        if (!CanReadConversation) { return; }
        RetireComposerComparison();
        RetireDraftRecovery();
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
        completedConversationMessage = null;
        savedConversationDraft = draft;
        savedConversationDraftControlRevision = access.ControlRevision;
        catalogueDraft = null;
        conversationSourceHeld = false;
        draftSaveUnconfirmed = false;
        clearDraftReviewed = false;
        conversationStatus = "Observed " + page.Messages.Length.ToString(CultureInfo.InvariantCulture)
            + " exact immutable user messages with any separately retained ASSISTANT answer. HOST status is not assistant prose. "
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
        savedConversationDraftControlRevision = access.ControlRevision;
        conversationSourceHeld = false;
        composerBaseline = body;
        clearDraftReviewed = false;
        conversationStatus = "Exact draft explicitly saved at revision " + saved.Draft.Revision.ToString(CultureInfo.InvariantCulture)
            + ". Not a submitted message, task dispatch, approval or retention heartbeat.";
    }, draftWrite: true);

    public Task ReloadConversationDraftAsync() => RunConversationAsync(async (target, live) =>
    {
        if (ComposerDirty && !await ProtectComposerAsync())
        { return; }
        await VerifyCatalogueReload(live, lifetime.Token);
        var saved = await conversation!.ReadDraftAsync(target.Authority.SessionId, lifetime.Token);
        if (catalogueDraft is { } exact && (saved.Draft.Revision != exact.DraftRevision || saved.Draft.Body is null))
        { throw new InvalidOperationException("The catalogue saved revision changed. Refresh; no newer body imported."); }
        await VerifyCatalogueReload(live, lifetime.Token);
        if (!live() || saved.Session != target.Authority) { throw new OperationCanceledException("The draft target changed."); }
        savedConversationDraft = saved;
        savedConversationDraftControlRevision = access.ControlRevision;
        conversationSourceHeld = false;
        draftSaveUnconfirmed = false;
        composerBody = saved.Draft.Body ?? string.Empty;
        composerBaseline = composerBody;
        clearDraftReviewed = false;
        conversationStatus = "Exact saved draft reloaded as data only. A new explicit Submit is required under current admission.";
    }, sourceRead: true);

    public void ReviewClearConversationDraft()
    {
        if (!CanReviewClearConversationDraft) { return; }
        RetireComposerComparison();
        RetireDraftRecovery();
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
        savedConversationDraftControlRevision = access.ControlRevision;
        composerBaseline = string.Empty;
        conversationStatus = "Exact saved draft cleared; memory-only edits remain, with no implicit save or submission.";
    }, draftWrite: true);

    public Task SubmitConversationAsync() => RunConversationAsync(async (target, live) =>
    {
        if (conversationHeld) { throw new InvalidOperationException("An unconfirmed submission is held; inspect without retry."); }
        var body = composerBody;
        var receipt = await conversation!.SubmitAsync(target, body, live, lifetime.Token);
        if (!live()) { throw new OperationCanceledException("The selected private presentation changed."); }
        if (string.Equals(composerBody, body, StringComparison.Ordinal)) { composerBody = string.Empty; composerBaseline = string.Empty; }
        conversationPage = null;
        completedConversationMessage = receipt.Message;
        conversationStatus = "Original user message retained exactly; request " + receipt.Message.Request.RequestId.Value.ToString("D")
            + " | sequence " + receipt.Message.Sequence.ToString(CultureInfo.InvariantCulture)
            + " | HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
            + " | task " + receipt.Task.State
            + (receipt.Message.AssistantResponse is null ? ". No assistant answer retained." : ". Genuine ASSISTANT answer retained separately below.")
            + " No fallback or automatic retry. Saved drafts remain independently retained; Refresh for the full bounded conversation.";
    }, submission: true);

    internal async Task<bool> ProtectComposerAsync(Func<bool>? admitted = null)
    {
        RetireComposerComparison();
        RetireDraftRecovery();
        if (protectingComposer) { return false; }
        if (admitted?.Invoke() == false) { throw new OperationCanceledException("Original composer navigation admission changed."); }
        if (!ComposerDirty) { return true; }
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var revision = access.ControlRevision;
        var body = composerBody;
        bool Live() => !closed && access.CanInspect && access.ControlRevision == revision
            && selectionEpoch == epoch && selected == target && string.Equals(composerBody, body, StringComparison.Ordinal)
            && (admitted?.Invoke() ?? true);
        protectingComposer = true;
        Notify();
        try
        {
            var decision = DecideComposerDraft is null ? ComposerDraftDecision.Cancel : await DecideComposerDraft();
            if (!Live() || decision == ComposerDraftDecision.Cancel) { return false; }
            if (decision == ComposerDraftDecision.Discard)
            { composerBody = string.Empty; composerBaseline = string.Empty; return true; }
            if (draftSaveUnconfirmed || conversationSourceHeld)
            { throw new InvalidOperationException("Reconcile the exact saved draft source before saving uncertain composer state."); }
            var draft = savedConversationDraft ?? await conversation!.ReadDraftAsync(target.Authority.SessionId, lifetime.Token);
            var saved = await conversation!.SaveDraftAsync(draft, body,
                () => Live() && access.CanControl, lifetime.Token);
            if (!Live() || saved.Session != target.Authority || !string.Equals(saved.Draft.Body, body, StringComparison.Ordinal))
            { throw new IOException("Saved draft receipt is unconfirmed; inspect exact durable revision."); }
            savedConversationDraft = saved;
            savedConversationDraftControlRevision = access.ControlRevision;
            composerBaseline = body;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            draftSaveUnconfirmed |= exception is not (ConversationDraftConflictException or ConversationCredentialException);
            throw;
        }
        finally
        {
            protectingComposer = false;
            Notify();
            if (closed && !busy && !refreshingWork && !changingRoutineQuiet && !changingQueueControl) { lifetime.Dispose(); }
        }
    }

    private async Task RunConversationAsync(Func<Kora.Core.Storage.SessionWorkspaceEntry, Func<bool>, Task> operation,
        bool submission = false, bool sourceRead = false, long? searchDetailEpoch = null, bool draftWrite = false)
    {
        if (!CanReadConversation) { return; }
        RetireComposerComparison();
        RetireDraftRecovery();
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
            if ((draftWrite || submission) && (draftSaveUnconfirmed || conversationSourceHeld))
            { throw new InvalidOperationException("Explicitly read/reconcile the exact saved source before another draft write or submission."); }
            await operation(target, Live);
            if (!Live()) { throw new OperationCanceledException("Private conversation presentation was revoked."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            draftSaveUnconfirmed |= draftWrite
                && exception is not (ConversationDraftConflictException or ConversationCredentialException);
            var searchNavigationRevoked = searchDetailEpoch is { } searchEpoch && searchEpoch != conversationSearchEpoch && Live();
            if (searchNavigationRevoked)
            {
                conversationStatus = "Search match navigation revoked. Composer and saved draft unchanged; explicit fresh Search required.";
            }
            else if (sourceRead && exception is ConversationSnapshotChangedException && Live())
            {
                ClearConversationSearch();
                conversationPage = null;
                completedConversationMessage = null;
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
                completedConversationMessage = null;
                conversationHeld |= submission;
                conversationStatus = "The original selection changed during admission; its memory draft remains held, never retargeted. Inspect exact receipts.";
            }
            Failure(logger, exception.GetType().Name);
            activity.Complete(searchNavigationRevoked || exception is OperationCanceledException
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
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
        RetireComposerComparison();
        RetireDraftRecovery();
        if (selected is { } target) { revokeConversationDetails?.Invoke(target.Authority.SessionId); }
        catalogueDraft = null;
        ClearConversationSearch();
        composerBody = string.Empty;
        composerBaseline = string.Empty;
        savedConversationDraft = null;
        savedConversationDraftControlRevision = 0;
        conversationPage = null;
        completedConversationMessage = null;
        conversationHeld = false;
        conversationSourceHeld = false;
        draftSaveUnconfirmed = false;
        clearDraftReviewed = false;
        conversationStatus = "Private conversation cleared. Explicitly read an exact session before restoring saved data.";
    }

    private void NotifyConversation()
    {
        CheckComposerComparison();
        CheckDraftRecovery();
        NotifyConversationSearch();
        foreach (var name in new[] { nameof(ComposerBody), nameof(ComposerDirty), nameof(ComposerTarget),
            nameof(ConversationMessages), nameof(ConversationStatus), nameof(SavedConversationDraftReview), nameof(CanReadConversation), nameof(CanNextConversation),
            nameof(CanOpenConversationDetails), nameof(CanReviewOriginalMessageReuse), nameof(CanCompose), nameof(CanSaveConversationDraft), nameof(CanReloadConversationDraft),
            nameof(CanReviewClearConversationDraft), nameof(CanClearConversationDraft), nameof(CanSubmitConversation), nameof(CanCompareComposer),
            nameof(CanRecoverConversationDraft) })
        { OnPropertyChanged(name); }
    }
}
