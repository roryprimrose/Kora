using Kora.Application.Diagnostics;
using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public sealed record SpokenToolApprovalTarget(
        HostQuestionRecord Question,
        Func<bool> IsCurrent,
        Func<RequestOrigin, Func<bool>, Task<HostInteractionDecision>> ReviewAsync,
        Func<SpokenApprovalReply, RequestOrigin, Func<bool>, Task<HostInteractionDecision>> AnswerAsync);

    private Func<IReadOnlyList<SpokenToolApprovalTarget>>? spokenToolApprovals;
    private long spokenToolApprovalBinding;
    private bool spokenToolApprovalPending;

    public void BindSpokenToolApprovals(Func<IReadOnlyList<SpokenToolApprovalTarget>> capture)
    {
        spokenToolApprovals = capture;
        Interlocked.Increment(ref spokenToolApprovalBinding);
    }

    private async Task ExecuteSpokenToolApprovalAsync(SpokenApprovalReply reply, string input,
        SecurityAuditInitiator initiator)
    {
        const string unavailable = "Tool approval reply refused. Use one exact current eligible card; nothing was approved, denied or sent to a model.";
        var origin = OriginalOrigin(initiator);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var callRevision = CallPolicyRevision;
        var binding = Interlocked.Read(ref spokenToolApprovalBinding);
        var ownerRevision = auditImpactOwnerRevision();
        var capture = spokenToolApprovals;
        bool Admitted() => !disposed && CanRevealPrivatePresentation && IsHostInputEligible
            && !IsResponseInteractionPending && composerDictation?.IsActive != true && promptInputVoice?.IsActive != true
            && questionVoiceReplies?.IsActive != true && activeCustomizationVoice is null
            && nameRevision == assistantNameConfiguration.Get().Revision && callRevision == CallPolicyRevision
            && ownerRevision == auditImpactOwnerRevision()
            && binding == Interlocked.Read(ref spokenToolApprovalBinding) && ReferenceEquals(capture, spokenToolApprovals)
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && voiceOwnershipEligible()
                && (!IsProtectedCall || CallObservation.Settings.AllowSpokenApprovalsDuringCalls));
        if (reply.Decision is null || spokenToolApprovalPending || !isAssistantNameAvailable
            || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(input)
            || !Admitted())
        { Transcript = unavailable; return; }

        spokenToolApprovalPending = true;
        try
        {
            var targets = capture?.Invoke()
                .Where(target => target.IsCurrent() && HostAuthorizationService.SupportsSpokenReply(target.Question))
                .ToArray() ?? [];
            if (targets.Length != 1) { Transcript = unavailable; return; }
            var target = targets[0];
            var review = await target.ReviewAsync(origin, Admitted);
            if (!Admitted() || !target.IsCurrent()
                || review.Outcome != HostInteractionOutcome.Presented
                || !HostAuthorizationService.MatchesReplyTarget(target.Question, review.Question))
            { Transcript = unavailable; return; }
            var decisionText = reply.Decision switch
            {
                SpokenApprovalReply.Operation.Once => "Approve this operation once.",
                SpokenApprovalReply.Operation.Session => "Approve this exact operation for this session.",
                _ => "Deny this operation.",
            };
            var readback = target.Question.Spec.Text + " Operation: "
                + target.Question.Proposal!.Binding.ActionId + ". " + decisionText;
            Transcript = readback;
            if (origin == RequestOrigin.ActivatedVoice)
            {
                await SpeakExactQuestionAsync(readback, () => Admitted() && target.IsCurrent(), CancellationToken.None);
            }
            if (!Admitted() || !target.IsCurrent()) { Transcript = unavailable; return; }
            var result = await target.AnswerAsync(reply, origin, Admitted);
            Transcript = result.Outcome switch
            {
                HostInteractionOutcome.Approved => "Exact tool approval committed. No other card was answered.",
                HostInteractionOutcome.Cancelled => "Exact tool approval denied. No grant was created.",
                _ => unavailable,
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Answering the exact tool approval");
            Transcript = unavailable;
        }
        finally { spokenToolApprovalPending = false; }
    }
}
