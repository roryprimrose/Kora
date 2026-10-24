using Kora.Application.Voice;
using Kora.Core.Communication;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private HandsFreeVoiceRecognitionService? handsFreeVoice;

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
        handsFree.Bind(ObserveHandsFreeVoiceAdmission, async (transcript, request) =>
        {
            if (transcript.Generation != voiceRecognition.Generation || ObserveHandsFreeVoiceAdmission() is null)
            {
                throw new InvalidOperationException("The original hands-free command was retired before dispatch.");
            }
            Interlocked.Exchange(ref acceptedTranscriptGeneration, transcript.Generation);
            await DispatchRecognizedVoiceTranscriptAsync(transcript, request);
        }, () => !IsBusy && !IsSpeaking && !textToSpeech.IsSpeaking
            && promptInputVoice?.IsActive != true && composerDictation?.IsActive != true
            && questionVoiceReplies?.IsActive != true);
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
                    Interlocked.Read(ref voiceOwnershipBindingRevision))
                : null;
    }

    private Task StartHandsFreeVoiceAsync(bool explicitRecovery = false) =>
        handsFreeVoice?.StartHandsFreeAsync(explicitRecovery) ?? Task.CompletedTask;
}
