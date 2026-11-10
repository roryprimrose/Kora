using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<SessionMetadataCommand, RequestOrigin, Func<bool>, Task>? SessionMetadataRequested;

    private async Task ExecuteSessionMetadata(SessionMetadataCommand command, string text,
        ManualCallInput input, Func<bool>? queuedEligibility)
    {
        if (command.Error is { } error) { ReportHostInteractionFailure(error); return; }
        var captured = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        var privacy = CaptureCustomizationVoiceEligibility();
        bool Eligible() => captured() && privacy() && !IsResponseInteractionPending;
        if (!Eligible() || input.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || input.Origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(text))
        { ReportHostInteractionFailure("Exact metadata review requires original private typed input or current activated voice."); return; }
        if (SessionMetadataRequested is not { } route)
        { ReportHostInteractionFailure("The reviewed metadata answer surface is unavailable."); return; }
        await uiDispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(input.Origin,
            () => route(command, input.Origin, Eligible)));
    }
}
