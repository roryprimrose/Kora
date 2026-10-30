using System.ComponentModel;

using Kora.Application.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ComposerDictationService? composerDictation;
    public ComposerDictationService? ComposerDictation => composerDictation;

    public ComposerDictationService BindComposerDictation(ILogger<ComposerDictationService> dictationLogger)
    {
        if (composerDictation is null)
        {
            composerDictation = new(voiceRecognition, uiDispatcher, ObserveComposerDictation, dictationLogger);
            PropertyChanged += ComposerVoiceReadinessChanged;
        }
        return composerDictation;
    }

    private ComposerDictationAdmission? ObserveComposerDictation()
    {
        var privacy = privacyObservation.Current;
        return Volatile.Read(ref listeningStopRequests) == 0 && !listeningStop.IsStopping
            && IsHostInputEligible && voiceOwnershipEligible() && IsVoiceReadinessEligible(privacy)
            && microphoneCatalogCurrent && catalogPrivacyRevision == privacy.TopologyRevision
            && Volatile.Read(ref pushToTalkHeld) == 0 && !IsBusy && !IsResponseInteractionPending
            && questionVoiceReplies?.IsActive != true && promptInputVoice?.IsActive != true && !textToSpeech.IsSpeaking && !IsSpeaking
                ? new(EffectiveMicrophone!, Interlocked.Read(ref voiceRecoveryRevision), privacy.TopologyRevision,
                    CallPolicyRevision, auditImpactOwnerRevision(), Interlocked.Read(ref workspaceSurfaceInputRevision))
                : null;
    }

    private void ComposerVoiceReadinessChanged(object? sender, PropertyChangedEventArgs args) => composerDictation!.Revalidate();

    public Func<bool> CaptureInstructionBodyDictationEligibility()
    {
        var admission = ObserveComposerDictation();
        var name = assistantNameConfiguration.Get().Revision;
        var voiceBinding = Interlocked.Read(ref voiceOwnershipBindingRevision);
        var controlBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        return () => admission is not null && ObserveComposerDictation() == admission
            && assistantNameConfiguration.Get().Revision == name
            && voiceBinding == Interlocked.Read(ref voiceOwnershipBindingRevision)
            && controlBinding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && !IsProtectedCall && CanRevealPrivatePresentation;
    }

    private void RetireComposerDictation() =>
        composerDictation?.Retire("Dictation admission revoked. Original buffer preserved; late results discarded.");
}
