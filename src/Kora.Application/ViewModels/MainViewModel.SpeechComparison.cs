using System.ComponentModel;

using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SpeechSetupComparison? speechComparison;
    public string SpeechComparisonPhrase
    {
        get => speechComparison?.ExpectedPhrase ?? "";
        set { if (speechComparison is not null) { speechComparison.ExpectedPhrase = value; } }
    }
    public string SpeechComparisonStatus => speechComparison?.Status ?? "Local setup comparison is unavailable.";
    public string SpeechComparisonProfiles => speechComparison?.ReviewDescription ?? "";
    public string SpeechComparisonDisclosure => SpeechSetupComparison.Disclosure;
    public string SpeechComparisonResults => string.Join(Environment.NewLine,
        speechComparison?.Results.Select(result => result.Description) ?? []);
    public bool CanTestSpeechComparison => speechComparison?.CanTest == true;
    public bool IsSpeechComparisonRunning => speechComparison?.IsRunning == true;
    public AsyncCommand ReviewSpeechComparisonCommand { get; private set; } = null!;
    public AsyncCommand TestSpeechComparisonCommand { get; private set; } = null!;
    public AsyncCommand CancelSpeechComparisonCommand { get; private set; } = null!;

    private void InitializeSpeechComparison(SpeechSetupComparison? comparison, Func<Func<Task>, AsyncCommand> createCommand)
    {
        speechComparison = comparison;
        if (comparison is not null)
        {
            comparison.Changed += OnSpeechComparisonChanged;
            PropertyChanged += RevalidateSpeechComparison;
        }
        ReviewSpeechComparisonCommand = createCommand(() => ExecuteSpeechComparison(
            new("compare speech input"), SecurityAuditInitiator.LocalUser));
        TestSpeechComparisonCommand = createCommand(() => ExecuteSpeechComparison(
            new("test speech comparison"), SecurityAuditInitiator.LocalUser));
        CancelSpeechComparisonCommand = createCommand(() => ExecuteSpeechComparison(
            new("cancel speech comparison"), SecurityAuditInitiator.LocalUser));
    }

    private void OnSpeechComparisonChanged(object? sender, EventArgs args) => uiDispatcher.Post(() =>
    {
        OnPropertyChanged(nameof(SpeechComparisonPhrase));
        OnPropertyChanged(nameof(SpeechComparisonStatus));
        OnPropertyChanged(nameof(SpeechComparisonProfiles));
        OnPropertyChanged(nameof(SpeechComparisonResults));
        OnPropertyChanged(nameof(CanTestSpeechComparison));
        OnPropertyChanged(nameof(IsSpeechComparisonRunning));
    });

    private void RevalidateSpeechComparison(object? sender, PropertyChangedEventArgs args) => speechComparison!.Revalidate();

    private async Task ExecuteSpeechComparison(SpeechComparisonCommand command, SecurityAuditInitiator initiator)
    {
        var comparison = speechComparison ?? throw new InvalidOperationException("Comparison runtime is unavailable.");
        if (string.Equals(command.Operation, "cancel speech comparison", StringComparison.Ordinal))
        {
            await comparison.CancelAsync();
            return;
        }
        var origin = OriginalOrigin(initiator);
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !isAssistantNameAvailable)
        { throw new InvalidOperationException("Comparison requires original admitted local user input."); }
        if (string.Equals(command.Operation, "compare speech input", StringComparison.Ordinal))
        {
            if (initiator != SecurityAuditInitiator.LocalUser)
            {
                var input = CaptureManualCallInput(origin, CallPolicyRevision);
                var navigation = settingsSectionNavigation
                    ?? throw new InvalidOperationException("The owning native Settings presenter is unavailable.");
                navigation.Navigate(SettingsSection.SpeechAudio, input.Eligible);
            }
            var native = speechInputNativeLifetime;
            var host = CaptureAudioControlEligibility(origin);
            var microphone = EffectiveMicrophone ?? throw new InvalidOperationException("Choose an available microphone first.");
            var privacy = privacyObservation.Current;
            var owner = auditImpactOwnerRevision();
            var ownerBinding = Interlocked.Read(ref voiceOwnershipBindingRevision);
            var sourceRevision = Interlocked.Read(ref workspaceSurfaceInputRevision);
            var nameRevision = assistantNameConfiguration.Get().Revision;
            var recovery = Interlocked.Read(ref voiceRecoveryRevision);
            var selected = SelectedSpeechInputProfile;
            var resource = SelectedSpeechInputResource;
            var residency = SelectedSpeechInputResidency;
            bool Eligible() => !disposed && host() && native() && ReferenceEquals(native, speechInputNativeLifetime)
                && voiceOwnershipEligible() && HasVoiceConsent && !IsProtectedCall
                && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision
                && privacyObservation.Current.CanCaptureFrom(microphone)
                && microphoneCatalogCurrent && EffectiveMicrophone == microphone
                && MicrophoneAccessStatus.State == Kora.Core.Voice.MicrophoneAccessState.Allowed
                && auditImpactOwnerRevision() == owner && Interlocked.Read(ref voiceOwnershipBindingRevision) == ownerBinding
                && Interlocked.Read(ref workspaceSurfaceInputRevision) == sourceRevision
                && assistantNameConfiguration.Get().Revision == nameRevision && Interlocked.Read(ref voiceRecoveryRevision) == recovery
                && SelectedSpeechInputProfile == selected && SelectedSpeechInputResource == resource && SelectedSpeechInputResidency == residency
                && !textToSpeech.IsSpeaking && !IsSpeaking && (!IsBusy || Volatile.Read(ref pushToTalkHeld) != 0) && !IsResponseInteractionPending
                && questionVoiceReplies?.IsActive != true && promptInputVoice?.IsActive != true;
            comparison.Review(microphone, Eligible);
        }
        else
        {
            await comparison.TestAsync(microphoneUseConfirmed: true);
        }
    }
}
