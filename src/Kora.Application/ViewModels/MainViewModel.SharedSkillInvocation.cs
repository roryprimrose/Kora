using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<SharedSkillInvocationCommand, RequestOrigin, Func<bool>, Task>? SharedSkillInvocationRequested;
    private Func<Task>? sharedSkillUseShutdown;
    public void BindSharedSkillUseShutdown(Func<Task>? shutdown) => sharedSkillUseShutdown = shutdown;
    private Task CloseSharedSkillUse() => sharedSkillUseShutdown?.Invoke() ?? Task.CompletedTask;

    public event Func<NamedSkillCommand, RequestOrigin, OwnedSkillAuthoringAuthority, Task>? NamedSkillInvocationRequested;
    private Func<Task>? namedSkillUseShutdown;
    private Func<Func<bool>>? namedSkillChoiceCapture;
    public void BindNamedSkillUseShutdown(Func<Task>? shutdown) => namedSkillUseShutdown = shutdown;
    public void BindNamedSkillChoiceCapture(Func<Func<bool>>? capture) => namedSkillChoiceCapture = capture;
    private Task CloseNamedSkillUse() => namedSkillUseShutdown?.Invoke() ?? Task.CompletedTask;

    private async Task ExecuteNamedSkillInvocation(NamedSkillCommand command, string text,
        ManualCallInput input, Func<bool>? queuedEligibility)
    {
        var sourceEligible = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        bool Eligible() => sourceEligible() && (command.Action is not "Choice" || input.NamedSkillChoiceEligible());
        Func<bool> eligible = Eligible;
        if (IsResponseInteractionPending || !eligible()
            || !isAssistantNameAvailable && commandRouter.IsActivationPrefixed(text, AssistantName)
            || input.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || input.Origin == RequestOrigin.ActivatedVoice && !commandRouter.IsActivationPrefixed(text, AssistantName))
        {
            Transcript = "Named skill use requires original private typed input or current-name activated voice, without a pending question or approval.";
            return;
        }
        if (command.Action is "Invalid")
        {
            Transcript = "Use 'use skill <exact readable name>', 'skill choice <displayed number>' or 'skill close'. Nothing was run.";
            return;
        }
        if (NamedSkillInvocationRequested is not { } route)
        { Transcript = "Reviewed named skill use is unavailable in this host. No fallback was run."; return; }
        await uiDispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(input.Origin, async () =>
        {
            if (!eligible()) { ReportHostInteractionFailure("Original named skill input expired."); return; }
            var questions = command.Action is "Review" && input.Origin == RequestOrigin.ActivatedVoice
                ? new OwnedSkillQuestionContinuation(input.Eligible, input.SkillQuestionEligible, () => voiceRecognition.Generation) : null;
            var current = questions is null ? eligible : CaptureSkillReviewEligibility(input, questions);
            await route(command, input.Origin, new(input.Origin, current) { Questions = questions });
        }, operation: HostOperation.Request));
    }

    private async Task ExecuteSharedSkillInvocation(SharedSkillInvocationCommand command, string text,
        ManualCallInput input, Func<bool>? queuedEligibility)
    {
        var eligible = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        if (IsResponseInteractionPending || !eligible()
            || !isAssistantNameAvailable && commandRouter.IsActivationPrefixed(text, AssistantName)
            || input.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || input.Origin == RequestOrigin.ActivatedVoice && !commandRouter.IsActivationPrefixed(text, AssistantName))
        {
            Transcript = "Shared skill use requires original private typed input or current-name activated voice, without a pending question or approval.";
            return;
        }
        if (SharedSkillInvocationRequested is not { } route)
        { Transcript = "Reviewed shared skill use is unavailable in this host."; return; }
        await uiDispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(input.Origin,
            () => route(command, input.Origin, eligible), operation: HostOperation.Request));
    }
}
