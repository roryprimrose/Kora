using Kora.Application.Voice;
using Kora.Core.Communication;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private HandsFreeVoiceRecognitionService? handsFreeVoice;
    private sealed record WakeDispatch(VoiceTranscriptEventArgs Transcript, HandsFreeCommandActivation Activation);
    private readonly AsyncLocal<WakeDispatch?> wakeDispatch = new();

    private Task RunTranscriptOperationAsync(Kora.Core.Hosting.RequestOrigin origin, Func<Task> operation) =>
        wakeDispatch.Value is { } current && origin == Kora.Core.Hosting.RequestOrigin.ActivatedVoice
        && ReferenceEquals(Kora.Core.Diagnostics.HostActivity.Current?.Request, current.Activation.Request)
            ? operation() : Kora.Application.Hosting.HostRequestRunner.RunAsync(origin, operation);

    private bool HasCurrentNameActivation(string text) =>
        commandRouter.IsActivationPrefixed(text, AssistantName) || IsWakeCommand(text);

    private bool IsWakeCommand(string text) =>
        wakeDispatch.Value is { } current && string.Equals(text, current.Transcript.Transcript, StringComparison.Ordinal)
        && ReferenceEquals(Kora.Core.Diagnostics.HostActivity.Current?.Request, current.Activation.Request)
        && current.Activation.Request.Origin == Kora.Core.Hosting.RequestOrigin.ActivatedVoice
        && current.Transcript.Generation == voiceRecognition.Generation
        && current.Activation.Wake.Generation == current.Transcript.Generation
        && current.Activation.Wake.PlaybackExcluded && current.Activation.Wake.CommandStartSample >= 0
        && ObserveHandsFreeVoiceAdmission() == current.Activation.Admission;

    private bool CanRetireWakeForManualInput =>
        voiceRecognition is HandsFreeVoiceRecognitionService { State: HandsFreeVoiceState.WakeListening };

    private string? ActiveHandsFreeVoiceStatus => voiceRecognition is HandsFreeVoiceRecognitionService { IsAmbientListeningAvailable: true } handsFree
        ? handsFree.State switch
        {
            HandsFreeVoiceState.WakeListening => "Wake listening - local name detection only",
            HandsFreeVoiceState.CapturingCommand => "Capturing command - bounded local transcription",
            HandsFreeVoiceState.CompletingOperation => "Microphone closed - completing activated command",
            HandsFreeVoiceState.RecoveryRequired => "Hands-free unavailable - explicit listening recovery required",
            _ => null,
        }
        : null;

    private void BindHandsFreeVoiceRuntime()
    {
        if (handsFreeVoice is not null || voiceRecognition is not HandsFreeVoiceRecognitionService handsFree) { return; }
        handsFree.Bind(ObserveHandsFreeVoiceAdmission, async (transcript, request, activation) =>
        {
            if (transcript.Generation != voiceRecognition.Generation || ObserveHandsFreeVoiceAdmission() != activation.Admission
                || !ReferenceEquals(request, activation.Request) || activation.Wake.Generation != transcript.Generation
                || !activation.Wake.PlaybackExcluded || activation.Wake.CommandStartSample < 0)
            {
                throw new InvalidOperationException("The original hands-free command was retired before dispatch.");
            }
            Interlocked.Exchange(ref acceptedTranscriptGeneration, transcript.Generation);
            var previous = wakeDispatch.Value;
            wakeDispatch.Value = new(transcript, activation);
            try { await DispatchRecognizedVoiceTranscriptAsync(transcript, request); }
            finally { wakeDispatch.Value = previous; }
        }, () => IsReviewedVoiceCaptureReleased && !IsBusy && !IsSpeaking && !textToSpeech.IsSpeaking
            && promptInputVoice?.IsActive != true && composerDictation?.IsActive != true
            && questionVoiceReplies?.IsActive != true,
            _ => CaptureCustomizationVoice(Volatile.Read(ref customizationVoiceTarget)), GetRecognitionPhrases);
        handsFreeVoice = handsFree;
        handsFree.Changed += OnHandsFreeVoiceStateChanged;
    }

    private void OnHandsFreeVoiceStateChanged(object? sender, EventArgs args) => uiDispatcher.Post(() =>
    {
        if (disposed) { return; }
        NotifyVoiceEnablementChanged();
        if (voiceRecognition is HandsFreeVoiceRecognitionService { State: HandsFreeVoiceState.RecoveryRequired })
        {
            HoldVoiceInput("Microphone closed - hands-free capture or cleanup failed; use Enable listening");
            ShowInformation("Hands-free voice recovery required.",
                "Capture was retired. Review microphone, ownership, privacy and local profile readiness before explicitly enabling listening.");
        }
    });

    private void RetireHandsFreeVoiceBinding()
    {
        if (voiceRecognition is HandsFreeVoiceRecognitionService handsFree) { handsFree.Changed -= OnHandsFreeVoiceStateChanged; }
    }

    private HandsFreeVoiceAdmission? ObserveHandsFreeVoiceAdmission()
    {
        var privacy = privacyObservation.Current;
        var profile = speechInputConfiguration?.Get().Effective;
        return handsFreeVoice is not null && profile is not null && IsVoiceEnabled && IsHostInputEligible && voiceOwnershipEligible()
            && microphoneCatalogCurrent && catalogPrivacyRevision == privacy.TopologyRevision
            && IsVoiceReadinessEligible(privacy)
            && communicationPolicy.Current.EffectiveState is CallState.Unavailable or CallState.Clear or CallState.Active or CallState.Suspected
                ? new(EffectiveMicrophone!, AssistantName, profile, Interlocked.Read(ref voiceRecoveryRevision),
                    privacy.TopologyRevision, CallPolicyRevision, auditImpactOwnerRevision(),
                    Interlocked.Read(ref voiceOwnershipBindingRevision), assistantNameConfiguration.Get().Revision)
                : null;
    }

    private Task StartHandsFreeVoiceAsync(bool explicitRecovery = false) =>
        handsFreeVoice?.StartHandsFreeAsync(explicitRecovery) ?? Task.CompletedTask;
}
