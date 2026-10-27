using Kora.Core.Hosting;
using Kora.Core.Diagnostics;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public Func<bool> CaptureSettingInspectionAdmission()
    {
        var input = HostActivity.RequireCurrent();
        var origin = input.Request.Origin;
        var call = CallPolicyRevision;
        var originalInput = CaptureManualCallInput(origin, call);
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var original = input.Request.InvocationId is null && input.Outcome == HostOperationOutcome.Unknown
            && !input.Activity!.IsStopped && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice;
        return () => original && originalInput.Eligible() && CanRevealPrivatePresentation
            && !IsResponseInteractionPending && CallPolicyRevision == call && isAssistantNameAvailable
            && settingsOwnershipRevision() == ownership && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && Interlocked.Read(ref workspaceSurfaceInputRevision) == originalInput.InputRevision
            && assistantNameConfiguration.Get().Revision == originalInput.AssistantNameRevision
            && (origin != RequestOrigin.ActivatedVoice || CallObservation.AllowActivation);
    }
}
