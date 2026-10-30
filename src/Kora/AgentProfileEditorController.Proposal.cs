using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Core.Agents;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class AgentProfileEditorController
{
    private CancellationTokenSource? proposalLifetime;
    private Func<bool>? descriptionEligible;
    private RequestOrigin descriptionOrigin;
    private long descriptionRevision;
    private OwnedAgentProfileDestination? proposalDestination;
    private Func<bool>? proposalEligible;

    private void DescriptionEdited(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != TextBox.TextProperty) { return; }
        Interlocked.Increment(ref descriptionRevision);
        descriptionEligible = main.CaptureInstructionRefinementEligibility();
        descriptionOrigin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        Retire();
    }

    private async Task Propose(AgentProfileEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        Retire();
        if (authoring is null || descriptionEligible is not { } descriptionInput)
        { throw new InvalidOperationException("Local conversational authoring is unavailable or requires an original description. Manual authoring remains available."); }
        var name = draftName!;
        var source = opened.Source.Text ?? string.Empty;
        var request = opened.DescriptionRequest.Text ?? string.Empty;
        _ = OwnedAgentProfileDocument.ProposeToolFree(name, source, "Authoring validation", "Inert validation");
        if (string.IsNullOrWhiteSpace(request)) { throw new InvalidDataException("Describe the local text-only agent or refinement first."); }
        var input = originalEligible!;
        var exact = Interlocked.Read(ref revision);
        var requestRevision = Interlocked.Read(ref descriptionRevision);
        var candidates = catalogue?.SourceCandidates;
        var useRevision = catalogue?.UseControlRevision;
        var failures = catalogue?.ReloadFailureRevision;
        var clock = time ?? TimeProvider.System;
        var started = clock.GetUtcNow();
        var expires = started.AddMinutes(2);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        proposalLifetime = cancellation;
        bool Authority() => Eligible() && input() && descriptionInput()
            && requestRevision == Interlocked.Read(ref descriptionRevision)
            && clock.GetUtcNow() >= started && clock.GetUtcNow() < expires
            && (catalogue is null || !catalogue.IsReloadUnavailable
                && ReferenceEquals(candidates, catalogue.SourceCandidates)
                && useRevision == catalogue.UseControlRevision && failures == catalogue.ReloadFailureRevision);
        bool Current() => Authority() && eligible() && !cancellation.IsCancellationRequested
            && exact == Interlocked.Read(ref revision);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { if (!Current()) { cancellation.Cancel(); opened.RetireReview(); } };
        timer.Start();
        try
        {
            var destination = await workflow.PrepareNewAsync(name, Current, cancellation.Token);
            if (proposalDestination is { } previous && destination != previous)
            { throw new InvalidOperationException("The original proposal destination changed. No replacement root is approved."); }
            async Task RequireDestination()
            {
                if (await workflow.PrepareNewAsync(name, Current, cancellation.Token) != destination)
                { throw new InvalidOperationException("The reviewed new destination changed. Nothing staged or saved."); }
            }
            if (await opened.Choose("Review local agent authoring disclosure",
                "Send only the EXACT REQUEST and COMPLETE CURRENT DRAFT below to one admitted local tool-free model. "
                + "No history, memory, attachments, references, tools, network or hosted fallback. "
                + "The model may return only description and guidance; identity, local text format, limits and create-only destination stay host-owned. "
                + "An initial draft uses v1, references/tools none, zero calls, one round, 16000 input bytes, 2000 output bytes and 15000ms deadline. "
                + "Existing v1 metadata is preserved. Generation never saves, enables or invokes an agent.\n\n"
                + $"DESTINATION (not sent to model)\n{destination.DisplayPath}\n\nEXACT REQUEST\n{request}\n\nCOMPLETE CURRENT DRAFT\n{source}",
                ["Generate local agent proposal", "Cancel"], cancellation.Token) != 0 || !Current()) { return; }
            await RequireDestination();
            var origin = originalOrigin == RequestOrigin.ActivatedVoice || descriptionOrigin == RequestOrigin.ActivatedVoice
                ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
            ActivityLink[] links = HostActivity.Current?.Activity is { } parent ? [new(parent.Context)] : [];
            using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request, links);
            var proposed = await authoring.ProposeAgent(name, source, request, Current, cancellation.Token);
            await RequireDestination();
            if (!Current()) { throw new InvalidOperationException("Original agent authoring input retired; draft retained."); }
            if (await opened.Choose("Compare complete agent proposal",
                $"NEW CREATE-ONLY DESTINATION\n{destination.DisplayPath}\n\nEXACT REQUEST\n{request}\n\n"
                + $"BEFORE: COMPLETE CURRENT DRAFT\n{source}\n\nAFTER: COMPLETE PROPOSED SOURCE\n{proposed.Document.Text}\n\n"
                + "All source is inert untrusted data. Structural compatibility is not semantic harmlessness. "
                + "Use proposed draft stages memory only. Refine or edit it, then independently Review Save as new and confirm the exact source/destination. "
                + "Save remains disabled and unselected; no activation, hosting or invocation is approved.",
                ["Use proposed draft", "Reject proposal"], cancellation.Token) != 0 || !Current())
            { activity.Complete(HostOperationOutcome.Cancelled); return; }
            await RequireDestination();
            if (!Current()) { throw new InvalidOperationException("The proposal expired before adoption. Draft retained."); }
            // Staging intentionally advances the raw draft revision, not the original description authority.
            proposalLifetime = null;
            proposalEligible = Authority;
            originalOrigin = origin;
            proposalDestination = destination;
            opened.Source.Text = proposed.Document.Text;
            opened.Status.Text = "Validated complete agent source staged in memory. Refine/review, then separately confirm Save as new. Nothing saved, enabled or invoked.";
            activity.Complete(HostOperationOutcome.Completed);
        }
        finally
        {
            timer.Stop();
            proposalLifetime = null;
        }
    }
}
