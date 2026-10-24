using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<InstructionUseCommand, RequestOrigin, Func<bool>, Guid?, Task<Guid?>>? InstructionUseRequested;
    private Func<Task>? instructionUseShutdown;
    private Guid? instructionUseReview;
    private long instructionUseReviewRevision;

    public void BindInstructionUseShutdown(Func<Task>? shutdown) => instructionUseShutdown = shutdown;
    public void RetireInstructionUseReview(Guid? expected)
    {
        if (instructionUseReview == expected) { instructionUseReview = null; }
    }
    private Task CloseInstructionUse() => instructionUseShutdown?.Invoke() ?? Task.CompletedTask;

    private async Task ExecuteInstructionUseCommand(InstructionUseCommand command, string text,
        ManualCallInput? originalInput, Func<bool>? queuedEligibility)
    {
        var origin = Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin;
        if (origin == RequestOrigin.ActivatedVoice && (originalInput is null || queuedEligibility is null))
        { ReportHostInteractionFailure("Original queued voice admission is unavailable; no instruction request was dispatched."); return; }
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var eligible = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        if (IsResponseInteractionPending)
        { Transcript = "Instruction requests are unavailable while a question or security review is pending."; return; }
        if (command.Error is not null)
        { ReportHostInteractionFailure(command.Error); return; }
        if (!eligible() || !CanRevealPrivatePresentation || IsProtectedCall
            || input.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || input.Origin == RequestOrigin.ActivatedVoice && !commandRouter.IsActivationPrefixed(text, AssistantName))
        { ReportHostInteractionFailure("Instruction requests require original private typed input or current-name activated voice without a pending question or approval."); return; }
        if (command.Action is "Confirm" && (instructionUseReview is null
            || input.Origin == RequestOrigin.ActivatedVoice && input.InputRevision < instructionUseReviewRevision))
        { ReportHostInteractionFailure("This confirmation did not arrive after the exact pending review. Review first, then separately confirm."); return; }
        if (InstructionUseRequested is not { } route)
        { ReportHostInteractionFailure("One-shot owned instruction use is unavailable in this host."); return; }
        var expected = instructionUseReview;
        var result = await route(command, input.Origin, eligible, expected);
        instructionUseReview = result;
        if (result is not null && command.Action is "Review")
        {
            instructionUseReviewRevision = Interlocked.Increment(ref workspaceSurfaceInputRevision);
        }
    }
}
