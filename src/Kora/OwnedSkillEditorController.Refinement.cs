using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora;

internal sealed partial class OwnedSkillEditorController
{
    private OwnedDeclarativeSkillSnapshot? source;
    private OwnedSkillAuthoringAuthority? critiqueAuthority;
    private OwnedSkillGuidanceRefinement? pendingRefinement;
    private CancellationTokenSource? refinementLifetime;
    private long refinementEpoch;

    private void CancelRefinement()
    {
        Interlocked.Increment(ref refinementEpoch);
        if (pendingRefinement is not null) { validated = null; }
        pendingRefinement = null;
        refinementLifetime?.Cancel();
        window?.RetireReview();
    }

    private async Task Refine(OwnedSkillEditorWindow opened, OwnedSkillAuthoringAuthority request, CancellationToken token)
    {
        CancelRefinement();
        validated = null;
        if (refinement is null || uncertain || critiqueAuthority is not { } critiqueInput)
        { throw new InvalidOperationException("Local guidance refinement is unavailable, publication is uncertain, or an original critique is required. Draft retained."); }
        var draft = opened.Document();
        var critique = opened.Critique.Text ?? "";
        var exact = Interlocked.Read(ref generation);
        var epoch = Interlocked.Read(ref refinementEpoch);
        var originalSource = source;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        refinementLifetime = cancellation;
        var authority = new OwnedSkillAuthoringAuthority(critiqueInput.Origin,
            () => Eligible() && request.IsCurrent() && critiqueInput.IsCurrent());
        bool Current() => Eligible() && authority.IsCurrent() && !cancellation.IsCancellationRequested
            && exact == Interlocked.Read(ref generation) && epoch == Interlocked.Read(ref refinementEpoch)
            && ReferenceEquals(source, originalSource);
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { if (!Current()) { opened.RetireReview(); cancellation.Cancel(); } };
        timer.Start();
        try
        {
            if (await opened.ChooseGuidance("Review local skill refinement disclosure",
                "Send only the complete current guidance and unchanged original critique to one admitted local model with tools none. "
                + "No history, memory, attachments, references, network or hosted fallback. Metadata, required input names, format, "
                + "ID/version, paths and publication authority cannot be chosen by the model. The draft is not changed or saved.\n\nCRITIQUE\n" + critique,
                draft.Guidance, draft.Guidance, ["Generate local guidance proposal", "Cancel"], Current, cancellation.Token) != 0 || !Current())
            { return; }
            pendingRefinement = await refinement.Propose(draft, originalSource, critique, authority,
                async (proposal, admitted) => await opened.ChooseGuidance("Compare complete skill guidance proposal",
                    "Complete current and proposed guidance. Metadata and input names are unchanged. Content remains inert and untrusted; "
                    + "structural validation is not semantic harmlessness. Accept only stages this proposal. Validate, Save and the separate "
                    + "native publication confirmation are still required. The saved package remains disabled; enable/invoke are separate.\n\nCRITIQUE\n" + critique,
                    draft.Guidance, proposal.Proposed.Guidance, ["Use proposal for next Save", "Reject proposal"],
                    () => Current() && proposal.IsCurrent(), admitted) == 0 && proposal.IsCurrent(), Current, cancellation.Token);
            if (!Current()) { pendingRefinement = null; return; }
            if (pendingRefinement is not null) { dirty = true; invocation?.Retire(); }
            opened.Report(pendingRefinement is null
                ? "Proposal rejected. Draft and critique retained; nothing saved or enabled."
                : "Complete guidance proposal reviewed in memory. Draft unchanged. Validate, then Save for both existing publication reviews. No automatic save, enablement or invocation.");
        }
        finally
        {
            timer.Stop();
            refinementLifetime = null;
        }
    }
}
