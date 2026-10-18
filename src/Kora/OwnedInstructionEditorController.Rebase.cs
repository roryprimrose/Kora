using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private CancellationTokenSource? rebaseLifetime;

    private async Task RebaseDraft(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        if (!opened.IsVisible || opened.Source.IsReadOnly || baseline is not { } original || selectedName is not { } name
            || uncertain || Actions.IsSourceUnavailable || newDraft
            || !string.Equals(opened.NameInput.Text, name, StringComparison.Ordinal)
            || opened.KindInput.SelectedItem is not ArtifactKind selectedKind || selectedKind != kind
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow row && !string.Equals(row.Name, name, StringComparison.Ordinal))
        { throw new InvalidOperationException("Rebase requires the exact open supported owned baseline, unchanged ID/kind and confirmed publication state. Reload/reconcile uncertainty explicitly; the raw draft is retained."); }
        var draft = opened.Source.Text ?? string.Empty;
        var revision = Interlocked.Read(ref sourceRevision);
        var inputRevision = Interlocked.Read(ref copyInputRevision);
        var epoch = Interlocked.Read(ref inventoryEpoch);
        var observedKind = kind;
        var observedActions = Actions;
        var inputCurrent = main.CaptureDefinitionObservationEligibility();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        rebaseLifetime = cancellation;
        bool Current() => !cancellation.IsCancellationRequested && eligible() && inputCurrent()
            && ReferenceEquals(baseline, original) && kind == observedKind && !uncertain
            && revision == Interlocked.Read(ref sourceRevision) && epoch == Interlocked.Read(ref inventoryEpoch)
            && inputRevision == Interlocked.Read(ref copyInputRevision);
        try
        {
            var result = await observedActions.RebaseDraft(name, original, draft, (proposal, admitted) =>
                opened.ReviewRebase(proposal, () => Current() && observedActions.IsDraftValidationCurrent(proposal.Draft.Validation), admitted),
                logger, Current, cancellation.Token);
            if (!Current()) { return; }
            if (result is null)
            { opened.Report("Rebase review cancelled. Prior baseline and exact raw draft retained; no source or use-state write."); return; }
            opened.ClearValidation();
            opened.SetUseReviewAvailable(false);
            if (!Current() || !observedActions.IsDraftValidationCurrent(result.Draft.Validation)) { return; }
            baseline = result.Latest;
            originalText = result.Latest.Document.Text;
            Interlocked.Increment(ref sourceRevision);
            opened.Report("Latest inspected baseline adopted in memory; exact raw draft retained without merging or writing. "
                + "Old editor validation/use review retired. Save is separate, revalidates, requires complete exact confirmation and can conflict again; no use enabled.");
        }
        finally { rebaseLifetime = null; }
    }
}
