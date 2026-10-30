using Kora.Application.Hosting;
using Kora.Core;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private TaskResponseOutputReview? activeTaskResponseOutput;
    private (long Revision, bool Visible)? completedTaskResponseVisual;
    private bool? CompletedTaskResponseVisual => completedTaskResponseVisual is { } completed
        && completed.Revision == Interlocked.Read(ref webResponseRevision) ? completed.Visible : null;

    private TaskResponseOutputReview ReviewTaskResponseOutput(TaskResponseMode declared, RequestOrigin origin)
    {
        var call = CallPolicyRevision;
        var owner = Interlocked.Read(ref voiceOwnershipBindingRevision);
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        bool Admitted() => !disposed && IsHostInputEligible && IsCallMutationHostEligible
            && owner == Interlocked.Read(ref voiceOwnershipBindingRevision)
            && !IsResponseInteractionPending
            && communicationPolicy.CheckMutation(origin, call, () => IsCallMutationHostEligible) is null
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && !IsProtectedCall && recovery == Interlocked.Read(ref voiceRecoveryRevision));
        if (!Admitted() || responseModeConfiguration is not null && !responseModeConfiguration.Get().Available)
        { throw new InvalidOperationException("The original channel or response preference is unavailable. No one-task output choice was admitted."); }
        return new(declared, ResponseOutputModeResolver.Resolve(DefaultResponseMode, QueueResponseMode, TaskResponseMode),
            Admitted, PresentTaskResponseOutputAsync);
    }

    private Task PresentTaskResponseOutputAsync(ConversationSubmissionReceipt receipt, TaskResponseOutputReview output,
        Func<bool> admitted, CancellationToken token) => uiDispatcher.InvokeAsync(async () =>
    {
        token.ThrowIfCancellationRequested();
        if (!admitted() || IsSpeaking || IsCurrentResponseReadActive)
        {
            output.Status = "Task output withheld: the original presentation is unavailable or another response owns speech. Inspect the retained conversation; no replay.";
            if (CanRevealPrivatePresentation)
            { Transcript = output.Status; }
            return;
        }
        var answer = receipt.Message.AssistantResponse;
        var succeeded = receipt.Message.Outcome == ModelTurnOutcome.Succeeded && answer is not null;
        PresentResponse(succeeded ? AssistantState.Information : AssistantState.Failure,
            succeeded ? "Conversation answer" : "Conversation request did not complete",
            succeeded ? answer!.Body : "HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
                + ". No assistant answer is claimed. Inspect this exact conversation before a fresh request.",
            requestWindow: false, refreshOutput: false, sourceAssigned: () => activeTaskResponseOutput = output);
        var revision = Interlocked.Read(ref webResponseRevision);
        bool Current() => admitted() && revision == Interlocked.Read(ref webResponseRevision)
            && ReferenceEquals(activeTaskResponseOutput, output);
        sessionPresentationSources.Add(receipt.Message.Request.SessionId.Value);
        try
        {
            NotifyOutputPolicyChanged();
            var effective = EffectiveResponseMode;
            var spoke = false;
            if (IsVisualResponseVisible && CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
            if (succeeded)
            {
                spoke = await TrySpeakCurrentResponseAsync(additionalEligibility: Current, cancellationToken: token);
            }
            output.Status = "This task only - declared: " + output.Declared + "; configured: " + output.Configured
                + "; effective at output: " + effective + "; speech completed: " + spoke
                + "; full visual output: " + (Current() && IsVisualResponseVisible)
                + ". Mandatory policy wins; output choice is consumed with no replay.";
        }
        finally
        {
            if (revision == Interlocked.Read(ref webResponseRevision) && ReferenceEquals(activeTaskResponseOutput, output))
            {
                completedTaskResponseVisual = (revision, IsVisualResponseVisible);
                activeTaskResponseOutput = null;
                NotifyOutputPolicyChanged();
            }
        }
    });
}
