using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private VoiceCaptureTimingConfigurationService? captureTiming;
    private sealed record TimingReview(VoiceCaptureTiming Proposed, long Revision, long CallRevision,
        RequestOrigin Origin, SecurityAuditInitiator Initiator, Func<bool> Eligible);
    private TimingReview? timingReview;
    private bool timingWriting;
    private string selectedVoiceTimingOption = VoiceCaptureTiming.CommandStartId;
    private string voiceTimingSeconds = "5";
    public IReadOnlyList<string> VoiceTimingOptions => VoiceCaptureTimingCommand.OptionIds;
    public string SelectedVoiceTimingOption
    {
        get => selectedVoiceTimingOption;
        set { if (SetProperty(ref selectedVoiceTimingOption, value)) { timingReview = null; NotifyVoiceTiming(); } }
    }
    public string VoiceTimingSeconds
    {
        get => voiceTimingSeconds;
        set { if (SetProperty(ref voiceTimingSeconds, value)) { timingReview = null; NotifyVoiceTiming(); } }
    }
    public string VoiceTimingStatus
    {
        get
        {
            var snapshot = captureTiming?.Get();
            if (snapshot?.Timing is not { } timing)
            { return "Capture timing unavailable: saved state is invalid, unreadable or unconfirmed. New captures are refused; inspect preferences and audit before recovery."; }
            return $"Defaults: {VoiceCaptureTiming.Default.Describe()}. {(snapshot.Value.IsSaved ? "Saved" : "Default (not saved)")}: {timing.Describe()}. "
                + (speechInputConfiguration?.Get().Effective is not null
                    ? $"Effective for the next eligible supported capture: {timing.Describe()}. "
                    : "Effective capture unavailable: no confirmed supported speech-input profile. Saved intent is not readiness. ")
                + "An in-flight capture keeps its snapshot. Consent, permission, call policy and ownership remain required; saving never opens a microphone.";
        }
    }
    public string VoiceTimingReviewStatus => timingReview is { } review
        ? $"Proposed for next eligible capture: {review.Proposed.Describe()}. Confirm or cancel; the current capture is unchanged."
        : "No capture timing change awaiting confirmation.";
    public bool CanReviewVoiceTiming => captureTiming?.Get().Timing is not null && !timingWriting
        && speechInputNativeLifetime() && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public bool CanConfirmVoiceTiming => timingReview is { } review && !timingWriting && review.Eligible();
    public bool HasVoiceTimingReview => timingReview is not null;
    public AsyncCommand ReviewVoiceTimingCommand { get; private set; } = null!;
    public AsyncCommand ConfirmVoiceTimingCommand { get; private set; } = null!;
    public AsyncCommand CancelVoiceTimingCommand { get; private set; } = null!;

    private void InitializeVoiceCaptureTiming(VoiceCaptureTimingConfigurationService? service, Func<Func<Task>, AsyncCommand> createCommand)
    {
        captureTiming = service;
        ReviewVoiceTimingCommand = createCommand(() => ExecuteVoiceCaptureTimingAsync(
            new(AppearanceCommandOperation.Set, SelectedVoiceTimingOption, VoiceTimingSeconds), SecurityAuditInitiator.LocalUser));
        ConfirmVoiceTimingCommand = createCommand(() => ExecuteVoiceCaptureTimingAsync(
            new(AppearanceCommandOperation.Set, ReviewDecision: true), SecurityAuditInitiator.LocalUser));
        CancelVoiceTimingCommand = createCommand(() => ExecuteVoiceCaptureTimingAsync(
            new(AppearanceCommandOperation.Set, ReviewDecision: false), SecurityAuditInitiator.LocalUser));
    }

    internal async Task ExecuteVoiceCaptureTimingAsync(VoiceCaptureTimingCommand command, SecurityAuditInitiator initiator,
        CancellationToken cancellationToken = default)
    {
        if (command.ReviewDecision == false) { timingReview = null; NotifyVoiceTiming(); return; }
        if (disposed || timingWriting || !IsCallMutationHostEligible || IsResponseInteractionPending
            || initiator == SecurityAuditInitiator.LocalUser && !speechInputNativeLifetime())
        { Transcript = "Capture timing requires the current unlocked owning input with no pending question or approval."; return; }
        var service = captureTiming;
        var origin = OriginalOrigin(initiator);
        if (service?.Get() is not { Timing: { } current } snapshot)
        { ShowFailure("Capture timing unavailable.", VoiceTimingStatus); return; }
        try
        {
            if (command.Operation == AppearanceCommandOperation.Clarify)
            { ShowInformation("Clarify capture timing.", VoiceCaptureTimingCommand.Syntax); return; }
            if (command.ReviewDecision == true)
            {
                var review = timingReview;
                timingReview = null;
                if (review is null || !review.Eligible() || origin != review.Origin)
                { ShowFailure("Capture timing review expired.", "Use the original channel and review again; no preference changed."); return; }
                timingWriting = true;
                NotifyVoiceTiming();
                var accepted = await service.SetAsync(review.Proposed, review.Revision, review.CallRevision,
                    review.Origin, review.Initiator, communicationPolicy, review.Eligible, cancellationToken);
                if (!accepted) { ShowFailure("Capture timing denied.", "Current call/channel policy refused the change; no timing preference changed."); return; }
                ShowInformation("Capture timing saved.", VoiceTimingStatus);
            }
            else if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                timingReview = null;
                var proposed = current.With(command.Option ?? throw new ArgumentException("Choose one exact capture timing option.", nameof(command)),
                    command.Operation == AppearanceCommandOperation.Reset ? null : command.Value ?? throw new ArgumentException("Specify seconds.", nameof(command)));
                var admitted = CaptureAudioControlEligibility(origin);
                var native = speechInputNativeLifetime;
                var nameRevision = assistantNameConfiguration.Get().Revision;
                var consent = HasVoiceConsent;
                var recovery = Interlocked.Read(ref voiceRecoveryRevision);
                var owner = auditImpactOwnerRevision();
                var ownerBinding = Interlocked.Read(ref voiceOwnershipBindingRevision);
                var microphone = SelectedMicrophone;
                var topology = MicrophoneTopologyRevision;
                bool Eligible() => !disposed && admitted() && assistantNameConfiguration.Get().Revision == nameRevision
                    && HasVoiceConsent == consent && Interlocked.Read(ref voiceRecoveryRevision) == recovery
                    && auditImpactOwnerRevision() == owner && Interlocked.Read(ref voiceOwnershipBindingRevision) == ownerBinding
                    && SelectedMicrophone == microphone && MicrophoneTopologyRevision == topology
                    && (initiator != SecurityAuditInitiator.LocalUser || ReferenceEquals(native, speechInputNativeLifetime) && native());
                if (communicationPolicy.CheckMutation(origin, CallPolicyRevision, Eligible) is not null)
                { ShowFailure("Capture timing denied.", "Current call/channel policy refused the proposal; no preference changed."); return; }
                timingReview = new(proposed, snapshot.Revision, CallPolicyRevision, origin, initiator, Eligible);
                ShowInformation("Review capture timing.", VoiceTimingStatus + Environment.NewLine + VoiceTimingReviewStatus
                    + Environment.NewLine + VoiceCaptureTimingCommand.Syntax);
            }
            else { ShowInformation("Capture timing.", VoiceTimingStatus + Environment.NewLine + VoiceCaptureTimingCommand.Syntax); }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Changing capture timing");
            ShowFailure("Capture timing not confirmed.", exception.Message + Environment.NewLine + VoiceTimingStatus);
        }
        finally { timingWriting = false; NotifyVoiceTiming(); }
    }

    private void NotifyVoiceTiming()
    {
        OnPropertyChanged(nameof(VoiceTimingStatus));
        OnPropertyChanged(nameof(VoiceTimingReviewStatus));
        OnPropertyChanged(nameof(CanReviewVoiceTiming));
        OnPropertyChanged(nameof(CanConfirmVoiceTiming));
        OnPropertyChanged(nameof(HasVoiceTimingReview));
    }
}
