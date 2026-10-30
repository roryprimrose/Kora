using Kora.Application.Definitions;
using Kora.Core.Diagnostics;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<CustomizationCommand, Task>? CustomizationRequested;
    private Func<Task<bool>>? customizationShutdown;
    public void BindCustomizationShutdown(Func<Task<bool>>? shutdown) => customizationShutdown = shutdown;
    private CustomizationVoiceTarget? customizationVoiceTarget;
    private CustomizationVoiceTarget? capturedCustomizationVoiceTarget;
    private Func<CustomizationVoiceRequest, Task>? customizationVoice;
    private CustomizationVoiceRequest? activeCustomizationVoice;
    private CustomizationVoiceClarification? customizationClarification;
    private Task customizationClarificationRetirement = Task.CompletedTask;

    public void BindCustomizationVoice(Func<CustomizationVoiceRequest, Task>? handler) => customizationVoice = handler;

    public CustomizationVoiceClarification CreateCustomizationClarification(CustomizationVoiceRequest request,
        Func<bool> exact, ILogger questionLogger, CancellationToken token)
    {
        if (customizationClarification is not null)
        { throw new InvalidOperationException("An editor clarification already owns this operation."); }
        var question = new CustomizationVoiceClarification(request, exact,
            voiceRecognition as IQuestionVoiceRecognitionService,
            () => ReferenceEquals(activeCustomizationVoice, request) && !questionSpeech
                && questionVoiceReplies?.IsActive != true && promptInputVoice?.IsActive != true
                && Volatile.Read(ref pushToTalkHeld) == 0 && Volatile.Read(ref listeningStopRequests) == 0
                && !listeningStop.IsStopping ? ObserveAdmittedVoiceCaptureReadiness() : null,
            TimeProvider.System, questionLogger, token);
        customizationClarification = question;
        customizationClarificationRetirement = question.Completion;
        PropertyChanged += RevalidateCustomizationClarification;
        return question;
    }

    public void ReleaseCustomizationClarification(CustomizationVoiceClarification question)
    {
        if (!ReferenceEquals(customizationClarification, question)) { return; }
        PropertyChanged -= RevalidateCustomizationClarification;
        customizationClarification = null;
    }

    private void RevalidateCustomizationClarification(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        customizationClarification?.Revalidate();

    private async Task JoinCustomizationClarification()
    {
#pragma warning disable VSTHRD003 // Join the exact editor question's capture, subscriptions, expiry and cancellation resources.
        await customizationClarificationRetirement;
#pragma warning restore VSTHRD003
    }

    public void PublishCustomizationVoiceTarget(CustomizationVoiceTarget? target)
    {
        Interlocked.Exchange(ref customizationVoiceTarget, target)?.Retire();
        reviewedVoiceCommand?.Revalidate();
    }

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
        var call = CallPolicyRevision;
        var original = CaptureCustomizationVoiceEligibility();
        var request = new CustomizationVoiceRequest(target, text, HostActivity.RequireCurrent().Request,
            () => original() && IsHostInputEligible && CallPolicyRevision == call && voiceOwnershipEligible(),
            () => voiceRecognition.Generation);
        activeCustomizationVoice = request;
        IsBusy = true;
        try { await handler(request); }
        finally { request.Retire(); activeCustomizationVoice = null; IsBusy = false; }
    }

    private Task ExecuteCustomizationCommand(CustomizationCommand command)
    {
        if (CustomizationRequested is { } handler) { return handler(command); }
        ReportHostInteractionFailure("The Customizations window is unavailable. No source was edited.");
        return Task.CompletedTask;
    }
}
