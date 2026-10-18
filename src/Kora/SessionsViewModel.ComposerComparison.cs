using Kora.Application.Presentation;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ConversationDraftComparison? composerComparison;
    private long composerComparisonEpoch;
    private long savedConversationDraftControlRevision;
    internal event EventHandler? ComposerComparisonRetired;

    public bool CanCompareComposer => CanReadConversation && access.CanControl && !conversationSourceHeld
        && savedConversationDraftControlRevision > 0 && access.ControlRevision == savedConversationDraftControlRevision
        && savedConversationDraft is { } observed && observed.Session == selected?.Authority;

    internal ConversationDraftComparison? CompareComposer()
    {
        RetireComposerComparison();
        if (!CanCompareComposer)
        {
            conversationStatus = "Compare unavailable. Explicitly read the exact saved revision under current private ownership first; nothing was read or changed.";
            NotifyConversation();
            return null;
        }
        var target = selected!;
        var observed = savedConversationDraft!;
        var epoch = composerComparisonEpoch;
        var selection = selectionEpoch;
        var control = access.ControlRevision;
        var body = composerBody;
        bool Current() => CanCompareComposer && composerComparisonEpoch == epoch && selectionEpoch == selection
            && access.ControlRevision == control && selected == target && ReferenceEquals(savedConversationDraft, observed)
            && string.Equals(composerBody, body, StringComparison.Ordinal);
        try
        {
            composerComparison = new(target.Authority, observed, body, Current);
            return composerComparison;
        }
        catch (Exception exception) when (exception is InvalidDataException or ConversationCredentialException or OperationCanceledException)
        {
            conversationStatus = "Compare refused: " + exception.Message + " Complete memory draft retained; nothing was truncated or saved.";
            NotifyConversation();
            return null;
        }
    }

    internal void CloseComposerComparison(ConversationDraftComparison comparison)
    {
        if (ReferenceEquals(composerComparison, comparison)) { RetireComposerComparison(); }
    }

    private void RetireComposerComparison()
    {
        composerComparisonEpoch++;
        var previous = composerComparison;
        composerComparison = null;
        previous?.Dispose();
        ComposerComparisonRetired?.Invoke(this, EventArgs.Empty);
    }

    private void CheckComposerComparison()
    {
        if (composerComparison is { Available: false }) { RetireComposerComparison(); }
    }
}
