using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private Func<bool>? CaptureBufferFind(OwnedInstructionEditorWindow opened)
    {
        if (disposed || busy || lifetime?.IsCancellationRequested != false || !ReferenceEquals(window, opened)
            || !opened.IsVisible || selectedName is not { } name || baseline is null && !newDraft
            || !string.Equals(opened.NameInput.Text, name, StringComparison.Ordinal)
            || opened.KindInput.SelectedItem is not ArtifactKind selectedKind || selectedKind != kind
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow row && !string.Equals(row.Name, name, StringComparison.Ordinal))
        { return null; }
        var original = baseline;
        var draft = newDraft;
        var observedKind = kind;
        var revision = Interlocked.Read(ref sourceRevision);
        var ownership = windowOwnershipRevision;
        var eligibility = main.CaptureInstructionEditorEligibility();
        bool Current() => !disposed && !busy && lifetime?.IsCancellationRequested == false
            && ReferenceEquals(window, opened) && opened.IsVisible && ownsHost()
            && ownershipRevision() == ownership && eligibility()
            && ReferenceEquals(baseline, original) && newDraft == draft && kind == observedKind
            && string.Equals(selectedName, name, StringComparison.Ordinal)
            && revision == Interlocked.Read(ref sourceRevision);
        return Current() ? Current : null;
    }
}
