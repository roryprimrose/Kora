using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Skills;

namespace Kora;

internal sealed partial class NativeQuestionHost
{
    internal async Task<HostInteractionDecision> RequestBundledPermissionAsync(
        HostOperationProposal proposal, BundledFixedScript script, ExactOperationBinding expected,
        HostRevision generation, Func<bool> admitted, Func<NativeQuestionViewModel, Task> present, CancellationToken token)
    {
        var request = proposal.Request;
        if (disposed || !canInteract() || !admitted() || HostActivity.RequireCurrent().Request != request
            || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || proposal.Binding != expected || !script.Package.HasSameDeclaredContent(expected)
            || proposal.Effect != HostOperationEffect.BoundedWrite || callObservation is null
            || Interlocked.CompareExchange(ref waiting, 1, 0) != 0)
        { throw new InvalidOperationException("The exact bundled package permission presenter is unavailable."); }
        var ownership = admissionRevision?.Invoke();
        var privacy = privateRevision?.Invoke();
        bool Eligible() => !disposed && canInteract() && admitted() && !token.IsCancellationRequested
            && time.GetUtcNow() < proposal.ExpiresAt
            && ownership == admissionRevision?.Invoke() && privacy == privateRevision?.Invoke();
        currentAdmission = Eligible;
        NativeQuestionViewModel? opened = null;
        try
        {
            var task = await store.ReadTaskAsync(request.SessionId, request.TaskId, token);
            if (!Eligible() || task is not { CurrentSource: true, Task.IsTerminal: false }
                || !request.IsWithinIntent(task.Task.Request) || task.Generation != generation)
            { throw new InvalidOperationException("The original bundled control source is no longer current."); }
            await store.PublishTrustedSnapshotAsync(request,
                callObservation().Authorization(Eligible(), mandatoryGatesSatisfied: true),
                proposal, expectedObservationRevision: 0, token);
            var spec = new QuestionSpec(
                $"Grant the displayed immutable {script.Package.Manifest.Name} package and its fixed {script.Action} effect? "
                + "Review every manifest, instruction, entry and shared helper tab. Original scripts run as separate in-memory blocks "
                + "inside a qualified contained PowerShell worker; the host independently authorizes one exact Windows operation. "
                + "No arbitrary scripts, extraction, elevation or forced application closure. "
                + "Shutdown/restart require separate fresh computer-power confirmation and physical resource joins. "
                + "A successful Windows return confirms request acceptance, not physical completion. "
                + "Exact grants use native controls even for a voice-origin request; separate computer-power consent uses the original channel. "
                + "Cancel denies this request without creating a grant.",
                QuestionKind.SingleChoice, [new("once", "Once"), new("session", "This session"), new("perpetual", "Always")],
                purpose: "authorization", sourceId: "bundled-fixed-script");
            var decision = await authorization.RequestGrantAsync(request, spec, token);
            if (decision.Outcome != HostInteractionOutcome.Presented) { return decision; }
            if (decision.Question is not { } initial || initial.SessionGeneration != generation || !Eligible())
            { throw new InvalidOperationException("The bundled package permission question was not durably presented."); }
            opened = new(initial, questions, authorization, reviews, Eligible, time, logger);
            current = opened;
            Interlocked.Exchange(ref currentFocusRevision, Interlocked.Read(ref focusRevision));
            opened.BindVoiceReplies(voiceReplies, () => ownership is not null && privacy is not null
                && ReferenceEquals(current, opened) && Eligible());
            await present(opened);
            var remaining = proposal.ExpiresAt - time.GetUtcNow();
            using var deadline = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, time);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            var completed = await opened.Completion.WaitAsync(cancellation.Token);
            if (!Eligible() || completed is not { Outcome: HostInteractionOutcome.Approved, Grant: { } grant }
                || completed.Question?.AnswerChannel != RequestOrigin.LocalUi)
            { return new(HostInteractionOutcome.Denied, "bundled-permission-not-approved"); }
            return await authorization.ConsumeAsync(request, grant.Id, grant.Revision, token);
        }
        finally
        {
            opened?.Close("Bundled package review ended. No prior answer can replay an effect.");
#pragma warning disable VSTHRD003 // Joins the existing native question and original-channel reply owners.
            if (opened is not null) { await opened.OperationQuiescence; }
            if (voiceReplies is not null) { await voiceReplies.Quiescence; }
#pragma warning restore VSTHRD003
            current = null;
            currentAdmission = null;
            Volatile.Write(ref waiting, 0);
        }
    }
}