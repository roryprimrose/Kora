using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private long nameDraftRevision;
    private Func<bool>? listPreparationDraftAdmission;
    internal bool IsPreparingListQuery => listPreparationDraftAdmission is not null;

    internal async Task SaveListPreparationDraftFromNativeAsync()
    {
        var current = listPreparationDraftAdmission;
        if (!protectingComposer || current?.Invoke() != true || !access.CanControl)
        { throw new OperationCanceledException("The native dirty-buffer Save source is no longer current."); }
        var target = RequireSelected();
        var body = composerBody;
        // Only the real native Save event enters this boundary; command/voice activity cannot acquire its authority.
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Storage);
        try
        {
            await SaveProtectedComposerAsync(target, body, current);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    internal Func<bool> CaptureListPreparationEligibility()
    {
        var selection = selectionEpoch;
        var query = listSearchEpoch;
        var buffer = composerBufferRevision;
        var name = nameDraftRevision;
        var control = access.ControlRevision;
        var saved = savedConversationDraft;
        return () => !closed && access.CanInspect && control == access.ControlRevision
            && selection == selectionEpoch && query == listSearchEpoch
            && buffer == composerBufferRevision && name == nameDraftRevision
            && ReferenceEquals(saved, savedConversationDraft);
    }

    internal async Task<bool> PrepareListQueryAsync(HostId<SessionIdentity> id, Func<bool> admitted, Func<bool> focus)
    {
        id.Validate();
        var source = CaptureListPreparationEligibility();
        if (!admitted() || !source() || (!CanRead && !listSearching) || refreshingWork || protectingComposer)
        { return PreparationRefused("The original native source is unavailable or busy."); }
        if (listSearchMode && (dispositionPreview is not null || providerProposal is not null
            || retentionReview is not null || queueMoveReview is not null || queueRequeueReview is not null
            || queueCancelReview is not null || diagnosticsPlanReview is not null || memoryBatchReview is not null
            || composerComparison is not null || draftRecoveryCancellation is not null || clearDraftReviewed
            || attachments?.HasOpenPresentation == true))
        { return PreparationRefused("Finish the existing native review explicitly first; it was not replaced."); }
        // A list-search edit retires its selection. Never silently drop an unsaved name editor.
        if (listSearchMode && selected is not null
            && !string.Equals(nameDraft, selected.Metadata?.Name.Value ?? string.Empty, StringComparison.Ordinal))
        { return PreparationRefused("Finish the native session-name edit first; its draft was retained."); }
        if (ComposerDirty)
        {
            if (draftSaveUnconfirmed || conversationSourceHeld)
            { return PreparationRefused("Reconcile the unconfirmed native draft source first; the memory buffer was retained."); }
            bool kept;
            listPreparationDraftAdmission = () => admitted() && source();
            try { kept = await ProtectComposerAsync(admitted: listPreparationDraftAdmission, allowSave: false); }
            finally { listPreparationDraftAdmission = null; }
            if (!kept)
            { return PreparationRefused("The native draft decision cancelled preparation. After native Save, issue a fresh command; the old command grants no Save authority."); }
            if (!admitted() || !source())
            { return PreparationRefused("The original input, control or buffer changed during the native decision."); }
        }
        if (!admitted() || !source() || !CanEditListSearch)
        { return PreparationRefused("The original input or private source changed."); }
        InvalidateListSearch();
        sessions = null;
        listSearchMode = true;
        listSearchKind = SessionListSearchKind.ExactId;
        listQuery = id.Value.ToString("D");
        status = "PREPARED / REVIEW NEEDED. No Search has run. " + SessionListPreparationCommand.Syntax;
        var prepared = CaptureListPreparationEligibility();
        Notify();
        if (!admitted() || !prepared() || !focus() || !admitted() || !prepared())
        { return PreparationRefused("Native query focus or original admission was not confirmed; no Search has run."); }
        return true;
    }

    private bool PreparationRefused(string reason)
    {
        if (!closed && access.CanInspect)
        {
            status = "Session list preparation unavailable / cancelled. " + reason + " No Search has run.";
            OnPropertyChanged(nameof(Status));
        }
        return false;
    }
}
