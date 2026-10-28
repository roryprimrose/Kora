using Kora.Application.Definitions;
using Kora.Core.Customizations;

namespace Kora;

internal sealed partial class CustomizationWindowController
{
    private static bool IsInstructionOriginal(CustomizationSource? selected) =>
        selected is { Kind: CustomizationKind.Instruction, Scope: CustomizationScope.User };

    private async Task RegisterOriginal(Func<bool> current, CancellationToken token)
    {
        var selected = source ?? throw new InvalidOperationException("Select one exact personal instruction original.");
        var revision = draftRevision;
        bool Exact() => current() && draftRevision == revision && source == selected;
        if (!await management.RegisterInstruction(selected, async (proposal, admitted) =>
            await window!.Review("Review personal instruction source registration",
                "EXACT PERSONAL SOURCE\n" + proposal.Source.Location
                + "\n\nRegister this one existing canonical personal .instructions.md original for this window's lifetime."
                + "\nAfter registration the host may read complete source and prepare source-preserving edits only for native kora-format: 1 instructions."
                + "\nStrict UTF-8; at most 65,536 file bytes including BOM; at most 16 scalar header fields / 4,096 header bytes."
                + "\nUnsupported metadata, scope/globs, hooks, nonlocal sources and other kinds are refused rather than rewritten."
                + "\nThis expands read-only source scope deliberately. It does NOT approve a write, activation, tool, model disclosure or external editor."
                + "\nThe original is shared with other applications and external sync. Every write requires a separate complete native original/proposed review."
                + "\nNative single-file writes use optimistic final identity/byte conflict detection, not content compare-and-swap."
                + "\nWithdraw/close cancels pending access and writes without deleting originals or Kora copies. No registration persists to disk."
                + "\nInstead, use the owned instruction editor's existing Create Kora copy from profile route for a compatible original.",
                admitted, "Register exact original", "Cancel") is "Register exact original", Exact, token)) { return; }
        RequireCurrent(Exact, token);
        registeredOriginal = selected;
        if (baseline is null) { await Inspect(selected, false, current, token); }
        else { ShowDraft(); }
        window!.Status.Text = "Exact original registered for this window. The existing base and dirty draft were retained. Compare external before rebasing a changed/replaced source; registration did not approve a save or enable content.";
    }

    private async Task WithdrawOriginal(Func<bool> current, CancellationToken token)
    {
        var selected = source ?? throw new InvalidOperationException("Select the registered personal instruction.");
        if (await window!.Review("Withdraw personal instruction registration", selected.Location
            + "\nStop source reads and pending writes. Clear this window's private source and draft."
            + "\nOriginal files and independent Kora copies are NOT deleted.", token, "Withdraw and clear", "Cancel") is not "Withdraw and clear")
        { return; }
        await management.WithdrawInstruction(selected, current, token);
        registeredOriginal = null;
        baseline = null; draft = []; selectedFile = null; draftRevision++;
        ShowDraft();
        window.Status.Text = "Exact-source registration withdrawn. Original and Kora copies retained; no pending source write is authorized.";
    }

    private async Task CompareOriginal(Func<bool> current, CancellationToken token)
    {
        if (!IsInstructionOriginal(source) || baseline is null)
        { throw new InvalidOperationException("Inspect one registered native instruction original before comparing its retained draft."); }
        var selected = source!;
        FlushBuffer();
        var revision = draftRevision;
        var retained = draft.ToArray();
        bool Exact() => current() && source == selected && draftRevision == revision && management.IsInstructionRegistered(selected);
        var external = await management.Read(selected, false, Exact, token);
        RequireCurrent(Exact, token);
        var text = "SHARED ORIGINAL\n" + selected.Location
            + "\n\nRETAINED BASE [UTF-8 BOM: " + baseline.Files.Single().HasBom + "]\n" + baseline.Files.Single().Text
            + "\n\nRETAINED COMPLETE DRAFT [UTF-8 BOM: " + retained.Single().HasBom + "]\n" + retained.Single().Text
            + "\n\nCURRENT EXTERNAL SOURCE [UTF-8 BOM: " + external.Files.Single().HasBom + "]\n" + external.Files.Single().Text
            + "\n\nRebase keeps the complete draft unchanged but adopts this exact current base."
            + "\nIt does not merge external changes or adopt an external BOM/newline change, write a file or enable content. A separate fresh Save review is mandatory."
            + "\nDiscard and reload replaces only the in-memory draft. Keep draft/Cancel retain the previous base."
            + "\nCreate Kora copy remains an independent destination choice.";
        var choice = await window!.Review("Compare personal instruction original", text, token,
            "Keep draft", "Rebase complete draft", "Discard and reload", "Cancel");
        RequireCurrent(Exact, token);
        if (choice is not ("Rebase complete draft" or "Discard and reload")) { return; }
        var live = await management.Read(selected, false, Exact, token);
        if (!string.Equals(live.Revision, external.Revision, StringComparison.Ordinal))
        { throw new InvalidOperationException("The external source changed again during comparison. The previous base and dirty draft remain unchanged. Compare again."); }
        if (choice is "Rebase complete draft") { CustomizationManagementService.ValidatePrimary(selected, retained); }
        RequireCurrent(Exact, token);
        baseline = external;
        draft = (choice is "Rebase complete draft" ? retained : external.Files.ToArray()).ToList();
        draftRevision++;
        ShowDraft();
        window.Status.Text = choice is "Rebase complete draft"
            ? "Rebased the complete retained draft onto the exact reviewed external source. Nothing written or enabled; review and save separately."
            : "Discarded only the memory draft and reloaded the exact external source. Nothing written or enabled.";
    }
}