using Kora.Core.Customizations;

namespace Kora;

internal sealed partial class CustomizationWindowController
{
    private async Task Delete(Func<bool> current, CancellationToken token)
    {
        if (source is null || baseline is null)
        { throw new InvalidOperationException("Inspect the exact saved Kora-owned definition before deletion."); }
        CustomizationDeletion.Validate(baseline);
        FlushBuffer();
        var selected = source;
        var reviewed = baseline;
        var revision = draftRevision;
        bool Exact() => current() && ReferenceEquals(source, selected) && ReferenceEquals(baseline, reviewed)
            && draftRevision == revision;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var text = "Delete exactly this Kora-owned single-file definition, not its directory or another source.\n"
            + selected.Kind + " / " + selected.Name + "\n" + selected.Location
            + "\n\nCOMPLETE REVIEWED DISK SOURCE\n" + reviewed.Files.Single().Text
            + "\n\nUnsaved editor content is retained, not saved or deleted. Source recovery must be completed first."
            + "\nThe source is held against future use before deletion. Existing reviews/selections retire through the same use policy and catalogue reload."
            + "\nCaptured admitted work is not physically cancelled. Independent grants, history and audit remain; no cascade or reference rewriting occurs."
            + "\nOther applications, sync and external references are not exhaustively inventoried. Restoring bytes does not enable them."
            + "\nNo backup/archive, automatic rollback or retry is provided.";
        var deleted = false;
        try
        {
            var outcome = await management.Delete(reviewed, async admitted =>
            {
                if (await window!.Review("Review exact owned definition deletion", text, admitted,
                    "Continue deletion", "Cancel") is not "Continue deletion") { return false; }
                RequireCurrent(Exact, admitted);
                return await window.Review("Confirm exact owned definition deletion",
                    text + "\n\nThis confirmation authorizes only the unchanged reviewed kind, source, root, revision and complete bytes.",
                    admitted, "Delete exact owned file", "Cancel") is "Delete exact owned file";
            }, Exact, deadline.Token);
            if (outcome is CustomizationDeletionOutcome.Cancelled)
            { window!.Status.Text = "Deletion cancelled. Source and editor draft unchanged."; return; }
            deleted = true;
            var reloaded = reload.ReloadNow();
            RequireCurrent(Exact, token);
            // Keep the raw draft and its baseline visible for explicit recovery, never a new-file Save.
            await Refresh(current, token);
            RequireCurrent(Exact, token);
            window!.Status.Text = reloaded
                ? "Exact owned source deleted and catalogue reloaded. Draft retained; Save cannot recreate the missing baseline. Use a distinct explicit Kora copy to recover it. No unrelated definition or grant was removed."
                : "Exact owned source deleted, but catalogue reload failed. Source remains held; draft retained. Resolve reload before further use. No rollback is claimed.";
        }
        catch (Exception exception) when ((deleted || exception is CustomizationDeletionUnconfirmedException)
            && exception is not OutOfMemoryException)
        {
            reload.ReloadNow();
            throw new CustomizationDeletionUnconfirmedException(exception);
        }
    }
}
