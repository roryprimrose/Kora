using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<KnowledgeSourceCaptureLimitsCommand, Func<bool>, Task>? KnowledgeSourceCaptureLimitsRequested;

    private async Task ExecuteKnowledgeSourceCaptureLimits(KnowledgeSourceCaptureLimitsCommand command, string text,
        ManualCallInput input, Func<bool>? queuedEligibility)
    {
        var originalCurrent = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        var hostCurrent = CaptureKnowledgeSourceAnswerEligibility(input.Origin);
        bool Current() => originalCurrent() && hostCurrent();
        if (IsResponseInteractionPending || !Current()
            || !isAssistantNameAvailable && commandRouter.IsActivationPrefixed(text, AssistantName)
            || input.Origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(text))
        {
            ReportHostInteractionFailure("Source capture limits require original private typed input or current-name activated voice, without a pending interaction.");
            return;
        }
        if (command.Invalid || KnowledgeSourceCaptureLimitsRequested is not { } route)
        {
            ReportHostInteractionFailure(command.Invalid
                ? "Use source capture limits get/lower/reset \"Exact source name\", or source capture limits confirm/cancel. Lower requires three positive counts in files, per-file bytes and total bytes."
                : "Source capture limits configuration is unavailable. No preference or source changed.");
            return;
        }
        await uiDispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(input.Origin, () => route(command, Current)));
    }
}
