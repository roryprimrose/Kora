using Kora.Application.Definitions;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private void AdmitSelectedMetadata(OwnedInstructionEditorWindow opened)
    {
        opened.RetireMetadataFind();
        if (inventory is not { } snapshot || inventoryPage is not { } page
            || opened.Files.SelectedItem is not OwnedDefinitionInventoryRow row
            || !page.Rows.Any(item => ReferenceEquals(item, row))) { return; }
        var epoch = Interlocked.Read(ref inventoryEpoch);
        var sourceEpoch = Interlocked.Read(ref sourceRevision);
        var inputEpoch = Interlocked.Read(ref copyInputRevision);
        var observedKind = kind;
        var eligible = main.CaptureInstructionEditorEligibility();
        var observation = main.CaptureDefinitionObservationEligibility();
        bool Current() => !disposed && !busy && lifetime?.IsCancellationRequested == false
            && ReferenceEquals(window, opened) && ownsHost() && ownershipRevision() == windowOwnershipRevision
            && eligible() && observation()
            && kind == observedKind && opened.KindInput.SelectedItem is Kora.Core.Artifacts.ArtifactKind selectedKind && selectedKind == observedKind
            && row.Kind == observedKind && ReferenceEquals(inventory, snapshot) && ReferenceEquals(inventoryPage, page)
            && ReferenceEquals(opened.Files.SelectedItem, row)
            && epoch == Interlocked.Read(ref inventoryEpoch) && sourceEpoch == Interlocked.Read(ref sourceRevision)
            && inputEpoch == Interlocked.Read(ref copyInputRevision)
            && inventoryEligibility?.Invoke() == true && Actions.IsInventoryCurrent(snapshot);
        opened.AdmitMetadataFind(row, Current);
    }
}
