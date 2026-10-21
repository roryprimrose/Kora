using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private Func<bool>? CaptureGoToLine()
    {
        if (window is not { } opened || lifetime is not { IsCancellationRequested: false } active
            || busy || disposed || closing || selectedName is not { } name || baseline is null && !newDraft
            || opened.Source.IsReadOnly || Actions.IsSourceUnavailable
            || opened.KindInput.SelectedItem is not ArtifactKind selectedKind || selectedKind != kind
            || !string.Equals(opened.NameInput.Text, name, StringComparison.Ordinal)
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow row && !string.Equals(row.Name, name, StringComparison.Ordinal))
        { return null; }
        var revision = Interlocked.Read(ref sourceRevision);
        var inputRevision = Interlocked.Read(ref copyInputRevision);
        var epoch = Interlocked.Read(ref inventoryEpoch);
        var ownership = windowOwnershipRevision;
        var original = baseline;
        var observedKind = kind;
        var eligible = main.CaptureInstructionEditorEligibility();
        bool Current() => !disposed && !closing && !busy && !active.IsCancellationRequested
            && ReferenceEquals(lifetime, active) && ReferenceEquals(window, opened) && opened.IsVisible
            && ownsHost() && ownershipRevision() == ownership && eligible()
            && !opened.Source.IsReadOnly && !Actions.IsSourceUnavailable
            && kind == observedKind && ReferenceEquals(baseline, original) && string.Equals(selectedName, name, StringComparison.Ordinal)
            && revision == Interlocked.Read(ref sourceRevision)
            && inputRevision == Interlocked.Read(ref copyInputRevision) && epoch == Interlocked.Read(ref inventoryEpoch);
        return Current() ? Current : null;
    }
}
