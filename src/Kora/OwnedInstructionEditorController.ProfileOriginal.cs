using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private sealed record ProfileOriginal(InstructionOriginalRegistration Registration, CustomizationSnapshot Snapshot);
    private ProfileOriginal? profileOriginal;

    private ProfileInstructionEditorWorkflow ProfileOriginals =>
        profileOriginals ?? throw new InvalidOperationException("Registered profile-original editing is unavailable in this runtime.");

    private async Task OpenProfileOriginal(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token,
        bool reloadOriginal = false)
    {
        if (kind != ArtifactKind.Instruction)
        { throw new InvalidOperationException("Profile-original editing supports Instruction only. Other profile kinds remain unavailable here."); }
        var registration = ProfileOriginals.RegisteredInstruction
            ?? throw new InvalidOperationException("No exact profile instruction is registered/read-consented. Select and explicitly review one existing original in Customizations first; this editor grants no source registration or consent.");
        if (reloadOriginal && profileOriginal?.Snapshot.Source != registration.Source)
        { throw new InvalidOperationException("The selected original no longer has the same source registration. Reopen the exact source deliberately; no retargeting is allowed."); }
        if (!await AbandonDraft(opened, token)) { return; }
        ClearRestore();
        var revision = Interlocked.Read(ref sourceRevision);
        var inputRevision = Interlocked.Read(ref copyInputRevision);
        bool Current() => eligible() && revision == Interlocked.Read(ref sourceRevision)
            && inputRevision == Interlocked.Read(ref copyInputRevision) && ProfileOriginals.IsCurrent(registration);
        if (!reloadOriginal && await opened.Choose("Open one registered profile instruction original",
            $"Exact shared original: {registration.Source.Location}\n"
            + "This source already has explicit host-lifetime registration/read consent in Customizations. Open only this original in the current editor? "
            + "No new source, root or consent is registered. Registration/selection does not approve any write. "
            + "Supported: strict UTF-8 native kora-format: 1, bounded scalar name/description/attribution, at most 64 KiB. "
            + "Unknown metadata, scope/globs and other formats are refused without rewriting. "
            + "Every Save separately reviews the complete old/new source and needs Confirm save original. "
            + "Closing or withdrawing its Customizations registration retires this editor's source authority. Create Kora copy remains separate.",
            string.Empty, string.Empty, ["Open exact registered original", "Cancel"], token) != 0) { return; }
        var snapshot = await ProfileOriginals.Open(registration, Current, token);
        if (!Current()) { return; }
        InvalidateInventory("Profile original selected. Owned metadata controls do not select or retarget this source.");
        baseline = null; selectedName = null; newDraft = false;
        profileOriginal = new(registration, snapshot);
        originalText = snapshot.Files.Single().Text;
        opened.NameInput.Text = registration.Source.Name;
        opened.Source.Text = originalText;
        opened.Source.IsReadOnly = false;
        opened.SetUseReviewAvailable(false);
        uncertain = !reload.ReloadNow();
        opened.Report($"Exact registered profile original: {snapshot.Source.Location}. "
            + (uncertain ? "Catalogue reconciliation unavailable; Reload/reconcile before Save."
                : "Manual edit, exact separately confirmed Save and Reload available. No write or use approved by opening; unknown formats are not upgraded."));
    }

    private async Task<bool> SaveProfileOriginal(OwnedInstructionEditorWindow opened, bool delete,
        Func<bool> eligible, CancellationToken token)
    {
        var original = profileOriginal ?? throw new InvalidOperationException("Open one registered original first.");
        if (delete || uncertain || Actions.IsSourceUnavailable)
        { throw new InvalidOperationException("Original deletion is not admitted. Save requires an exact opened supported source and successful explicit Reload/reconciliation."); }
        var revision = Interlocked.Read(ref sourceRevision);
        var inputRevision = Interlocked.Read(ref copyInputRevision);
        bool Current() => eligible() && ReferenceEquals(profileOriginal, original)
            && revision == Interlocked.Read(ref sourceRevision) && inputRevision == Interlocked.Read(ref copyInputRevision)
            && ProfileOriginals.IsCurrent(original.Registration);
        var result = await ProfileOriginals.Save(original.Registration, original.Snapshot, opened.Source.Text ?? string.Empty,
            new NativeProfileInstructionMutationReview(opened, Current), Current, token);
        if (!Current()) { return false; }
        if (!result.Saved && !result.Reloaded)
        { opened.Report("Original Save/conflict review cancelled. Source and open draft retained; no replacement approved."); return false; }
        if (!reload.ReloadNow())
        { uncertain = true; throw new IOException("Original save/reload may be durable, but catalogue reconciliation is unconfirmed. Keep the revision held and explicitly Reload before further mutation."); }
        profileOriginal = new(original.Registration, result.Current);
        originalText = result.Current.Files.Single().Text;
        opened.Source.Text = originalText;
        opened.SetUseReviewAvailable(false);
        opened.Report(result.Reloaded
            ? "Explicitly abandoned the draft and reloaded the exact current original. Nothing saved or enabled."
            : "Exact original saved/read back and catalogue reconciled (unchanged bytes require no write). Changed content remains disabled; no applicability, activation, grants or semantic harmlessness inferred.");
        return result.Saved;
    }

    private async Task CompareProfileDraft(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        var original = profileOriginal!;
        var revision = Interlocked.Read(ref sourceRevision);
        bool Current() => eligible() && ReferenceEquals(profileOriginal, original)
            && revision == Interlocked.Read(ref sourceRevision) && ProfileOriginals.IsCurrent(original.Registration);
        if (!Current()) { throw new InvalidOperationException("The selected original registration expired. Reopen or explicitly Reload after source review."); }
        var proposed = ProfileInstructionEditorWorkflow.PrepareSave(original.Registration, original.Snapshot, opened.Source.Text ?? string.Empty);
        await opened.Choose("Validate/compare registered profile instruction draft",
            $"Exact source: {original.Snapshot.Source.Location}\n"
            + "Complete frozen baseline and supported proposed bytes. Memory-only structural validation; no source read, save, model use, permission or semantic harmlessness. Save requires its own fresh exact confirmation.",
            original.Snapshot.Files.Single().Text, proposed.Text, ["Close"], token);
    }
}