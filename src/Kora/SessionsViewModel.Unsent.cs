using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private readonly Dictionary<HostId<SessionIdentity>, UnsentMessage> unsent = [];
    private HostId<SessionIdentity>? composerSession;
    private WorkSessionAuthorization? composerAuthority;
    private long composerControl;
    private long unsentRevision;
    internal bool HasUnsentMessages => ComposerDirty || unsent.Values.Any(value => value.Body.Length != 0);

    private sealed record UnsentMessage(WorkSessionAuthorization Authority, long Control, string Body, bool Held, bool SourceHeld);

    private void RememberUnsent()
    {
        if (closed || !access.CanInspect || selected is not { } target || composerSession != target.Authority.SessionId
            || composerControl != access.ControlRevision) { return; }
        conversationSourceHeld |= composerAuthority != target.Authority;
        var value = new UnsentMessage(target.Authority, access.ControlRevision, composerBody, conversationHeld, conversationSourceHeld);
        if (!unsent.TryGetValue(target.Authority.SessionId, out var previous) || previous != value)
        {
            unsent[target.Authority.SessionId] = value;
            unsentRevision++;
        }
    }

    private void RestoreUnsent()
    {
        if (closed || !access.CanInspect)
        {
            ClearUnsent();
            composerBody = string.Empty;
            composerSession = null;
            return;
        }
        if (composerControl != 0 && composerControl != access.ControlRevision)
        {
            ClearUnsent();
            composerBody = string.Empty;
            composerSession = null;
        }
        if (selected is not { } target) { return; }
        if (composerSession == target.Authority.SessionId)
        {
            conversationSourceHeld |= composerAuthority != target.Authority;
            return;
        }
        composerSession = target.Authority.SessionId;
        composerAuthority = target.Authority;
        composerControl = access.ControlRevision;
        if (!unsent.TryGetValue(target.Authority.SessionId, out var value)) { return; }
        if (value.Control != access.ControlRevision)
        {
            if (unsent.Remove(target.Authority.SessionId)) { unsentRevision++; }
            return;
        }
        composerBody = value.Body;
        conversationHeld = value.Held;
        conversationSourceHeld = value.SourceHeld || value.Authority != target.Authority;
        if (conversationHeld || conversationSourceHeld)
        { conversationStatus = "Unsent input retained for this exact session. Changed/unknown source or submission remains held; inspect exact receipts."; }
    }

    internal void RetireUnsent(HostId<SessionIdentity> session)
    {
        if (unsent.Remove(session)) { unsentRevision++; }
        if (composerSession == session) { ClearConversation(preserveUnsent: false); }
    }

    private void ClearUnsent()
    {
        if (unsent.Count == 0) { return; }
        unsent.Clear();
        unsentRevision++;
    }

    internal Func<bool> CaptureUnsentShutdownEligibility()
    {
        var admitted = CanRead && access.CanControl;
        var control = access.ControlRevision;
        var selection = selectionEpoch;
        var target = selected;
        var page = sessions;
        var revision = Interlocked.Read(ref composerBufferRevision);
        var buffers = unsentRevision;
        // Shutdown closes host input before joining resources. The receipt uses the captured private
        // control revision, not CanInspect, which is also closed by that same voluntary operation.
        return () => admitted && !closed && access.ControlRevision == control && selectionEpoch == selection && selected == target
            && ReferenceEquals(sessions, page)
            && !busy && !protectingComposer && !refreshingWork && !conversationBusy && !HasUnsentMessages
            && !conversationHeld && !conversationSourceHeld
            && Interlocked.Read(ref composerBufferRevision) == revision && unsentRevision == buffers;
    }

    internal async Task<bool> ConfirmUnsentDiscardAsync(bool all)
    {
        if (!CanRead || !access.CanControl) { return false; }
        RememberUnsent();
        if (all && (conversationHeld || conversationSourceHeld || unsent.Values.Any(value => value.Held || value.SourceHeld)))
        { throw new InvalidOperationException("Unconfirmed conversation source or submission remains held. Inspect exact receipts before voluntary shutdown."); }
        if (all ? !HasUnsentMessages : !ComposerDirty) { return true; }
        var revision = access.ControlRevision;
        var selection = selectionEpoch;
        var target = selected;
        var page = sessions;
        var buffer = Interlocked.Read(ref composerBufferRevision);
        var buffers = unsentRevision;
        bool Current() => !closed && access.CanInspect && access.CanControl && access.ControlRevision == revision
            && selectionEpoch == selection && selected == target && Interlocked.Read(ref composerBufferRevision) == buffer
            && ReferenceEquals(sessions, page)
            && unsentRevision == buffers
            && (!all || !conversationHeld && !conversationSourceHeld);
        protectingComposer = true;
        Notify();
        try
        {
            if (!Current()) { throw new OperationCanceledException("Unsent input changed before discard review."); }
            var decision = DecideComposerDraft is null ? ComposerDraftDecision.Cancel : await DecideComposerDraft();
            if (!Current() || decision != ComposerDraftDecision.Discard) { return false; }
            if (all) { ClearUnsent(); composerBody = string.Empty; }
            return true;
        }
        finally { protectingComposer = false; Notify(); }
    }

    public bool CanRecoverConversationDraft => CanReadConversation && access.CanControl && !ComposerDirty
        && !conversationHeld && !conversationSourceHeld;

    public Task RecoverLegacyUnsentAsync()
    {
        if (!CanRecoverConversationDraft) { return Task.CompletedTask; }
        return RunConversationAsync(async (target, live) =>
        {
            var observed = await conversation!.ReadLegacyUnsentAsync(target, live, lifetime.Token);
            if (observed.Draft.Body is null)
            {
                conversationStatus = "No existing legacy text for this exact session. Unsent input and conversation history unchanged.";
                return;
            }
            var latest = await conversation!.ReadLegacyUnsentAsync(target, live, lifetime.Token);
            if (!live() || ComposerDirty || latest.Session != observed.Session || latest.Draft != observed.Draft)
            { throw new InvalidDataException("The exact legacy source changed. Read again; no newer body was imported."); }
            composerBody = latest.Draft.Body ?? throw new InvalidDataException("Legacy text is no longer available.");
            RememberUnsent();
            conversationStatus = "Existing legacy text recovered into this session's empty unsent input, in memory only. Nothing saved, removed or submitted.";
        }, legacyRecovery: true);
    }
}
