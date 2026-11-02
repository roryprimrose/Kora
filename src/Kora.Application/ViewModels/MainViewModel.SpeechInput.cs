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
    private SpeechInputConfigurationService? speechInputConfiguration;
    private Func<bool> speechInputNativeLifetime = static () => false;
    private bool speechInputActive;
    private SpeechInputProfile? selectedSpeechInputProfile = SpeechInputProfileCatalogue.Profiles[0];
    private SpeechInputResource selectedSpeechInputResource;
    private SpeechInputResidency selectedSpeechInputResidency;
    private sealed record PendingSpeechInputReview(SpeechInputProfileReview Review, TaskCompletionSource<bool> Completion, Func<bool> Eligible);
    private PendingSpeechInputReview? pendingSpeechInputReview;
    private Task speechInputOperation = Task.CompletedTask;

    public IReadOnlyList<SpeechInputProfile> SpeechInputProfiles => speechInputConfiguration?.GetProfiles() ?? SpeechInputProfileCatalogue.Profiles;
    public IReadOnlyList<SpeechInputResource> SpeechInputResources =>
        SpeechInputProfiles.Any(profile => profile.Id is SpeechInputProfileCatalogue.NativeWakeProfileId)
            ? [SpeechInputResource.WindowsManaged, SpeechInputResource.Cpu] : [SpeechInputResource.WindowsManaged];
    public IReadOnlyList<SpeechInputResidency> SpeechInputResidencies { get; } = [SpeechInputResidency.CaptureLifetime];
    public SpeechInputProfile? SelectedSpeechInputProfile
    {
        get => selectedSpeechInputProfile;
        set { if (SetProperty(ref selectedSpeechInputProfile, value)) { Volatile.Read(ref pendingSpeechInputReview)?.Completion.TrySetResult(false); } }
    }
    public SpeechInputResource SelectedSpeechInputResource
    {
        get => selectedSpeechInputResource;
        set { if (SetProperty(ref selectedSpeechInputResource, value)) { Volatile.Read(ref pendingSpeechInputReview)?.Completion.TrySetResult(false); } }
    }
    public SpeechInputResidency SelectedSpeechInputResidency
    {
        get => selectedSpeechInputResidency;
        set { if (SetProperty(ref selectedSpeechInputResidency, value)) { Volatile.Read(ref pendingSpeechInputReview)?.Completion.TrySetResult(false); } }
    }
    public string SpeechInputStatus => speechInputConfiguration?.Get().Describe()
        ?? "Speech-input profile selection is unavailable. Existing activated capture is unchanged.";
    public bool CanChangeSpeechInputNative => speechInputConfiguration is not null && !speechInputActive
        && speechInputNativeLifetime() && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public bool HasSpeechInputReview => Volatile.Read(ref pendingSpeechInputReview) is not null;
    public bool CanConfirmSpeechInputReview => Volatile.Read(ref pendingSpeechInputReview) is { } pending && pending.Eligible();
    public string SpeechInputReviewStatus => Volatile.Read(ref pendingSpeechInputReview)?.Review.Describe() ?? "No speech-input switch awaiting confirmation.";
    public AsyncCommand SaveSpeechInputCommand { get; private set; } = null!;
    public AsyncCommand InspectSpeechInputCommand { get; private set; } = null!;
    public AsyncCommand ConfirmSpeechInputCommand { get; private set; } = null!;
    public AsyncCommand CancelSpeechInputCommand { get; private set; } = null!;

    public void BindSpeechInputNativeLifetime(Func<bool> lifetime)
    {
        speechComparison?.Clear();
        Volatile.Read(ref pendingSpeechInputReview)?.Completion.TrySetResult(false);
        speechInputNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanChangeSpeechInputNative));
    }

    private void InitializeSpeechInputConfiguration(SpeechInputConfigurationService? service, Func<Func<Task>, AsyncCommand> createCommand)
    {
        speechInputConfiguration = service;
        if (service?.Get().Desired is { } desired)
        {
            selectedSpeechInputResource = desired.Resource;
            selectedSpeechInputResidency = desired.Residency;
        }
        service?.BindAssistantName(assistantNameConfiguration);
        SaveSpeechInputCommand = createCommand(() => ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Set,
            new(SelectedSpeechInputProfile?.Id ?? throw new InvalidOperationException("Choose one exact known speech-input profile."),
                SelectedSpeechInputResource, SelectedSpeechInputResidency)), SecurityAuditInitiator.LocalUser));
        InspectSpeechInputCommand = createCommand(() => ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Get),
            SecurityAuditInitiator.LocalUser));
        ConfirmSpeechInputCommand = createCommand(() => ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Set, ReviewDecision: true),
            SecurityAuditInitiator.LocalUser));
        CancelSpeechInputCommand = createCommand(() => ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Set, ReviewDecision: false),
            SecurityAuditInitiator.LocalUser));
    }

    internal async Task ExecuteSpeechInputCommandAsync(SpeechInputCommand command, SecurityAuditInitiator initiator)
    {
        if (command.ReviewDecision is { } confirm)
        {
            var accepted = CompleteSpeechInputReview(confirm, initiator);
            if (accepted || !confirm) { await speechInputOperation; }
            return;
        }
        if (command.Operation != AppearanceCommandOperation.Set || speechInputActive)
        {
            await RunSpeechInputCommandAsync(command, initiator, null);
            return;
        }
        var reviewReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = RunSpeechInputCommandAsync(command, initiator, reviewReady);
        speechInputOperation = operation;
        await Task.WhenAny(reviewReady.Task, operation);
        if (operation.IsCompleted) { await operation; }
    }

    private async Task RunSpeechInputCommandAsync(SpeechInputCommand command, SecurityAuditInitiator initiator, TaskCompletionSource? reviewReady)
    {
        var service = speechInputConfiguration;
        var native = speechInputNativeLifetime;
        if (service is null || speechInputActive || !IsCallMutationHostEligible || IsResponseInteractionPending
            || initiator == SecurityAuditInitiator.LocalUser && !native())
        {
            Transcript = "Speech-input selection requires the owning unlocked host and a fresh input with no pending question/approval.";
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify speech input.", command.Error!);
            return;
        }
        var origin = OriginalOrigin(initiator);
        var hostEligible = CaptureAudioControlEligibility(origin);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var consent = HasVoiceConsent;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var owner = auditImpactOwnerRevision();
        var ownerBinding = Interlocked.Read(ref voiceOwnershipBindingRevision);
        var microphone = SelectedMicrophone;
        var deviceRevision = MicrophoneTopologyRevision;
        bool Eligible() => !disposed && hostEligible() && assistantNameConfiguration.Get().Revision == nameRevision
            && HasVoiceConsent == consent && Interlocked.Read(ref voiceRecoveryRevision) == recovery
            && auditImpactOwnerRevision() == owner && Interlocked.Read(ref voiceOwnershipBindingRevision) == ownerBinding
            && SelectedMicrophone == microphone && MicrophoneTopologyRevision == deviceRevision
            && (initiator != SecurityAuditInitiator.LocalUser || ReferenceEquals(native, speechInputNativeLifetime) && native());
        speechInputActive = true;
        OnPropertyChanged(nameof(CanChangeSpeechInputNative));
        try
        {
            if (command.Operation == AppearanceCommandOperation.Set)
            {
                var changed = await service.SelectAsync(command.Selection
                    ?? throw new InvalidOperationException("An exact speech-input tuple is required."),
                    service.Get().Revision, CallPolicyRevision, origin, initiator, communicationPolicy, Eligible, CancellationToken.None,
                    (review, token) => ReviewSpeechInputAsync(review, Eligible, reviewReady!, token));
                if (!changed) { Transcript = "Speech-input selection was denied by the current call/channel policy; no preference changed."; return; }
            }
            if (Eligible())
            {
                PresentResponse(AssistantState.Information, "Speech input.", service.Get().Describe()
                    + Environment.NewLine + SpeechInputCommand.Syntax, refreshOutput: false);
            }
        }
        catch (OperationCanceledException)
        {
            if (!disposed) { Transcript = "Speech-input review or switching was cancelled. " + service.Get().Describe(); }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            ApplicationLog.Error(logger, exception, "Selecting the speech-input profile");
            if (!disposed) { ShowFailure("Speech-input selection was not confirmed.", service.Get().Describe() + Environment.NewLine + exception.Message); }
        }
        finally
        {
            speechInputActive = false;
            OnPropertyChanged(nameof(SpeechInputStatus));
            OnPropertyChanged(nameof(CanChangeSpeechInputNative));
        }
    }

    private async Task<bool> ReviewSpeechInputAsync(SpeechInputProfileReview review, Func<bool> eligible,
        TaskCompletionSource ready, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingSpeechInputReview(review, completion, eligible);
        void OnAdmissionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!eligible()) { completion.TrySetResult(false); }
        }
        try
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Speech-input review admission changed."); }
                Volatile.Write(ref pendingSpeechInputReview, pending);
                PropertyChanged += OnAdmissionChanged;
                NotifySpeechInputReview();
                PresentResponse(AssistantState.Information, "Review speech-input switch.", review.Describe()
                    + Environment.NewLine + "Confirm speech.input or cancel speech.input.", refreshOutput: false);
                ready.TrySetResult();
                return Task.CompletedTask;
            });
            return await completion.Task.WaitAsync(token);
        }
        finally
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                PropertyChanged -= OnAdmissionChanged;
                Volatile.Write(ref pendingSpeechInputReview, null);
                NotifySpeechInputReview();
                return Task.CompletedTask;
            });
        }
    }

    private bool CompleteSpeechInputReview(bool confirm, SecurityAuditInitiator initiator)
    {
        var pending = Volatile.Read(ref pendingSpeechInputReview);
        if (pending is null || confirm && (!pending.Eligible()
            || OriginalOrigin(initiator) != pending.Review.Origin
            || initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)))
        {
            Transcript = "No current speech-input review can be confirmed by this original channel. Inspect and review again.";
            return false;
        }
        var accepted = pending.Completion.TrySetResult(confirm);
        NotifySpeechInputReview();
        return accepted;
    }

    private void NotifySpeechInputReview()
    {
        OnPropertyChanged(nameof(HasSpeechInputReview));
        OnPropertyChanged(nameof(CanConfirmSpeechInputReview));
        OnPropertyChanged(nameof(SpeechInputReviewStatus));
    }
}
