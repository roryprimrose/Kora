using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private bool renaming;

    private async Task RenameInstruction(OwnedInstructionEditorWindow opened, bool recover, Func<bool> eligible, CancellationToken token)
    {
        if (kind != ArtifactKind.Instruction || profileOriginal is not null || Dirty)
        { throw new InvalidOperationException("Rename/recovery requires a clean owned Instruction editor. Save or explicitly discard draft edits separately; prompts and profile originals cannot be renamed."); }
        if (!recover && (baseline is null || newDraft || uncertain || Actions.IsSourceUnavailable || opened.Source.IsReadOnly
            || !string.Equals(selectedName, baseline.Name, StringComparison.Ordinal)
            || !string.Equals(opened.NameInput.Text, baseline.Name, StringComparison.Ordinal)
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow row && !string.Equals(row.Name, baseline.Name, StringComparison.Ordinal)))
        { throw new InvalidOperationException("Inspect the exact clean supported canonical owned Instruction before renaming. Recovery-required sources need Recover rename."); }
        var source = baseline;
        var sourceEpoch = Interlocked.Read(ref sourceRevision);
        var inputEpoch = Interlocked.Read(ref copyInputRevision);
        var channel = main.CaptureInstructionRefinementEligibility();
        var expires = DateTimeOffset.UtcNow.AddMinutes(2);
        bool Current() => eligible() && channel() && kind == ArtifactKind.Instruction && ReferenceEquals(baseline, source)
            && Interlocked.Read(ref sourceRevision) == sourceEpoch && Interlocked.Read(ref copyInputRevision) == inputEpoch
            && DateTimeOffset.UtcNow < expires;
        var review = recover ? await Actions.ReviewRenameRecovery(Current, token)
            : await Actions.ReviewRename(source!, opened.RenameNameInput.Text ?? string.Empty, Current, token);
        if (review is null)
        { opened.Report("No unfinished rename intent exists. Explicitly Inspect/Reopen the old and new IDs and Reload/reconcile; no source write or rollback occurred."); return; }
        var saved = await Actions.Rename(review, (exact, admitted) => ConfirmInstructionRename(opened, exact, Current, admitted),
            reload.ReconcileRenamedInstruction, Current, token);
        if (!Current()) { return; }
        if (saved is null)
        { opened.Report("Rename review cancelled. No new mutation or recovery was approved; any pre-existing recovery hold remains."); return; }
        baseline = saved;
        selectedName = saved.Name;
        originalText = saved.Document.Text;
        newDraft = false;
        uncertain = false;
        opened.Files.SelectedItem = null;
        opened.NameInput.Text = saved.Name;
        opened.Source.Text = originalText;
        opened.Source.IsReadOnly = false;
        opened.SetUseReviewAvailable(true);
        InvalidateInventory("Rename reconciled. Refresh inventory explicitly; old identity unavailable, new candidate disabled.");
        inventoryChoiceWithdrawn = false;
        opened.Report($"Renamed {review.Proposal.Source.Name} to {saved.Name}. Old identity is unavailable; new source is disabled and requires fresh exact Review use. "
            + "No reference, grant or activation was transferred. Reopen the new ID to inspect current resolution status. Original unrelated files and source bytes except declared name were preserved.");
    }

    private static async Task<bool> ConfirmInstructionRename(OwnedInstructionEditorWindow opened, OwnedInstructionRenameReview review,
        Func<bool> current, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmInstructionRename(opened, review, current, token)); }
        if (!current()) { return false; }
        var proposal = review.Proposal;
        var source = proposal.Source;
        var dependencies = review.Dependents.Count == 0 ? "No observed catalogue agent declares the old exact ID."
            : "Observed dependent agents (unchanged source declarations):\n" + string.Join("\n", review.Dependents.Select(item =>
                $"{item.Name}: {item.Id}; source digest {item.Digest}"));
        var details = $"Kora-owned Instructions only; same owned root. Old file: {source.CanonicalFileName}; old ID: {source.Document.Definition.Id}.\n"
            + $"New file: {proposal.DestinationName}.instructions.md; new ID: {proposal.Proposed.Definition.Id}.\n"
            + $"Original digest: {source.Document.Definition.Digest}; proposed digest: {proposal.Proposed.Definition.Digest}; BOM={source.Document.HasBom}.\n"
            + $"Exact changes: canonical filename and {(source.Document.Metadata.ContainsKey("name") ? "declared name scalar only" : "filename-derived name only; source bytes unchanged")}. "
            + "Complete original/proposed source is shown below. Metadata formatting, line endings, attribution and body bytes are not normalized.\n"
            + dependencies + "\nOld invocation/name choices and pending old-source reviews become unavailable. References stay bound to the old exact source, never the new name; "
            + "separate dependent source/digest review remains mandatory. Admitted immutable work is never retargeted; existing access, source-freshness and stricter agent-bundle revocation checks still apply.\n"
            + "No grant, activation, policy or applicability transfers. New candidate stays disabled until fresh exact review.\n"
            + (review.Recovery is null ? "This is a recoverable multi-step rename, NOT an atomic transaction: private intent, native original identity move to a recovery slot, no-replace destination publication, exact readback and cleanup. "
                : $"Explicit recovery of the original exact proposal. Observed phase: {review.Recovery.Phase}. No rollback, overwrite or retargeting is authorized. ")
            + "Cancellation or uncertain publication can leave recovery-required state. Original bytes are retained until exact destination readback; external applications may observe intermediate absence. "
            + "Destination must remain absent; no conflict overwrite or automatic retry.";
        if (await opened.Choose(review.Recovery is null ? "Review owned Instruction rename" : "Review unfinished Instruction rename",
            details, source.Document.Text, proposal.Proposed.Text, ["Reviewed exact rename", "Cancel"], token) != 0 || !current())
        { return false; }
        return await opened.Choose("Separately confirm exact Instruction rename",
            details + "\nConfirm only this exact reviewed identity/source/reference change now?",
            source.Document.Text, proposal.Proposed.Text, ["Confirm rename", "Cancel"], token) == 0 && current();
    }
}
