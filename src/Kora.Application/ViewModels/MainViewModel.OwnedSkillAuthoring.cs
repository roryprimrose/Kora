using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<OwnedSkillAuthoringCommand, OwnedSkillAuthoringAuthority, Task>? ownedSkillAuthoring;
    private Func<Task<bool>>? ownedSkillAuthoringShutdown;
    private long ownedSkillAuthoringBindingRevision;

    public Func<bool> CaptureOwnedSkillFixtureEligibility()
    {
        var input = CaptureInstructionRefinementEligibility();
        var mode = providerModeConfiguration?.Get().Revision;
        var profile = providerProfileConfiguration?.Get().Revision;
        return () => input() && mode == providerModeConfiguration?.Get().Revision
            && profile == providerProfileConfiguration?.Get().Revision;
    }

    public void BindOwnedSkillAuthoring(
        Func<OwnedSkillAuthoringCommand, OwnedSkillAuthoringAuthority, Task>? handler, Func<Task<bool>>? shutdown)
    {
        ownedSkillAuthoring = handler;
        ownedSkillAuthoringShutdown = shutdown;
        Interlocked.Increment(ref ownedSkillAuthoringBindingRevision);
    }

    private async Task ExecuteOwnedSkillAuthoring(OwnedSkillAuthoringCommand command, string text,
        ManualCallInput input, Func<bool>? queuedEligibility)
    {
        if (command.Operation is "Invalid")
        { ReportHostInteractionFailure(OwnedSkillAuthoringCommand.Syntax); return; }
        if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(text, AssistantName)
            || input.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || input.Origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(text))
        { ReportHostInteractionFailure("Skill authoring requires original typed input or current-name activated voice."); return; }
        var captured = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        var privacy = CaptureInstructionEditorEligibility();
        var handler = ownedSkillAuthoring;
        var binding = Interlocked.Read(ref ownedSkillAuthoringBindingRevision);
        await uiDispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(input.Origin, async () =>
        {
            var authority = new OwnedSkillAuthoringAuthority(input.Origin,
                () => captured() && privacy() && !IsProtectedCall && !IsResponseInteractionPending && ReferenceEquals(handler, ownedSkillAuthoring)
                    && binding == Interlocked.Read(ref ownedSkillAuthoringBindingRevision));
            if (handler is null || !authority.IsCurrent())
            { ReportHostInteractionFailure("Owned skill authoring is unavailable or its original authority expired."); return; }
            await handler(command, authority);
        }));
    }
}
