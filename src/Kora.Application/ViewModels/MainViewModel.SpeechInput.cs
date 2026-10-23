using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
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

    public IReadOnlyList<SpeechInputProfile> SpeechInputProfiles => SpeechInputProfileCatalogue.Profiles;
    public IReadOnlyList<SpeechInputResource> SpeechInputResources { get; } = Enum.GetValues<SpeechInputResource>();
    public IReadOnlyList<SpeechInputResidency> SpeechInputResidencies { get; } = Enum.GetValues<SpeechInputResidency>();
    public SpeechInputProfile? SelectedSpeechInputProfile
    {
        get => selectedSpeechInputProfile;
        set => SetProperty(ref selectedSpeechInputProfile, value);
    }
    public SpeechInputResource SelectedSpeechInputResource
    {
        get => selectedSpeechInputResource;
        set => SetProperty(ref selectedSpeechInputResource, value);
    }
    public SpeechInputResidency SelectedSpeechInputResidency
    {
        get => selectedSpeechInputResidency;
        set => SetProperty(ref selectedSpeechInputResidency, value);
    }
    public string SpeechInputStatus => speechInputConfiguration?.Get().Describe()
        ?? "Speech-input profile selection is unavailable. Existing activated capture is unchanged.";
    public bool CanChangeSpeechInputNative => speechInputConfiguration is not null && !speechInputActive
        && speechInputNativeLifetime() && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public AsyncCommand SaveSpeechInputCommand { get; private set; } = null!;
    public AsyncCommand InspectSpeechInputCommand { get; private set; } = null!;

    public void BindSpeechInputNativeLifetime(Func<bool> lifetime)
    {
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
        SaveSpeechInputCommand = createCommand(() => ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Set,
            new(SelectedSpeechInputProfile?.Id ?? throw new InvalidOperationException("Choose one exact known speech-input profile."),
                SelectedSpeechInputResource, SelectedSpeechInputResidency)), SecurityAuditInitiator.LocalUser));
        InspectSpeechInputCommand = createCommand(() => ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Get),
            SecurityAuditInitiator.LocalUser));
    }

    internal async Task ExecuteSpeechInputCommandAsync(SpeechInputCommand command, SecurityAuditInitiator initiator)
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
        bool Eligible() => !disposed && hostEligible() && assistantNameConfiguration.Get().Revision == nameRevision
            && (initiator != SecurityAuditInitiator.LocalUser || ReferenceEquals(native, speechInputNativeLifetime) && native());
        speechInputActive = true;
        OnPropertyChanged(nameof(CanChangeSpeechInputNative));
        try
        {
            if (command.Operation == AppearanceCommandOperation.Set)
            {
                var changed = await service.SelectAsync(command.Selection
                    ?? throw new InvalidOperationException("An exact speech-input tuple is required."),
                    service.Get().Revision, CallPolicyRevision, origin, initiator, communicationPolicy, Eligible, CancellationToken.None);
                if (!changed) { Transcript = "Speech-input selection was denied by the current call/channel policy; no preference changed."; return; }
            }
            if (Eligible())
            {
                PresentResponse(AssistantState.Information, "Speech input.", service.Get().Describe()
                    + Environment.NewLine + SpeechInputCommand.Syntax, refreshOutput: false);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentOutOfRangeException or OperationCanceledException)
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
}
