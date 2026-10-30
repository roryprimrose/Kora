using System.ComponentModel;
using Kora.Application.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ReviewedVoiceCommandCapture? reviewedVoiceCommand;
    public bool IsReviewedVoiceCommandActive => reviewedVoiceCommand?.IsActive == true;
    private bool IsReviewedVoiceCaptureReleased => reviewedVoiceCommand?.IsCaptureReleased != false;
    public bool HasReviewedVoiceCommand => reviewedVoiceCommand?.HasReview == true;
    public string ReviewedVoiceCommandText => reviewedVoiceCommand?.Candidate ?? string.Empty;
    public string ReviewedVoiceCommandStatus => reviewedVoiceCommand?.Status
        ?? "Review one spoken command before dispatch. Direct push-to-talk is unchanged.";

    public void BindReviewedVoiceCommand(ILogger<ReviewedVoiceCommandCapture> captureLogger, TimeProvider time)
    {
        if (reviewedVoiceCommand is not null) { throw new InvalidOperationException("Reviewed command capture already has an owner."); }
        reviewedVoiceCommand = new(voiceRecognition, uiDispatcher, CaptureReviewedVoiceCommandTarget, captureLogger, time);
        reviewedVoiceCommand.Changed += OnReviewedVoiceCommandChanged;
        PropertyChanged += RevalidateReviewedVoiceCommand;
    }

    private bool ReviewedVoiceCommandHostEligible => !disposed && IsVoiceEnabled && IsHostInputEligible
        && voiceOwnershipEligible() && callOwnershipEligible() && IsVoiceReadinessEligible(privacyObservation.Current)
        && microphoneCatalogCurrent && catalogPrivacyRevision == privacyObservation.Current.TopologyRevision
        && Volatile.Read(ref listeningStopRequests) == 0 && !listeningStop.IsStopping
        && Volatile.Read(ref pushToTalkHeld) == 0 && !IsBusy && !IsSpeaking && !textToSpeech.IsSpeaking
        && (!IsProtectedCall || ShowVisualTextDuringCalls)
        && CanEnterRetainedAnswerRead && !HasResponseActions && !HasForegroundRequestReview
        && promptInputVoice?.IsActive != true && customizationClarification is null
        && Volatile.Read(ref customizationVoiceTarget) is null && activeCustomizationVoice is null;

    private ReviewedVoiceCommandTarget? CaptureReviewedVoiceCommandTarget()
    {
        if (!ReviewedVoiceCommandHostEligible || EffectiveMicrophone is not { } microphone) { return null; }
        var privacy = privacyObservation.Current;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var call = CallPolicyRevision;
        var name = assistantNameConfiguration.Get().Revision;
        var voiceBinding = Interlocked.Read(ref voiceOwnershipBindingRevision);
        var controlBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var owner = auditImpactOwnerRevision();
        var input = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var textRevision = Interlocked.Read(ref attachmentCatalogueComposerRevision);
        var foreground = Volatile.Read(ref foregroundConversationTarget);
        var source = Volatile.Read(ref ordinaryResponse);
        var presentation = Interlocked.Read(ref currentResponseBindingRevision);
        var profile = speechInputConfiguration?.Get().Effective;
        bool PrivacyCurrent()
        {
            var current = privacyObservation.Current;
            return current.SessionState == privacy.SessionState && current.MicrophoneAccess == privacy.MicrophoneAccess
                && current.TopologyRevision == privacy.TopologyRevision
                && string.Equals(current.DefaultMicrophoneId, privacy.DefaultMicrophoneId, StringComparison.Ordinal)
                && string.Equals(current.DefaultSpeakerId, privacy.DefaultSpeakerId, StringComparison.Ordinal)
                && current.ActiveMicrophoneIds.SequenceEqual(privacy.ActiveMicrophoneIds, StringComparer.Ordinal);
        }
        bool Current() => ReviewedVoiceCommandHostEligible && PrivacyCurrent()
            && recovery == Interlocked.Read(ref voiceRecoveryRevision) && CallPolicyRevision == call
            && assistantNameConfiguration.Get().Revision == name && EffectiveMicrophone == microphone
            && voiceBinding == Interlocked.Read(ref voiceOwnershipBindingRevision)
            && controlBinding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && auditImpactOwnerRevision() == owner && input == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && textRevision == Interlocked.Read(ref attachmentCatalogueComposerRevision)
            && ReferenceEquals(foreground, Volatile.Read(ref foregroundConversationTarget))
            && (foreground is null || foreground.IsCurrent)
            && ReferenceEquals(source, Volatile.Read(ref ordinaryResponse))
            && presentation == Interlocked.Read(ref currentResponseBindingRevision)
            && profile == speechInputConfiguration?.Get().Effective;
        return new(microphone, AssistantName, Current, async (transcript, request, exact) =>
        {
            if (!exact())
            { throw new InvalidOperationException("The original reviewed voice target expired."); }
            Interlocked.Exchange(ref acceptedTranscriptGeneration, transcript.Generation);
            await DispatchRecognizedVoiceTranscriptAsync(transcript, request, exact);
        });
    }

    public Task BeginReviewedVoiceCommandAsync(Func<bool> nativeLifetime) =>
        (reviewedVoiceCommand ?? throw new InvalidOperationException("Reviewed voice capture is not composed.")).BeginAsync(nativeLifetime);
    public Task FinishReviewedVoiceCommandAsync() =>
        (reviewedVoiceCommand ?? throw new InvalidOperationException("Reviewed voice capture is not composed.")).FinishAsync();

    public Func<string, bool> CaptureReviewedVoiceCommandEdit()
    {
        var revision = reviewedVoiceCommand?.Revision ?? 0;
        return text => (reviewedVoiceCommand ?? throw new InvalidOperationException("Reviewed voice capture is not composed."))
            .Edit(revision, text);
    }

    public Func<Task> CaptureReviewedVoiceCommandConfirmation()
    {
        var revision = reviewedVoiceCommand?.Revision ?? 0;
        return () => (reviewedVoiceCommand ?? throw new InvalidOperationException("Reviewed voice capture is not composed."))
            .ConfirmAsync(revision);
    }

    public async Task CancelReviewedVoiceCommandAsync()
    {
        RetireReviewedVoiceCommand();
#pragma warning disable VSTHRD003 // Cancellation joins the exact owned capture, not merely its cancellation token.
        if (reviewedVoiceCommand is not null) { await reviewedVoiceCommand.Quiescence; }
#pragma warning restore VSTHRD003
    }

    private void RetireReviewedVoiceCommand() =>
        reviewedVoiceCommand?.Retire("Reviewed command cancelled or retired. Volatile candidate cleared; no replay queued.");

    private void RevalidateReviewedVoiceCommand(object? sender, PropertyChangedEventArgs args) =>
        reviewedVoiceCommand!.Revalidate();

    private void OnReviewedVoiceCommandChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsReviewedVoiceCommandActive));
        OnPropertyChanged(nameof(HasReviewedVoiceCommand));
        OnPropertyChanged(nameof(ReviewedVoiceCommandText));
        OnPropertyChanged(nameof(ReviewedVoiceCommandStatus));
        NotifyVoiceEnablementChanged();
    }
}
