using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<CustomizationDiscoveryCommand, RequestOrigin, Func<bool>, Task>? CustomizationDiscoveryRequested;
    private Func<Task>? discoveryShutdown;
    public void BindCustomizationDiscoveryShutdown(Func<Task>? shutdown) => discoveryShutdown = shutdown;

    private async Task ExecuteCustomizationDiscovery(CustomizationDiscoveryCommand command, string text,
        ManualCallInput input, Func<bool>? queuedEligibility)
    {
        if (IsResponseInteractionPending)
        {
            Transcript = "Customization discovery cannot replace a pending question or approval. Nothing was shared.";
            return;
        }
        var captured = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        var privacy = CaptureCustomizationVoiceEligibility();
        bool Eligible() => captured() && privacy();
        if (!Eligible()
            || !isAssistantNameAvailable && commandRouter.IsActivationPrefixed(text, AssistantName)
            || input.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || input.Origin == RequestOrigin.ActivatedVoice && !commandRouter.IsActivationPrefixed(text, AssistantName))
        {
            ReportHostInteractionFailure("Customization discovery requires private original typed input or current-name activated voice. Nothing was shared.");
            return;
        }
        if (CustomizationDiscoveryRequested is not { } route)
        { ReportHostInteractionFailure("The reviewed customization metadata discovery surface is unavailable."); return; }
        await uiDispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(input.Origin,
            () => route(command, input.Origin, Eligible)));
    }
}
