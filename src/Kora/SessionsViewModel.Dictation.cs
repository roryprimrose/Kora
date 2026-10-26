using Kora.Application.Voice;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private long dictationSourceRevision;
    internal ComposerDictationService? Dictation { get; private set; }

    internal void BindDictation(ComposerDictationService? service) => Dictation = service;

    internal Func<bool> CaptureDictationPresentationEligibility()
    {
        var current = CaptureDraftPresentationEligibility();
        var source = dictationSourceRevision;
        return () => current() && dictationSourceRevision == source;
    }

    internal void RevokeDictationSource(HostId<SessionIdentity> session)
    {
        if (selected?.Authority.SessionId != session) { return; }
        dictationSourceRevision++;
        conversationSourceHeld = true;
        Dictation?.Revalidate();
        NotifyConversation();
    }

    internal ComposerDictationTarget CaptureDictationTarget()
    {
        var target = selected ?? throw new InvalidOperationException("Select an exact session before arming dictation.");
        var selection = selectionEpoch;
        var control = access.ControlRevision;
        var revision = Interlocked.Read(ref composerBufferRevision);
        var source = dictationSourceRevision;
        bool SourceCurrent() => !closed && CanCompose && !protectingComposer
            && selected == target && target.Authority.IsActive && selectionEpoch == selection
            && dictationSourceRevision == source
            && composerSession == target.Authority.SessionId && composerAuthority == target.Authority
            && composerControl == control && access.ControlRevision == control;
        bool Current() => SourceCurrent() && Interlocked.Read(ref composerBufferRevision) == revision;
        var request = new HostRequest(new(Guid.NewGuid()),
            target.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        return new(this, ComposerBody, request, Current, (literal, admitted) =>
        {
            if (!Current() || !admitted()) { return false; }
            var previous = composerBody;
            unsent.TryGetValue(target.Authority.SessionId, out var previousBuffer);
            var inserted = Interlocked.Increment(ref composerBufferRevision);
            composerBody = literal;
            RememberUnsent();
            unsent.TryGetValue(target.Authority.SessionId, out var publishedBuffer);
            conversationStatus = "Literal dictation inserted into this session's private memory only. Submit remains a separate action.";
            var completed = false;
            try
            {
                NotifyConversation();
                completed = SourceCurrent() && Interlocked.Read(ref composerBufferRevision) == inserted && admitted();
                return completed;
            }
            finally
            {
                if (!completed)
                {
                    // Never restore an old session body into a replacement selection or overwrite a newer edit.
                    if (!closed && access.CanInspect && access.ControlRevision == control
                        && unsent.TryGetValue(target.Authority.SessionId, out var currentBuffer)
                        && ReferenceEquals(currentBuffer, publishedBuffer))
                    {
                        if (previousBuffer is null) { unsent.Remove(target.Authority.SessionId); }
                        else { unsent[target.Authority.SessionId] = previousBuffer; }
                        unsentRevision++;
                    }
                    if (!closed && access.CanInspect && access.ControlRevision == control
                        && composerSession == target.Authority.SessionId
                        && Interlocked.Read(ref composerBufferRevision) == inserted)
                    {
                        composerBody = previous;
                        Interlocked.Increment(ref composerBufferRevision);
                        RememberUnsent();
                    }
                    conversationStatus = "Dictation publication revoked. No handoff completed; independent session input is preserved.";
                    NotifyConversation();
                }
            }
        }, ComposerDictationDestination.SessionComposer);
    }
}
