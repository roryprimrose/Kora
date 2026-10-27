using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private CancellationTokenSource? refinementLifetime;
    private OwnedInstructionRefinement? pendingRefinement;
    private OwnedInstructionSnapshot? refinementSource;
    private bool IsRefinementSourceObserved => refinementSource is { } source && actions.IsRefinementSourceObserved(source);
    private long critiqueRevision;
    private long refinementEpoch;

    private void CancelRefinement()
    {
        Interlocked.Increment(ref refinementEpoch);
        pendingRefinement = null;
        refinementSource = null;
        refinementLifetime?.Cancel();
        window?.CancelReview();
    }

    private async Task RefineDraft(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        pendingRefinement = null;
        if (refinement is null || kind != ArtifactKind.Instruction || baseline is not { } original
            || selectedName is not { } name || opened.Source.IsReadOnly || uncertain || actions.IsSourceUnavailable
            || !string.Equals(opened.NameInput.Text, name, StringComparison.Ordinal)
            || opened.KindInput.SelectedItem is not ArtifactKind.Instruction
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow row && !string.Equals(row.Name, name, StringComparison.Ordinal))
        { throw new InvalidOperationException("Refine requires the current supported inspected Kora-owned Instruction and confirmed source. Prompts, profile originals and arbitrary destinations are excluded."); }
        var draft = opened.Source.Text ?? string.Empty;
        var critique = opened.Critique.Text ?? string.Empty;
        refinementSource = original;
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
            && actions.IsRefinementSourceObserved(original);
        try
        {
            _ = actions.PrepareSave(name, original, draft);
            if (await opened.Choose("Review local instruction refinement disclosure",
                "Send only the complete current instruction body and unchanged critique to the admitted local model. "
                + "No tools, history, memory, attachments, reference execution or hosted fallback. "
                + "Metadata, kind, name, source, encoding and Save authority remain host-owned. "
                + "Generation and proposal review do not modify the draft, save or enable it.\n\nCRITIQUE\n" + critique,
                draft, draft, ["Generate local body proposal", "Cancel"], cancellation.Token) != 0 || !Current()) { return; }
            var proposed = await refinement.Propose(original, draft, critique, async (proposal, admitted) =>
            {
                var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                timer.Tick += (_, _) => { if (!proposal.IsCurrent()) { opened.CancelReview(); } };
                timer.Start();
                try
                {
                    return await opened.Choose("Compare complete instruction body proposal",
                        "Left: unchanged current draft. Right: COMPLETE prepared replacement including host-owned metadata. "
                        + "Structural validation is not semantic harmlessness. References and text remain inert. "
                        + "Review for a separate normal Save; nothing is applied to the editor or written now. "
                        + "Save requires its own complete-source human confirmation; activation is always separate.\n\nCRITIQUE\n" + critique,
                        draft, proposal.Proposed.Text, ["Use proposal for next Save", "Cancel"], admitted) == 0 && proposal.IsCurrent();
                }
                finally { timer.Stop(); }
            }, Current, cancellation.Token);
            if (!Current()) { return; }
            pendingRefinement = proposed;
            opened.Report(proposed is null
                ? "Proposal review cancelled. Draft and critique retained; nothing written or enabled."
                : "Complete replacement reviewed in memory. Draft unchanged. Select normal Save to review and confirm the exact replacement; no automatic save or activation.");
        }
        finally
        {
            refinementLifetime = null;
            if (pendingRefinement is null) { refinementSource = null; }
        }
    }
}
