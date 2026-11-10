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
        if (pendingRefinement is null && refinementLifetime is null) { return; }
        Interlocked.Increment(ref refinementEpoch);
        if (pendingRefinement is not null) { validated = null; }
        pendingRefinement = null;
        refinementLifetime?.Cancel();
        if (refinementLifetime is not null) { window?.RetireReview(); }
    }

    private enum GuidanceOperation { Generate, Refine }

    private async Task ProposeGuidance(OwnedSkillEditorView opened, OwnedSkillAuthoringAuthority request, GuidanceOperation operation,
        CancellationToken token)
    {
        var previous = pendingRefinement ?? (operation == GuidanceOperation.Refine ? generatedBase : null);
        var draft = previous?.Proposed ?? opened.Document();
        if (previous is not null && !ReferenceEquals(previous, generatedBase) && !previous.IsCurrent())
        { throw new InvalidOperationException("The staged proposal expired. Generate and review again."); }
        CancelRefinement();
        validated = null;
        var initial = operation == GuidanceOperation.Generate;
        var critiqueInput = initial ? initialAuthority : critiqueAuthority;
        if (refinement is null || uncertain || critiqueInput is null)
        { throw new InvalidOperationException("Local guidance refinement is unavailable, publication is uncertain, or an original critique is required. Draft retained."); }
        if (!initial && initialAuthority is not null && previous is null)
        { throw new InvalidOperationException("Generate and review initial guidance before refining it."); }
        var critique = initial ? opened.Purpose.Text ?? "" : opened.Critique.Text ?? "";
        var exact = Interlocked.Read(ref generation);
        var epoch = Interlocked.Read(ref refinementEpoch);
        var originalSource = source;
        var mcp = draft.Tool == OwnedSkillToolSelection.ReadOnlyMcp;
        var authority = new OwnedSkillAuthoringAuthority(critiqueInput.Origin,
            () => Eligible() && request.IsCurrent() && critiqueInput.IsCurrent());
        var generationInput = initial
            ? new OwnedSkillGenerationRequest(critique, opened.ExpectedInput.Text ?? "", opened.ExpectedOutput.Text ?? "", authority)
            : null;
        var format = draft.InputTemplate is null ? "native v1 literal guidance" : "native v3 required literal guidance";
        var requiredInputs = draft.InputTemplate is null ? "(none)" : string.Join(", ", draft.InputTemplate.RequiredNames);
        if (generationInput is not null && (string.IsNullOrWhiteSpace(generationInput.Purpose)
            || string.IsNullOrWhiteSpace(generationInput.Input) || string.IsNullOrWhiteSpace(generationInput.Output)))
        { throw new InvalidDataException("Supply the bounded purpose, expected input (or explicitly none) and expected output. Nothing sent or saved."); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        refinementLifetime = cancellation;
        bool Current() => Eligible() && authority.IsCurrent() && !cancellation.IsCancellationRequested
            && exact == Interlocked.Read(ref generation) && epoch == Interlocked.Read(ref refinementEpoch)
            && ReferenceEquals(source, originalSource);
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { if (!Current()) { opened.RetireReview(); cancellation.Cancel(); } };
        timer.Start();
        try
        {
            var disclosure = initial
                ? "Send only this unchanged original purpose, the two explicit input/output fields and the exact user-declared required names "
                    + "to one admitted local tools-none model. The host preserves " + format
                    + ", local runtime, tools none, references none and the owned destination. "
                    + "The model may return only complete guidance using exactly the declared names in first-use order; "
                    + "no inferred names, metadata, capabilities, paths, scripts or files.\n\nPURPOSE\n"
                    + critique + "\n\nEXPECTED INPUT\n" + generationInput!.Input + "\n\nEXPECTED OUTPUT\n" + generationInput.Output
                    + "\n\nFROZEN REQUIRED INPUT NAMES (FIRST-USE ORDER)\n" + requiredInputs
                : "Send only the complete current guidance and unchanged original critique to one admitted local model with tools none. "
                + (mcp ? "The package declares one fixed read-only MCP capability, but authoring cannot use it. "
                    + "A still-held independently admitted trusted descriptor is required; no connection or refresh will be attempted. " : "")
                + "No history, memory, attachments, references, network or hosted fallback. Metadata, required input names, format, "
                + "ID/version, paths and publication authority cannot be chosen by the model. The draft is not changed or saved.\n\nCRITIQUE\n" + critique;
            if (await opened.ChooseGuidance(initial ? "Review initial skill generation disclosure" : "Review local skill refinement disclosure",
                disclosure, initial ? "" : draft.Guidance, initial ? "" : draft.Guidance,
                ["Generate local guidance proposal", "Cancel"], Current, cancellation.Token) != 0 || !Current())
            { return; }
            async Task<bool> Review(OwnedSkillGuidanceRefinement proposal, CancellationToken admitted)
            {
                if (initial)
                {
                    return await opened.ChooseInitial("Review complete generated skill source",
                        "Exact host-chosen owned destination: " + proposal.Destination
                        + "\nComplete " + format + " source, local runtime, tools none, references none. Required names: "
                        + requiredInputs + ". Inert untrusted data, not authority. "
                        + "Accept stages only this exact source. Validate and Save still require separate complete review and publication confirmation. "
                        + "Saved disabled; no grants, trust, applicability, enablement or invocation.\n\n" + disclosure,
                        proposal.Proposed.ManifestText, proposal.Proposed.Guidance, ["Use proposal for next Save", "Reject proposal"],
                        () => Current() && proposal.IsCurrent(), admitted) == 0 && proposal.IsCurrent();
                }
                return await opened.ChooseGuidance("Compare complete skill guidance proposal",
                    "Complete current and proposed guidance. Metadata and input names are unchanged. Content remains inert and untrusted; "
                    + (mcp ? "Declared package capability: read-only MCP; authoring model tools: none. "
                        + "Complete unchanged new-version manifest:\n" + draft.ManifestText + "\n" : "")
                    + "structural validation is not semantic harmlessness. Accept only stages this proposal. Validate, Save and the separate "
                    + "native publication confirmation are still required. The saved package remains disabled; enable/invoke are separate.\n\nCRITIQUE\n" + critique,
                    mcp ? draft.ManifestText + "\ninstructions.md\n" + draft.Guidance : draft.Guidance,
                    mcp ? proposal.Proposed.ManifestText + "\ninstructions.md\n" + proposal.Proposed.Guidance : proposal.Proposed.Guidance,
                    ["Use proposal for next Save", "Reject proposal"],
                    () => Current() && proposal.IsCurrent(), admitted) == 0 && proposal.IsCurrent();
            }
            pendingRefinement = generationInput is not null
                ? await refinement.ProposeInitial(draft, generationInput, Review, Current, cancellation.Token)
                : await refinement.Propose(draft, originalSource, critique, authority, Review, Current, cancellation.Token, previous);
            if (!Current()) { pendingRefinement = null; return; }
            if (initial && pendingRefinement is not null) { generatedBase = pendingRefinement; }
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
