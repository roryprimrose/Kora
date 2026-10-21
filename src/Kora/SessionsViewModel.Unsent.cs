using Kora.Core.Conversation;
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
    private long savedConversationDraftControlRevision;
    internal bool HasUnsentMessages => ComposerDirty || unsent.Values.Any(value => value.Body.Length != 0);

    private sealed record UnsentMessage(WorkSessionAuthorization Authority, long Control, string Body, bool Held, bool SourceHeld);

    private void RememberUnsent()
    {
        if (closed || !access.CanInspect || selected is not { } target || composerSession != target.Authority.SessionId
            || composerControl != access.ControlRevision) { return; }
        conversationSourceHeld |= composerAuthority != target.Authority;
        unsent[target.Authority.SessionId] = new(target.Authority, access.ControlRevision, composerBody,
            conversationHeld, conversationSourceHeld);
    }

    private void RestoreUnsent()
    {
        if (closed || !access.CanInspect)
        {
            unsent.Clear();
            composerBody = string.Empty;
            composerSession = null;
            return;
        }
        if (composerControl != 0 && composerControl != access.ControlRevision)
        {
            unsent.Clear();
            composerBody = string.Empty;
            composerSession = null;
            savedConversationDraft = null;
            savedConversationDraftControlRevision = 0;
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
            unsent.Remove(target.Authority.SessionId);
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
        unsent.Remove(session);
        if (composerSession == session) { ClearConversation(preserveUnsent: false); }
    }

    internal async Task<bool> ConfirmUnsentDiscardAsync(bool all)
    {
        if (protectingComposer || closed || !access.CanInspect) { return false; }
        RememberUnsent();
        if (all ? !HasUnsentMessages : !ComposerDirty) { return true; }
        var revision = access.ControlRevision;
        var selection = selectionEpoch;
        protectingComposer = true;
        Notify();
        try
        {
            var decision = DecideComposerDraft is null ? ComposerDraftDecision.Cancel : await DecideComposerDraft();
            if (closed || !access.CanInspect || access.ControlRevision != revision || selectionEpoch != selection
                || decision != ComposerDraftDecision.Discard) { return false; }
            if (all) { unsent.Clear(); composerBody = string.Empty; }
            return true;
        }
        finally { protectingComposer = false; Notify(); }
    }

    public bool CanRecoverConversationDraft => CanReadConversation && access.CanControl && !ComposerDirty
        && !conversationHeld && !conversationSourceHeld
        && savedConversationDraftControlRevision == access.ControlRevision
        && savedConversationDraft is { Draft.Body: not null } observed && observed.Session == selected?.Authority;

    public Task RecoverLegacyUnsentAsync()
    {
        if (!CanRecoverConversationDraft) { return Task.CompletedTask; }
        var observed = savedConversationDraft!;
        return RunConversationAsync(async (target, live) =>
        {
            var latest = await conversation!.ReadLegacyUnsentAsync(target, live, lifetime.Token);
            if (!live() || ComposerDirty || latest.Session != observed.Session || latest.Draft != observed.Draft)
            { throw new InvalidDataException("The exact legacy source changed. Read again; no newer body was imported."); }
            composerBody = latest.Draft.Body ?? throw new InvalidDataException("Legacy text is no longer available.");
            RememberUnsent();
            conversationStatus = "Existing legacy text recovered into this session's empty unsent input, in memory only. Nothing saved, removed or submitted.";
        }, sourceRead: true);
    }
}
