using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private CancellationTokenSource? refinementLifetime;
    private OwnedInstructionRefinement? pendingRefinement;
    private OwnedInstructionSnapshot? refinementSource;
    private string? generationName;
    private bool generatedDraft;
    private bool stagingInitialProposal;
    private bool IsRefinementSourceObserved => refinementSource is { } source ? actions.IsRefinementSourceObserved(source)
        : generationName is { } name && actions.IsNewInstructionTargetObserved(name);
    private long critiqueRevision;
    private long refinementEpoch;

    private void CancelRefinement()
    {
        Interlocked.Increment(ref refinementEpoch);
        pendingRefinement = null;
        refinementSource = null;
        generationName = null;
        refinementLifetime?.Cancel();
        window?.CancelReview();
    }

    private async Task RefineDraft(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        pendingRefinement = null;
        var original = baseline;
        var initial = original is null && newDraft;
        if (refinement is null || kind != ArtifactKind.Instruction || original is null && !initial
            || selectedName is not { } name || opened.Source.IsReadOnly || uncertain || actions.IsSourceUnavailable
            || profileOriginal is not null
            || !string.Equals(opened.NameInput.Text, name, StringComparison.Ordinal)
            || opened.KindInput.SelectedItem is not ArtifactKind.Instruction
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow row && !string.Equals(row.Name, name, StringComparison.Ordinal))
        { throw new InvalidOperationException("Ask Kora requires the current supported inspected or explicitly created Kora-owned Instruction. Prompts, profile originals and arbitrary destinations are excluded."); }
        var draft = opened.Source.Text ?? string.Empty;
        var critique = opened.Critique.Text ?? string.Empty;
        refinementSource = original;
        generationName = initial ? name : null;
        var sourceEpoch = Interlocked.Read(ref sourceRevision);
        var inputEpoch = Interlocked.Read(ref critiqueRevision);
        var proposalEpoch = Interlocked.Read(ref refinementEpoch);
        var channel = main.CaptureInstructionRefinementEligibility();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        refinementLifetime = cancellation;
        bool Current() => !cancellation.IsCancellationRequested && eligible() && channel()
            && ReferenceEquals(baseline, original) && kind == ArtifactKind.Instruction && !uncertain
            && sourceEpoch == Interlocked.Read(ref sourceRevision)
            && inputEpoch == Interlocked.Read(ref critiqueRevision)
            && proposalEpoch == Interlocked.Read(ref refinementEpoch)
            && IsRefinementSourceObserved;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { if (!Current()) { opened.CancelReview(); cancellation.Cancel(); } };
        timer.Start();
        try
        {
            _ = actions.PrepareSave(name, original, draft);
            var disclosure = initial
                ? "Send ONLY the unchanged original description and this fixed context to one admitted local tools-none model:\n"
                    + OwnedInstructionRefinementService.InitialContext + "\n"
                    + "No draft, editable metadata, destination ID, source, references, history, memory or attachments are sent. "
                    + "The model may propose only literal future guidance BODY, not current instructions or host policy.\n"
                    + "Best-effort reject-only credential screening can miss secrets; do not include credentials. No secret-free guarantee. "
                    + "Host-owned destination kind: Instruction; native kora-format: 1; owned ID: " + name
                    + "; destination: " + Path.Combine(paths.RoamingRoot, "Instructions", name + ".instructions.md")
                    + ". No model-selected paths, metadata, capabilities, grants, autosave or activation.\n\nORIGINAL DESCRIPTION\n" + critique
                : "Send only the complete current instruction body and unchanged critique to the admitted local model. "
                + "No tools, history, memory, attachments, reference execution or hosted fallback. "
                + "Metadata, kind, name, source, encoding and Save authority remain host-owned. "
                + "Generation and proposal review do not modify the draft, save or enable it.\n\nCRITIQUE\n" + critique;
            if (await opened.Choose(initial ? "Review new instruction generation disclosure" : "Review local instruction refinement disclosure",
                disclosure, initial ? null : draft, initial ? null : draft,
                ["Generate local body proposal", "Cancel"], cancellation.Token) != 0 || !Current()) { return; }
            async Task<bool> Review(OwnedInstructionRefinement proposal, CancellationToken admitted)
            {
                    return await opened.Choose(initial ? "Review complete new instruction proposal" : "Compare complete instruction body proposal",
                        "Left: unchanged current draft. Right: COMPLETE prepared replacement including host-owned metadata. "
                        + "Structural validation is not semantic harmlessness. References and text remain inert. "
                        + (initial ? "Accept stages this complete source in the existing editor only. The new destination must stay absent; no overwrite. "
                            : "Review for a separate normal Save; nothing is applied to the editor or written now. ")
                        + "Save requires its own complete-source human confirmation; activation is always separate.\n\nCRITIQUE\n" + critique,
                        draft, proposal.Proposed.Text, ["Use proposal for next Save", "Cancel"], admitted) == 0 && proposal.IsCurrent();
            }
            var proposed = initial
                ? await refinement.ProposeInitial(name, draft, critique, Review, Current, cancellation.Token)
                : await refinement.Propose(original!, draft, critique, Review, Current, cancellation.Token);
            if (!Current()) { return; }
            if (initial && proposed is not null)
            {
                stagingInitialProposal = true;
                try { opened.Source.Text = proposed.Proposed.Text; }
                finally { stagingInitialProposal = false; }
                sourceEpoch = Interlocked.Read(ref sourceRevision);
                generatedDraft = true;
            }
            pendingRefinement = proposed;
            opened.Report(proposed is null
                ? "Proposal review cancelled. Draft and critique retained; nothing written or enabled."
                : initial ? "Complete source staged in memory. Select separate Save for exact create-only confirmation. Saved disabled; no activation, grants or execution."
                : "Complete replacement reviewed in memory. Draft unchanged. Select normal Save to review and confirm the exact replacement; no automatic save or activation.");
        }
        finally
        {
            timer.Stop();
            refinementLifetime = null;
            if (pendingRefinement is null) { refinementSource = null; generationName = null; }
        }
    }
}
