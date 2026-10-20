using Avalonia.Threading;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private OwnedDefinitionInventory? inventory;
    private OwnedDefinitionInventoryPage? inventoryPage;
    private Func<bool>? inventoryEligibility;
    private bool inventoryChoiceWithdrawn;
    private long inventoryEpoch;
    private long inventoryFilterRevision;
    private ArtifactKind InventoryKind => window?.InventoryKindInput.SelectedItem is ArtifactKind value
        && value is ArtifactKind.Instruction or ArtifactKind.Prompt ? value
        : throw new InvalidOperationException("Select an explicit owned metadata kind.");
    private OwnedInstructionEditorActions InventoryActions => InventoryKind == ArtifactKind.Instruction ? actions
        : promptActions ?? throw new InvalidOperationException("Prompt metadata is unavailable.");

    private void OnInventoryChanged(object? sender, EventArgs args) =>
        RetireInventory("Catalogue observation changed. Refresh and reselect; the open original/draft is retained.");

    private void OnInventoryReloadFailed(object? sender, ArtifactCatalogueReloadFailedEventArgs args) =>
        RetireInventory("Source/catalogue reload failed. Historical observations are unavailable, not current; Refresh required. Draft retained.");

    private void RetireInventory(string reason)
    {
        Interlocked.Increment(ref inventoryEpoch);
        var opened = window;
        if (Dispatcher.UIThread.CheckAccess()) { InvalidateInventory(reason); }
        else { Dispatcher.UIThread.Post(() => { if (ReferenceEquals(window, opened)) { InvalidateInventory(reason); } }); }
    }

    private void InvalidateInventory(string reason)
    {
        Interlocked.Increment(ref inventoryEpoch);
        inventory = null; inventoryPage = null; inventoryEligibility = null; inventoryChoiceWithdrawn = true;
        window?.RetireBufferFind(clearQuery: true);
        window?.ClearInventory();
        window?.ClearValidation();
        window?.SetUseReviewAvailable(false);
        window?.CancelReview();
        window?.ReportInventory(reason);
    }

    private async Task RefreshInventory(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        InvalidateInventory("Refreshing the admitted bounded owned filename inventory and existing source reconciliation.");
        var observedKind = InventoryKind;
        var observedActions = InventoryActions;
        var filterRevision = Interlocked.Read(ref inventoryFilterRevision);
        var files = await observedActions.List(eligible, token);
        if (!eligible() || filterRevision != Interlocked.Read(ref inventoryFilterRevision)) { return; }
        if (!reload.ReloadNow()) { throw new IOException("The catalogue could not be reconciled."); }
        var epoch = Interlocked.Read(ref inventoryEpoch);
        var snapshot = await observedActions.ObserveInventory(files, eligible, token);
        if (!eligible() || InventoryKind != observedKind || epoch != Interlocked.Read(ref inventoryEpoch)
            || filterRevision != Interlocked.Read(ref inventoryFilterRevision)) { return; }
        inventory = snapshot;
        inventoryEligibility = eligible;
        FilterInventory(next: false);
    }

    private void FilterInventory(bool next, bool edited = false)
    {
        if (preparingInventory) { return; }
        if (disposed || lifetime?.IsCancellationRequested != false || window is not { } opened) { return; }
        var selected = opened.Files.SelectedItem as OwnedDefinitionInventoryRow;
        if (edited)
        {
            RetireInventoryFilterEdit(opened);
        }
        if (!ownsHost() || ownershipRevision() != windowOwnershipRevision || !main.CaptureInstructionEditorEligibility()())
        {
            OnPrivacyClosed(this, EventArgs.Empty);
            return;
        }
        if (inventory is not { } snapshot || opened.InventoryKindInput.SelectedItem is not ArtifactKind queryKind
            || snapshot.Kind != queryKind || inventoryEligibility?.Invoke() != true || !InventoryActions.IsInventoryCurrent(snapshot))
        {
            InvalidateInventory("No current metadata observation. Explicit List/Refresh required; draft retained.");
            return;
        }
        try
        {
            var query = opened.QueryInput.Text ?? string.Empty;
            var mode = opened.QueryModeInput.SelectedItem is OwnedDefinitionInventoryQueryMode selectedMode
                ? selectedMode : throw new InvalidOperationException("Select an explicit owned metadata navigation mode.");
            var order = opened.OrderInput.SelectedItem is OwnedDefinitionInventoryOrder selectedOrder
                ? selectedOrder : throw new InvalidOperationException("Select an explicit owned metadata order.");
            var filter = opened.UseStateInput.SelectedItem is OwnedDefinitionUseFilter value ? value : OwnedDefinitionUseFilter.All;
            var digest = opened.DigestInput.Text ?? string.Empty;
            var revision = opened.RevisionInput.Text ?? string.Empty;
            var from = opened.ObservedFromInput.Text ?? string.Empty;
            var until = opened.ObservedUntilInput.Text ?? string.Empty;
            var page = next && inventoryPage is { } previous
                ? snapshot.Continue(previous, query, filter, digest, revision, from, until, mode, order)
                : snapshot.Page(query, filter, digest: digest, revision: revision, observedFromUtc: from, observedUntilUtc: until, mode: mode, order: order);
            inventoryPage = page;
            opened.ShowInventory(page);
            if (selected is not null && (edited || !page.Rows.Contains(selected)))
            {
                inventoryChoiceWithdrawn = true;
                opened.ReportInventory("Selected inventory choice withdrawn by this filter/page. Open original and unsaved draft remain unchanged. Reselect explicitly; no first match is loaded.");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            using var diagnostic = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Presentation, RequestOrigin.LocalUi);
            EditorFailed(logger, exception.GetType().Name);
            diagnostic.Complete(HostOperationOutcome.Failed);
            inventoryPage = null; inventoryChoiceWithdrawn = true;
            opened.ClearInventory();
            opened.ReportInventory(exception.Message + " No truncation or partial match; draft retained.");
        }
    }

    private void RetireInventoryFilterEdit(OwnedInstructionEditorWindow opened)
    {
        Interlocked.Increment(ref inventoryFilterRevision);
        Interlocked.Increment(ref inventoryEpoch);
        Interlocked.Increment(ref sourceRevision);
        inventoryPage = null;
        inventoryChoiceWithdrawn = true;
        opened.SetUseReviewAvailable(false);
        opened.CancelReview();
        opened.ClearInventory();
        opened.ClearValidation();
    }

    private async Task InspectChoice(OwnedInstructionEditorWindow opened, bool reloadOriginal, string? exactName,
        Func<bool> eligible, CancellationToken token)
    {
        var snapshot = inventory;
        var row = exactName is null && (!reloadOriginal || selectedName is null) ? opened.Files.SelectedItem as OwnedDefinitionInventoryRow : null;
        var epoch = Interlocked.Read(ref inventoryEpoch);
        var sourceEpoch = Interlocked.Read(ref sourceRevision);
        bool CurrentChoice() => eligible() && epoch == Interlocked.Read(ref inventoryEpoch)
            && sourceEpoch == Interlocked.Read(ref sourceRevision);
        if (row is not null && (snapshot is null || snapshot.Kind != InventoryKind
            || inventoryEligibility?.Invoke() != true || !InventoryActions.IsInventoryCurrent(snapshot)))
        { throw new InvalidOperationException("The selected observation expired. Refresh and reselect; draft retained."); }
        if (row is null && exactName is null && !reloadOriginal && inventoryChoiceWithdrawn)
        { throw new InvalidOperationException("The previous inventory choice was withdrawn. Reselect or explicitly enter the current filename ID; the draft is still open."); }
        if (row is not null && row.Kind != kind)
        {
            // Only explicit Inspect can cross from metadata kind to source kind, under the native draft guard.
            if (!await SwitchKind(opened, row.Kind, token, preserveInventory: true) || !eligible()
                || !ReferenceEquals(inventory, snapshot) || epoch != Interlocked.Read(ref inventoryEpoch))
            { return; }
            sourceEpoch = Interlocked.Read(ref sourceRevision);
        }
        else if (!await AbandonDraft(opened, token) || !CurrentChoice()) { return; }
        var name = exactName ?? (reloadOriginal ? selectedName ?? row?.Name ?? opened.NameInput.Text : row?.Name ?? opened.NameInput.Text);
        await Load(opened, name, CurrentChoice, token, row is null ? null : snapshot, row);
    }
}
