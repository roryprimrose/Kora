using Kora.Application.Definitions;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<CustomizationCommand, Task>? CustomizationRequested;
    private Func<Task<bool>>? customizationShutdown;
    public void BindCustomizationShutdown(Func<Task<bool>>? shutdown) => customizationShutdown = shutdown;
    private CustomizationVoiceTarget? customizationVoiceTarget;
    private CustomizationVoiceTarget? capturedCustomizationVoiceTarget;
    private Func<CustomizationVoiceTarget, string, Func<bool>, Task>? customizationVoice;

    public void BindCustomizationVoice(Func<CustomizationVoiceTarget, string, Func<bool>, Task>? handler) => customizationVoice = handler;

    public void PublishCustomizationVoiceTarget(CustomizationVoiceTarget? target) =>
        Interlocked.Exchange(ref customizationVoiceTarget, target)?.Retire();

    public Func<bool> CaptureCustomizationVoiceEligibility()
    {
        var privacy = privacyObservation.Current.TopologyRevision;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var call = CallPolicyRevision;
        var ownership = Interlocked.Read(ref voiceOwnershipBindingRevision);
        return () => CanRevealPrivatePresentation && !IsProtectedCall
            && privacyObservation.Current.TopologyRevision == privacy
            && Interlocked.Read(ref voiceRecoveryRevision) == recovery && CallPolicyRevision == call
            && voiceOwnershipEligible() && Interlocked.Read(ref voiceOwnershipBindingRevision) == ownership;
    }

    private void CaptureCustomizationVoice(CustomizationVoiceTarget? target) =>
        Volatile.Write(ref capturedCustomizationVoiceTarget, target);

    private async Task ExecuteCustomizationVoice(CustomizationVoiceTarget? target, string text)
    {
        if (target is null || !target.IsCurrent || customizationVoice is not { } handler)
        {
            ReportHostInteractionFailure("Editor voice target is unavailable or changed. No session received this input. Focus the artifact and activate again, or use native text.");
            return;
        }
        var generation = voiceRecognition.Generation;
        var call = CallPolicyRevision;
        bool Current() => target.IsCurrent && IsHostInputEligible
            && generation == voiceRecognition.Generation && CallPolicyRevision == call && voiceOwnershipEligible();
        IsBusy = true;
        try { await handler(target, text, Current); }
        finally { IsBusy = false; }
    }

    private Task ExecuteCustomizationCommand(CustomizationCommand command)
    {
        if (CustomizationRequested is { } handler) { return handler(command); }
        ReportHostInteractionFailure("The Customizations window is unavailable. No source was edited.");
        return Task.CompletedTask;
    }
}
