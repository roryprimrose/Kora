using Avalonia.Threading;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController : IOwnedDefinitionPreparation
{
    private bool preparingInventory;
    private string? activeEditorAction;
    private bool PreparationBusy => busy && activeEditorAction is not ("List" or "Refresh" or "Inspect" or "Reload" or "Validate" or "Compare");

    Func<bool> IOwnedDefinitionPreparation.CaptureLifetime()
    {
        var captured = window;
        var source = Interlocked.Read(ref sourceRevision);
        var filters = Interlocked.Read(ref inventoryFilterRevision);
        var owner = ownershipRevision();
        return () => !disposed && !PreparationBusy && ownsHost() && owner == ownershipRevision()
            && ReferenceEquals(window, captured) && source == Interlocked.Read(ref sourceRevision)
            && filters == Interlocked.Read(ref inventoryFilterRevision)
            && (captured is null || captured.IsVisible && !captured.HasMutationReview);
    }

    bool IOwnedDefinitionPreparation.TryPrepare(ArtifactKind requestedKind, string ownedId, Func<bool> admitted)
    {
        if (!Dispatcher.UIThread.CheckAccess() || disposed || PreparationBusy || !admitted() || !ownsHost()
            || window?.HasMutationReview == true
            || requestedKind is not (ArtifactKind.Instruction or ArtifactKind.Prompt)
            || requestedKind == ArtifactKind.Prompt && promptActions is null)
        { return false; }
        OwnedDefinitionInventory.ValidateQuery(ownedId, OwnedDefinitionInventoryQueryMode.ExactOwnedId);
        var owner = ownershipRevision();
        Open(load: false);
        if (window is not { } opened || lifetime?.IsCancellationRequested != false
            || !admitted() || owner != ownershipRevision() || owner != windowOwnershipRevision)
        { return false; }
        opened.Activate();
        if (!opened.IsVisible || !opened.QueryInput.Focus() || !opened.QueryInput.IsKeyboardFocusWithin
            || !admitted() || !ownsHost() || !ReferenceEquals(window, opened) || owner != ownershipRevision())
        { return false; }
        var buffer = Interlocked.Read(ref editorBufferRevision);
        // Metadata kind is not the loaded source kind. No draft abandonment or source retargeting occurs here.
        preparingInventory = true;
        try
        {
            opened.InventoryKindInput.SelectedItem = requestedKind;
            opened.QueryModeInput.SelectedItem = OwnedDefinitionInventoryQueryMode.ExactOwnedId;
            opened.QueryInput.Text = ownedId;
        }
        finally { preparingInventory = false; }
        RetireInventoryFilterEdit(opened);
        bool Current() => admitted() && !disposed && ownsHost() && owner == ownershipRevision()
            && buffer == Interlocked.Read(ref editorBufferRevision)
            && ReferenceEquals(window, opened) && lifetime?.IsCancellationRequested == false
            && opened.IsVisible && opened.QueryInput.IsKeyboardFocusWithin
            && Equals(opened.InventoryKindInput.SelectedItem, requestedKind)
            && Equals(opened.QueryModeInput.SelectedItem, OwnedDefinitionInventoryQueryMode.ExactOwnedId)
            && string.Equals(opened.QueryInput.Text, ownedId, StringComparison.Ordinal);
        if (Current())
        {
            opened.ReportInventory("Exact owned metadata query PREPARED; no query or read ran. Other filter drafts are retained and AND-composed. "
                + "List/Refresh and Inspect are separate explicit gestures. Loaded source kind, original and unsaved draft are unchanged. "
                + "A filename query is not evidence of existence, ownership, trust, use or executable authority.");
            if (Current()) { return true; }
        }
        opened.ReportInventory("Owned metadata preparation unavailable: private host, source buffer, filters or focus changed before native acknowledgement. No query or source read ran.");
        return false;
    }
}
