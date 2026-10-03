using System.Collections.ObjectModel;
using System.Globalization;

using Kora.Application.Infrastructure;
using Kora.Application.Diagnostics;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Platform;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string ApplicationRestartAction = "application.restart";
    private const string AppearanceThemeConfigurationAction = "configuration.appearance-theme";
    private const string ConstellationDotSizeConfigurationAction = "configuration.constellation-dot-size";
    private const string ConstellationMovementSpeedConfigurationAction = "configuration.constellation-movement-speed";
    private const string ConstellationPositionConfigurationAction = "configuration.constellation-position";
    private const string ConstellationSizeConfigurationAction = "configuration.constellation-size";
    private const string PresenceTimeoutConfigurationAction = "configuration.presence-timeout";
    private const string ResponseWindowConfigurationAction = "configuration.response-window";
    private const string AssistantNameConfigurationAction = "configuration.assistant-name";
    private const string CallAwareConfigurationAction = "configuration.call-aware-policy";
    private const string CurrentApplicationTarget = "application.current";
    private const string CurrentMachineTarget = "machine.current";
    private const string CurrentWindowsSessionTarget = "windows-session.current";
    private const string DeviceLocalPreferencesTarget = "preferences.device-local";
    private const string MicrophoneConfigurationAction = "configuration.microphone";
    private const string OutputDeviceConfigurationAction = "configuration.audio-output";
    private const string ResponseOutputConfigurationAction = "configuration.response-output";
    private const string SpeechProviderInstallAction = "speech-provider.install";
    private const string SpeechProviderRemoveAction = "speech-provider.remove";
    private const string SpeechProviderSelectionConfigurationAction = "configuration.speech-provider";
    private const string SessionLockAction = "session.lock";
    private const string ShutdownApprovalAction = "power.shutdown";
    private const string RestartApprovalAction = "power.restart";
    private const string VoiceSelectionConfigurationAction = "configuration.voice-selection";

    private readonly BuiltInCommandCatalog commandCatalog;
    private readonly BuiltInCommandRouter commandRouter;
    private readonly DependencyBootstrapper dependencyBootstrapper;
    private readonly IMicrophoneAccessService microphoneAccessService;
    private readonly IVoiceRecognitionService voiceRecognition;
    private readonly ITextToSpeechService textToSpeech;
    private readonly IAssistantNamePreferences assistantNamePreferences;
    private readonly IAppearancePreferences appearancePreferences;
    private readonly ITextToSpeechPreferences textToSpeechPreferences;
    private readonly IAudioDevicePreferences audioDevicePreferences;
    private readonly IResponseOutputPreferences responseOutputPreferences;
    private readonly ICallAwarePreferences callAwarePreferences;
    private readonly ICallStateService callStateService;
    private readonly ISessionController sessionController;
    private readonly IApplicationProcessController applicationProcessController;
    private readonly IUiDispatcher uiDispatcher;
    private readonly IApplicationInfo applicationInfo;
    private readonly ISecurityAuditLog securityAuditLog;
    private readonly ILogger<MainViewModel> logger;
    private MicrophoneDevice? selectedMicrophone;
    private SpeechProvider? selectedSpeechProvider;
    private SpeechVoice? selectedVoice;
    private AudioOutputDevice? selectedOutputDevice;
    private MicrophoneDevice? systemDefaultMicrophone;
    private AudioOutputDevice? systemDefaultOutputDevice;
    private string assistantName = AssistantNameRules.DefaultName;
    private string assistantNameInput = AssistantNameRules.DefaultName;
    private string assistantNameSettingStatus =
        "Use 1-3 words and up to 32 characters. Letters, numbers, spaces, apostrophes, and hyphens are supported.";
    private AssistantState state = AssistantState.Information;
    private string responseTitle = "A voice-first companion.";
    private string responseBody =
        $"Choose a microphone, enable listening, then say “{AssistantNameRules.DefaultName}, what can you do?”";
    private string transcript = "No command heard yet.";
    private string commandText = $"{AssistantNameRules.DefaultName}, what can you do?";
    private bool isListening;
    private bool isSpeaking;
    private bool isBusy;
    private bool isSpeechProviderOperationActive;
    private bool suppressVoicePreferenceSave;
    private bool suppressSpeechProviderPreferenceSave;
    private bool suppressAudioDevicePreferenceSave;
    private bool suppressAppearancePreferenceSave;
    private bool suppressConstellationPreferenceSave;
    private bool suppressPresencePreferenceSave;
    private bool suppressResponseWindowPreferenceSave;
    private bool suppressResponseModeSave;
    private bool suppressCallAwarePreferenceSave;
    private bool isInitializing;
    private bool forceVisualResponse;
    private string microphoneAvailabilityMessage = "Checking Windows microphone input devices.";
    private string speechProviderAvailabilityMessage = "Checking speech providers.";
    private string speechProviderOperationStatus = string.Empty;
    private int speechProviderOperationProgress;
    private string voiceAvailabilityMessage = "Checking installed Windows speech voices.";
    private string outputDeviceAvailabilityMessage = "Checking Windows audio output devices.";
    private ApplicationThemeMode themeMode = ApplicationThemeMode.System;
    private int presenceTimeoutSeconds = PresenceSettings.DefaultTimeoutSeconds;
    private int constellationSizePixels = ConstellationSettings.DefaultSizePixels;
    private int constellationDotSizePercent = ConstellationSettings.DefaultDotSizePercent;
    private int constellationMovementSpeedPercent = ConstellationSettings.DefaultMovementSpeedPercent;
    private ConstellationPosition? constellationPosition;
    private ResponseWindowSettings responseWindowSettings = ResponseWindowSettings.Default;
    private ResponseOutputMode defaultResponseMode = ResponseOutputMode.Hybrid;
    private ResponseOutputMode? queueResponseMode;
    private ResponseOutputMode? taskResponseMode;
    private CallState currentCallState;
    private bool showVisualTextDuringCalls = true;
    private bool allowVoiceActivationDuringCalls = true;
    private MicrophoneAccessStatus microphoneAccessStatus = new(
        MicrophoneAccessState.Unknown,
        "Windows microphone access has not been checked.");
    private string? listeningPauseReason;
    private string? activeSpokenText;
    private SpeechVoice? activeSpeechVoice;
    private BuiltInAction? pendingPowerAction;
    private SecurityAuditEvent? pendingPowerAudit;

    public MainViewModel(
        BuiltInCommandCatalog commandCatalog,
        BuiltInCommandRouter commandRouter,
        DependencyBootstrapper dependencyBootstrapper,
        IMicrophoneAccessService microphoneAccessService,
        IVoiceRecognitionService voiceRecognition,
        ITextToSpeechService textToSpeech,
        IAssistantNamePreferences assistantNamePreferences,
        IAppearancePreferences appearancePreferences,
        ITextToSpeechPreferences textToSpeechPreferences,
        IAudioDevicePreferences audioDevicePreferences,
        IResponseOutputPreferences responseOutputPreferences,
        ICallAwarePreferences callAwarePreferences,
        ICallStateService callStateService,
        ISessionController sessionController,
        IApplicationProcessController applicationProcessController,
        IUiDispatcher uiDispatcher,
        IApplicationInfo applicationInfo,
        ISecurityAuditLog securityAuditLog,
        ILogger<MainViewModel> logger)
    {
        this.commandCatalog = commandCatalog;
        this.commandRouter = commandRouter;
        this.dependencyBootstrapper = dependencyBootstrapper;
        this.microphoneAccessService = microphoneAccessService;
        this.voiceRecognition = voiceRecognition;
        this.textToSpeech = textToSpeech;
        this.assistantNamePreferences = assistantNamePreferences;
        this.appearancePreferences = appearancePreferences;
        this.textToSpeechPreferences = textToSpeechPreferences;
        this.audioDevicePreferences = audioDevicePreferences;
        this.responseOutputPreferences = responseOutputPreferences;
        this.callAwarePreferences = callAwarePreferences;
        this.callStateService = callStateService;
        currentCallState = callStateService.CurrentState;
        this.sessionController = sessionController;
        this.applicationProcessController = applicationProcessController;
        this.uiDispatcher = uiDispatcher;
        this.applicationInfo = applicationInfo;
        this.securityAuditLog = securityAuditLog;
        this.logger = logger;

        ToggleListeningCommand = new AsyncCommand(
            ToggleListeningAsync,
            () => EffectiveMicrophone is not null
                  && !IsBusy
                  && (IsListening
                      || (IsVoiceActivationAvailable && !IsMicrophoneAccessDenied)));
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        RunTypedCommand = new AsyncCommand(RunTypedCommandAsync, () => !string.IsNullOrWhiteSpace(CommandText) && !IsBusy);
        PreviewVoiceCommand = new AsyncCommand(
            PreviewVoiceAsync,
            () => IsSpeechOutputAvailable && !IsBusy);
        StopSpeechCommand = new AsyncCommand(StopSpeakingAsync, () => IsSpeaking);
        DownloadSpeechProviderCommand = new AsyncCommand(
            DownloadSpeechProviderAsync,
            () => CanDownloadSpeechProvider);
        RemoveSpeechProviderCommand = new AsyncCommand(
            RemoveSpeechProviderAsync,
            () => CanRemoveSpeechProvider);
        ToggleCallVisualOverrideCommand = new AsyncCommand(ToggleCallVisualOverrideAsync);
        ToggleCallVoiceActivationCommand = new AsyncCommand(ToggleCallVoiceActivationAsync);
        OpenMicrophonePrivacySettingsCommand = new AsyncCommand(OpenMicrophonePrivacySettingsAsync);
        ApplyAssistantNameCommand = new AsyncCommand(
            () => SetAssistantNameAsync(AssistantNameInput),
            CanApplyAssistantName);

        voiceRecognition.TranscriptRecognized += OnTranscriptRecognized;
        voiceRecognition.RecognitionFailed += OnRecognitionFailed;
        callStateService.StateChanged += OnCallStateChanged;
    }

    public MicrophoneAccessStatus MicrophoneAccessStatus
    {
        get => microphoneAccessStatus;
        private set
        {
            if (SetProperty(ref microphoneAccessStatus, value))
            {
                OnPropertyChanged(nameof(MicrophoneAccessMessage));
                OnPropertyChanged(nameof(IsMicrophoneAccessDenied));
                OnPropertyChanged(nameof(ListeningStatus));
                ToggleListeningCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string MicrophoneAccessMessage => MicrophoneAccessStatus.Detail;

    public bool IsMicrophoneAccessDenied =>
        MicrophoneAccessStatus.State == MicrophoneAccessState.Denied;

    private async void OnCallStateChanged(object? sender, CallStateChangedEventArgs eventArgs)
    {
        try
        {
            ApplicationLog.CallStateChanged(logger, eventArgs.State);
            await uiDispatcher.InvokeAsync(() => ApplyCallStateAsync(eventArgs.State));
        }
        catch (InvalidOperationException exception)
        {
            ApplicationLog.Error(logger, exception, "Applying the call-aware output policy");
            ShowFailure("The call-aware output policy could not be applied.", exception.Message);
        }
    }

    private async Task ApplyCallStateAsync(CallState callState)
    {
        CurrentCallState = callState;
        if (!IsCallDetected)
        {
            return;
        }

        if (!AllowVoiceActivationDuringCalls && IsListening)
        {
            await StopListeningAsync();
        }

        await ApplyCallVisualOverrideAsync();
    }

    private async Task ApplyCallVisualOverrideAsync()
    {
        if (!IsCallVisualOverrideActive)
        {
            return;
        }

        if (IsSpeaking)
        {
            await textToSpeech.StopAsync();
            IsSpeaking = false;
            activeSpokenText = null;
        }

        WindowActionRequested?.Invoke(this, WindowAction.Show);
    }

    public event EventHandler<WindowAction>? WindowActionRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? DocumentationRequested;

    public ObservableCollection<MicrophoneDevice> Microphones { get; } = [];

    public ObservableCollection<SpeechProvider> SpeechProviders { get; } = [];

    public ObservableCollection<SpeechVoice> Voices { get; } = [];

    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = [];

    public ObservableCollection<DependencyStatus> Dependencies { get; } = [];

    public IReadOnlyList<CommandDefinition> Commands => commandCatalog.GetCommands(AssistantName);

    public IReadOnlyList<ResponseOutputMode> ResponseOutputModes { get; } =
        Enum.GetValues<ResponseOutputMode>();

    public IReadOnlyList<ResponseModeOverrideOption> ResponseModeOptions { get; } =
    [
        new("Both audible and visual", ResponseOutputMode.Hybrid),
        new("Audible only", ResponseOutputMode.VoiceOnly),
        new("Visual only", ResponseOutputMode.VisualOnly),
    ];

    public IReadOnlyList<ResponseModeOverrideOption> ResponseModeOverrideOptions { get; } =
    [
        new("Inherit", null),
        new("Both audible and visual", ResponseOutputMode.Hybrid),
        new("Audible only", ResponseOutputMode.VoiceOnly),
        new("Visual only", ResponseOutputMode.VisualOnly),
    ];

    public IReadOnlyList<ApplicationThemeMode> ThemeModes { get; } =
        Enum.GetValues<ApplicationThemeMode>();

    public AsyncCommand ToggleListeningCommand { get; }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand RunTypedCommand { get; }

    public AsyncCommand PreviewVoiceCommand { get; }

    public AsyncCommand StopSpeechCommand { get; }

    public AsyncCommand DownloadSpeechProviderCommand { get; }

    public AsyncCommand RemoveSpeechProviderCommand { get; }

    public AsyncCommand ToggleCallVisualOverrideCommand { get; }

    public AsyncCommand ToggleCallVoiceActivationCommand { get; }

    public AsyncCommand OpenMicrophonePrivacySettingsCommand { get; }

    public AsyncCommand ApplyAssistantNameCommand { get; }

    public string AssistantName
    {
        get => assistantName;
        private set => SetProperty(ref assistantName, value);
    }

    public string AssistantNameInput
    {
        get => assistantNameInput;
        set
        {
            if (SetProperty(ref assistantNameInput, value))
            {
                UpdateAssistantNameSettingStatus();
                ApplyAssistantNameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string AssistantNameSettingStatus
    {
        get => assistantNameSettingStatus;
        private set => SetProperty(ref assistantNameSettingStatus, value);
    }

    public string AssistantInitial =>
        StringInfo.GetNextTextElement(AssistantName).ToLower(CultureInfo.CurrentCulture);

    public string SettingsWindowTitle => $"{AssistantName} settings";

    public string SettingsSubtitle => $"{AssistantName} preferences on this device";

    public string AppearanceSettingsDescription =>
        $"Choose the theme and ambient-presence behavior used by every {AssistantName} visual surface.";

    public string AppearanceThemeDescription =>
        $"System follows the current Windows light or dark preference. Light and Dark override it for all {AssistantName} surfaces.";

    public string PresenceTimeoutDescription =>
        "Hide the constellation and an unpinned response window after this many seconds without interaction.";

    public string ConstellationSizeDescription =>
        $"Overall constellation footprint: {ConstellationSizePixels} pixels.";

    public string ConstellationDotSizeDescription =>
        $"Relative particle diameter: {ConstellationDotSizePercent}%.";

    public string ConstellationMovementSpeedDescription =>
        $"Relative particle movement speed: {ConstellationMovementSpeedPercent}%.";

    public string MainCaptureDescription =>
        $"{AssistantName} enumerates devices without recording. Capture starts only after you enable listening.";

    public string SpeechAudioSettingsDescription =>
        $"Choose how {AssistantName} listens and speaks on this device.";

    public string ResponseSettingsDescription =>
        $"Choose when {AssistantName} uses visual text, speech, or both.";

    public string ReadinessSettingsDescription =>
        $"Review local dependencies used by {AssistantName}.";

    public ApplicationThemeMode ThemeMode
    {
        get => themeMode;
        set => _ = SetThemeMode(value);
    }

    public bool SetThemeMode(
        ApplicationThemeMode value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The appearance theme is invalid.");
        }

        if (ThemeMode == value)
        {
            return true;
        }

        if (!suppressAppearancePreferenceSave
            && !SaveAppearancePreference(value, initiator))
        {
            OnPropertyChanged(nameof(ThemeMode));
            return false;
        }

        return SetProperty(ref themeMode, value, nameof(ThemeMode));
    }

    public int PresenceTimeoutSeconds
    {
        get => presenceTimeoutSeconds;
        set => _ = SetPresenceTimeoutSeconds(value);
    }

    public bool SetPresenceTimeoutSeconds(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        PresenceSettings.ValidateTimeoutSeconds(value);
        if (PresenceTimeoutSeconds == value)
        {
            return true;
        }

        if (!suppressPresencePreferenceSave
            && !SavePresenceTimeoutPreference(value, initiator))
        {
            OnPropertyChanged(nameof(PresenceTimeoutSeconds));
            return false;
        }

        return SetProperty(
            ref presenceTimeoutSeconds,
            value,
            nameof(PresenceTimeoutSeconds));
    }

    public bool IsResponseAlwaysVisible
    {
        get => responseWindowSettings.AlwaysShow;
        set => _ = SetResponseAlwaysVisible(value);
    }

    public bool SetResponseAlwaysVisible(
        bool value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        SetResponseWindowSettings(
            responseWindowSettings with { AlwaysShow = value },
            initiator);

    public bool IsResponseWindowTopmost
    {
        get => responseWindowSettings.Topmost;
        set => _ = SetResponseWindowTopmost(value);
    }

    public bool SetResponseWindowTopmost(
        bool value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        SetResponseWindowSettings(
            responseWindowSettings with { Topmost = value },
            initiator);

    public ResponseWindowPosition? ResponseWindowPosition =>
        responseWindowSettings.Position;

    public bool SetResponseWindowPosition(
        ResponseWindowPosition value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SetResponseWindowSettings(
            responseWindowSettings with { Position = value },
            initiator);
    }

    public int ConstellationSizePixels
    {
        get => constellationSizePixels;
        set => _ = SetConstellationSizePixels(value);
    }

    public bool SetConstellationSizePixels(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        ConstellationSettings.ValidateSizePixels(value);
        if (ConstellationSizePixels == value)
        {
            return true;
        }

        if (!suppressConstellationPreferenceSave
            && !SaveConstellationPreference(
                () => appearancePreferences.SaveConstellationSizePixels(value),
                ConstellationSizeConfigurationAction,
                "constellation size",
                initiator))
        {
            OnPropertyChanged(nameof(ConstellationSizePixels));
            return false;
        }

        SetProperty(ref constellationSizePixels, value, nameof(ConstellationSizePixels));
        OnPropertyChanged(nameof(ConstellationSizeDescription));
        return true;
    }

    public int ConstellationDotSizePercent
    {
        get => constellationDotSizePercent;
        set => _ = SetConstellationDotSizePercent(value);
    }

    public bool SetConstellationDotSizePercent(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        ConstellationSettings.ValidateDotSizePercent(value);
        if (ConstellationDotSizePercent == value)
        {
            return true;
        }

        if (!suppressConstellationPreferenceSave
            && !SaveConstellationPreference(
                () => appearancePreferences.SaveConstellationDotSizePercent(value),
                ConstellationDotSizeConfigurationAction,
                "constellation dot size",
                initiator))
        {
            OnPropertyChanged(nameof(ConstellationDotSizePercent));
            return false;
        }

        SetProperty(
            ref constellationDotSizePercent,
            value,
            nameof(ConstellationDotSizePercent));
        OnPropertyChanged(nameof(ConstellationDotSizeDescription));
        return true;
    }

    public int ConstellationMovementSpeedPercent
    {
        get => constellationMovementSpeedPercent;
        set => _ = SetConstellationMovementSpeedPercent(value);
    }

    public bool SetConstellationMovementSpeedPercent(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        ConstellationSettings.ValidateMovementSpeedPercent(value);
        if (ConstellationMovementSpeedPercent == value)
        {
            return true;
        }

        if (!suppressConstellationPreferenceSave
            && !SaveConstellationPreference(
                () => appearancePreferences.SaveConstellationMovementSpeedPercent(value),
                ConstellationMovementSpeedConfigurationAction,
                "constellation movement speed",
                initiator))
        {
            OnPropertyChanged(nameof(ConstellationMovementSpeedPercent));
            return false;
        }

        SetProperty(
            ref constellationMovementSpeedPercent,
            value,
            nameof(ConstellationMovementSpeedPercent));
        OnPropertyChanged(nameof(ConstellationMovementSpeedDescription));
        return true;
    }

    public ConstellationPosition? ConstellationPosition => constellationPosition;

    public bool SetConstellationPosition(
        ConstellationPosition value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ConstellationPosition == value)
        {
            return true;
        }

        if (!suppressConstellationPreferenceSave
            && !SaveConstellationPreference(
                () => appearancePreferences.SaveConstellationPosition(value),
                ConstellationPositionConfigurationAction,
                "constellation position",
                initiator))
        {
            OnPropertyChanged(nameof(ConstellationPosition));
            return false;
        }

        return SetProperty(
            ref constellationPosition,
            value,
            nameof(ConstellationPosition));
    }

    public MicrophoneDevice? SelectedMicrophone
    {
        get => selectedMicrophone;
        set
        {
            var selectionChanged = !string.Equals(
                selectedMicrophone?.Id,
                value?.Id,
                StringComparison.Ordinal);
            if (SetProperty(ref selectedMicrophone, value))
            {
                ToggleListeningCommand.NotifyCanExecuteChanged();
                UpdateMicrophoneAvailability(selectedMicrophoneUnavailable: false);
                if (selectionChanged
                    && !suppressAudioDevicePreferenceSave
                    && value is not null)
                {
                    SaveMicrophonePreference(value);
                }
            }
        }
    }

    public SpeechProvider? SelectedSpeechProvider
    {
        get => selectedSpeechProvider;
        set
        {
            if (SetProperty(ref selectedSpeechProvider, value))
            {
                NotifySpeechProviderStateChanged();
                if (!suppressSpeechProviderPreferenceSave && value is not null)
                {
                    if (value.IsInstalled)
                    {
                        SaveSpeechProviderPreference(value.Id);
                    }

                    PopulateVoicesForProvider(
                        textToSpeech.GetVoices(),
                        preferredVoiceId: null,
                        savedVoiceUnavailable: false);
                }
                else
                {
                    UpdateSpeechProviderAvailability();
                }
            }
        }
    }

    public SpeechVoice? SelectedVoice
    {
        get => selectedVoice;
        set
        {
            if (SetProperty(ref selectedVoice, value))
            {
                if (SelectedSpeechProvider is { IsInstalled: true })
                {
                    SetActiveSpeechVoice(value);
                }

                PreviewVoiceCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsSpeechOutputAvailable));
                NotifyOutputPolicyChanged();
                UpdateVoiceAvailability(savedVoiceUnavailable: false);
                if (!suppressVoicePreferenceSave && value is not null)
                {
                    SaveVoicePreference(value.Id);
                }
            }
        }
    }

    public AudioOutputDevice? SelectedOutputDevice
    {
        get => selectedOutputDevice;
        set
        {
            var selectionChanged = !string.Equals(
                selectedOutputDevice?.Id,
                value?.Id,
                StringComparison.Ordinal);
            if (SetProperty(ref selectedOutputDevice, value))
            {
                PreviewVoiceCommand.NotifyCanExecuteChanged();
                NotifyOutputPolicyChanged();
                UpdateOutputDeviceAvailability();
                if (selectionChanged
                    && !suppressAudioDevicePreferenceSave
                    && value is not null)
                {
                    SaveOutputDevicePreference(value);
                }
            }
        }
    }

    public string MicrophoneAvailabilityMessage
    {
        get => microphoneAvailabilityMessage;
        private set => SetProperty(ref microphoneAvailabilityMessage, value);
    }

    public string VoiceAvailabilityMessage
    {
        get => voiceAvailabilityMessage;
        private set => SetProperty(ref voiceAvailabilityMessage, value);
    }

    public string SpeechProviderAvailabilityMessage
    {
        get => speechProviderAvailabilityMessage;
        private set => SetProperty(ref speechProviderAvailabilityMessage, value);
    }

    public string SpeechProviderOperationStatus
    {
        get => speechProviderOperationStatus;
        private set => SetProperty(ref speechProviderOperationStatus, value);
    }

    public int SpeechProviderOperationProgress
    {
        get => speechProviderOperationProgress;
        private set => SetProperty(ref speechProviderOperationProgress, value);
    }

    public bool IsSpeechProviderOperationActive
    {
        get => isSpeechProviderOperationActive;
        private set
        {
            SetProperty(ref isSpeechProviderOperationActive, value);
            NotifySpeechProviderStateChanged();
        }
    }

    public bool CanDownloadSpeechProvider =>
        SelectedSpeechProvider is
        {
            IsBuiltIn: false,
            IsInstalled: false,
        }
        && !IsSpeechProviderOperationActive;

    public bool CanRemoveSpeechProvider =>
        SelectedSpeechProvider is
        {
            IsBuiltIn: false,
            IsInstalled: true,
        }
        && !IsSpeechProviderOperationActive
        && !IsSpeaking;

    public string SpeechProviderDownloadButtonText
    {
        get
        {
            if (SelectedSpeechProvider is null)
            {
                return "Download";
            }

            if (SelectedSpeechProvider.DownloadSizeBytes is not long bytes)
            {
                return "Download";
            }

            return $"Download ({Math.Ceiling(bytes / 1024d / 1024d):0} MB)";
        }
    }

    public string OutputDeviceAvailabilityMessage
    {
        get => outputDeviceAvailabilityMessage;
        private set => SetProperty(ref outputDeviceAvailabilityMessage, value);
    }

    public ResponseOutputMode DefaultResponseMode
    {
        get => defaultResponseMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The response output mode is invalid.");
            }

            if (SetProperty(ref defaultResponseMode, value))
            {
                OnPropertyChanged(nameof(DefaultResponseModeOption));
                NotifyOutputPolicyChanged();
                if (!suppressResponseModeSave)
                {
                    SaveResponseOutputPreference(value);
                }
            }
        }
    }

    public ResponseModeOverrideOption DefaultResponseModeOption
    {
        get => ResponseModeOptions.Single(option => option.Mode == DefaultResponseMode);
        set
        {
            if (!ResponseModeOptions.Contains(value))
            {
                throw new ArgumentException(
                    "The response mode option is unavailable.",
                    nameof(value));
            }

            DefaultResponseMode = value.Mode!.Value;
        }
    }

    public ResponseOutputMode ConfiguredResponseMode
    {
        get => DefaultResponseMode;
        set => DefaultResponseMode = value;
    }

    public ResponseOutputMode? QueueResponseMode
    {
        get => queueResponseMode;
        set
        {
            ValidateResponseModeOverride(value, nameof(value));
            if (SetProperty(ref queueResponseMode, value))
            {
                OnPropertyChanged(nameof(QueueResponseModeOption));
                NotifyOutputPolicyChanged();
            }
        }
    }

    public ResponseModeOverrideOption QueueResponseModeOption
    {
        get => ResponseModeOverrideOptions.Single(option => option.Mode == QueueResponseMode);
        set
        {
            ValidateResponseModeOverrideOption(value, nameof(value));
            QueueResponseMode = value.Mode;
        }
    }

    public ResponseOutputMode? TaskResponseMode
    {
        get => taskResponseMode;
        set
        {
            ValidateResponseModeOverride(value, nameof(value));
            if (SetProperty(ref taskResponseMode, value))
            {
                OnPropertyChanged(nameof(TaskResponseModeOption));
                NotifyOutputPolicyChanged();
            }
        }
    }

    public ResponseModeOverrideOption TaskResponseModeOption
    {
        get => ResponseModeOverrideOptions.Single(option => option.Mode == TaskResponseMode);
        set
        {
            ValidateResponseModeOverrideOption(value, nameof(value));
            TaskResponseMode = value.Mode;
        }
    }

    public ResponseOutputMode EffectiveResponseMode =>
        ResponseOutputModeResolver.Resolve(DefaultResponseMode, QueueResponseMode, TaskResponseMode);

    public bool IsSpeechOutputAvailable =>
        activeSpeechVoice is not null
        && EffectiveOutputDevice is not null
        && !EffectiveOutputDevice.IsMuted;

    private MicrophoneDevice? EffectiveMicrophone =>
        SelectedMicrophone?.IsSystemDefault == true
            ? systemDefaultMicrophone
            : SelectedMicrophone;

    private AudioOutputDevice? EffectiveOutputDevice =>
        SelectedOutputDevice?.IsSystemDefault == true
            ? systemDefaultOutputDevice
            : SelectedOutputDevice;

    public bool IsVisualResponseVisible =>
        IsCallVisualOverrideActive
        || EffectiveResponseMode != ResponseOutputMode.VoiceOnly
        || forceVisualResponse
        || !IsSpeechOutputAvailable;

    public bool IsSpeechResponseEnabled =>
        EffectiveResponseMode != ResponseOutputMode.VisualOnly
        && !IsCallVisualOverrideActive
        && IsSpeechOutputAvailable;

    public CallState CurrentCallState
    {
        get => currentCallState;
        private set
        {
            if (SetProperty(ref currentCallState, value))
            {
                OnPropertyChanged(nameof(IsCallDetected));
                OnPropertyChanged(nameof(IsCallVisualOverrideActive));
                OnPropertyChanged(nameof(IsVoiceActivationAvailable));
                OnPropertyChanged(nameof(CallStateStatus));
                OnPropertyChanged(nameof(ListeningStatus));
                ToggleListeningCommand.NotifyCanExecuteChanged();
                NotifyOutputPolicyChanged();
            }
        }
    }

    public bool IsCallDetected =>
        CurrentCallState is CallState.Active or CallState.Suspected;

    public bool ShowVisualTextDuringCalls
    {
        get => showVisualTextDuringCalls;
        private set
        {
            if (SetProperty(ref showVisualTextDuringCalls, value))
            {
                OnPropertyChanged(nameof(IsCallVisualOverrideActive));
                OnPropertyChanged(nameof(CallVisualOverrideButtonText));
                OnPropertyChanged(nameof(CallVisualOverrideStatus));
                SaveCallAwareSettings();
                NotifyOutputPolicyChanged();
            }
        }
    }

    public bool AllowVoiceActivationDuringCalls
    {
        get => allowVoiceActivationDuringCalls;
        private set
        {
            if (SetProperty(ref allowVoiceActivationDuringCalls, value))
            {
                OnPropertyChanged(nameof(IsVoiceActivationAvailable));
                OnPropertyChanged(nameof(CallVoiceActivationButtonText));
                OnPropertyChanged(nameof(CallVoiceActivationStatus));
                OnPropertyChanged(nameof(ListeningStatus));
                SaveCallAwareSettings();
                ToggleListeningCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsCallVisualOverrideActive =>
        IsCallDetected && ShowVisualTextDuringCalls;

    public bool IsVoiceActivationAvailable =>
        !IsCallDetected || AllowVoiceActivationDuringCalls;

    public string CallVisualOverrideButtonText => ShowVisualTextDuringCalls
        ? "Use normal response mode during calls"
        : "Show visual text during calls";

    public string CallVisualOverrideStatus => ShowVisualTextDuringCalls
        ? "On · detected calls use visual-only responses"
        : "Off · detected calls use the normal response mode";

    public string CallVoiceActivationButtonText => AllowVoiceActivationDuringCalls
        ? "Disable voice activation during calls"
        : "Keep voice activation during calls";

    public string CallVoiceActivationStatus => AllowVoiceActivationDuringCalls
        ? $"On · {AssistantName} can continue listening during detected calls"
        : "Off · listening closes and remains unavailable during detected calls";

    public string CallStateStatus => CurrentCallState switch
    {
        CallState.Active => "A call is active.",
        CallState.Suspected => "Call activity is suspected.",
        CallState.Clear => "No call is currently detected.",
        CallState.Unknown => "Call detection is enabled but its current state is unknown.",
        _ => "Automatic call detection is unavailable. Normal response settings remain active.",
    };

    public string ResponseOutputStatus
    {
        get
        {
            var scope = TaskResponseMode is not null
                ? "Current task override"
                : QueueResponseMode is not null
                    ? "Current queue override"
                    : "Device default";
            var fallback = IsVisualResponseVisible
                           && !IsCallVisualOverrideActive
                           && EffectiveResponseMode == ResponseOutputMode.VoiceOnly
                ? " Visual text is forced because speech output is unavailable or failed."
                : string.Empty;
            var callOverride = IsCallVisualOverrideActive
                ? " Detected-call override: visual text only; voice activation remains independently configured."
                : string.Empty;
            var modeLabel = ResponseModeOptions.Single(
                option => option.Mode == EffectiveResponseMode).Label;
            return $"{scope}: {modeLabel}.{callOverride}{fallback}";
        }
    }

    public AssistantState State
    {
        get => state;
        private set
        {
            if (SetProperty(ref state, value))
            {
                OnPropertyChanged(nameof(StateLabel));
            }
        }
    }

    public string StateLabel => State.GetLabel();

    public string ResponseTitle
    {
        get => responseTitle;
        private set => SetProperty(ref responseTitle, value);
    }

    public string ResponseBody
    {
        get => responseBody;
        private set => SetProperty(ref responseBody, value);
    }

    public string Transcript
    {
        get => transcript;
        private set => SetProperty(ref transcript, value);
    }

    public string CommandText
    {
        get => commandText;
        set
        {
            if (SetProperty(ref commandText, value))
            {
                RunTypedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsListening
    {
        get => isListening;
        private set
        {
            if (SetProperty(ref isListening, value))
            {
                OnPropertyChanged(nameof(ListeningButtonText));
                OnPropertyChanged(nameof(ListeningStatus));
                PreviewVoiceCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsSpeechOutputAvailable));
                NotifyOutputPolicyChanged();
            }
        }
    }

    public bool IsSpeaking
    {
        get => isSpeaking;
        private set
        {
            if (SetProperty(ref isSpeaking, value))
            {
                StopSpeechCommand.NotifyCanExecuteChanged();
                RemoveSpeechProviderCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanRemoveSpeechProvider));
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                ToggleListeningCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                RunTypedCommand.NotifyCanExecuteChanged();
                PreviewVoiceCommand.NotifyCanExecuteChanged();
                ApplyAssistantNameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ListeningButtonText => IsListening ? "Disable listening" : "Enable listening";

    public string ListeningStatus => IsListening
        ? $"Wake listening on {SelectedMicrophone!.Name}"
        : IsMicrophoneAccessDenied
            ? "Microphone closed · Windows access is blocked"
            : listeningPauseReason
              ?? (IsVoiceActivationAvailable
                  ? "Microphone closed"
                  : "Microphone closed · voice activation paused during detected call");

    public async Task InitializeAsync()
    {
        isInitializing = true;
        try
        {
            await RefreshAsync();
            if (State != AssistantState.Failure
                && EffectiveMicrophone is not null
                && !IsListening)
            {
                if (IsMicrophoneAccessDenied)
                {
                    PresentResponse(
                        AssistantState.Information,
                        "Microphone access is blocked.",
                        MicrophoneAccessMessage);
                    return;
                }

                if (!IsVoiceActivationAvailable)
                {
                    return;
                }

                if (!sessionController.IsCurrentSessionUnlocked())
                {
                    SetListeningPauseReason(
                        "Microphone closed · listening paused because Windows reports this session as locked");
                    PresentResponse(
                        AssistantState.Information,
                        "Listening is paused.",
                        "Kora could not confirm that the Windows session is unlocked. Use Enable listening after returning to an unlocked interactive session.");
                    return;
                }

                await StartListeningAsync();
            }
        }
        finally
        {
            isInitializing = false;
        }
    }

    public async Task DetectMicrophonesAsync() => await RefreshAsync();

    public void ShowApplication()
    {
        if (State == AssistantState.Hidden)
        {
            State = AssistantState.Information;
        }

        ShowPresentation();
    }

    public void HideApplication()
    {
        State = AssistantState.Hidden;
        HidePresentation();
    }

    public void ShowPresentation() =>
        WindowActionRequested?.Invoke(this, WindowAction.Show);

    public void HidePresentation() =>
        WindowActionRequested?.Invoke(this, WindowAction.Hide);

    public void NotifyPresenceInteraction() =>
        WindowActionRequested?.Invoke(this, WindowAction.ShowPresence);

    public void ShowSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void ShowDocumentation() =>
        DocumentationRequested?.Invoke(this, EventArgs.Empty);

    public async Task ExitAsync()
    {
        ApplicationLog.Information(logger, "Kora exit was requested");
        await StopAudioAsync();
        WindowActionRequested?.Invoke(this, WindowAction.Close);
    }

    private async Task RefreshAsync()
    {
        ApplicationLog.Debug(logger, "Refreshing devices, preferences, and dependency readiness");
        IsBusy = true;
        try
        {
            MicrophoneAccessStatus = microphoneAccessService.GetStatus();
            var savedAssistantName = assistantNamePreferences.LoadName();
            try
            {
                ApplyAssistantNameState(savedAssistantName ?? AssistantNameRules.DefaultName);
            }
            catch (ArgumentException exception) when (savedAssistantName is not null)
            {
                throw new InvalidDataException(
                    "The saved assistant name conflicts with a built-in command.",
                    exception);
            }

            var savedThemeMode = appearancePreferences.LoadThemeMode();
            var savedPresenceTimeoutSeconds = appearancePreferences.LoadPresenceTimeoutSeconds();
            var savedConstellationSizePixels = appearancePreferences.LoadConstellationSizePixels();
            var savedConstellationDotSizePercent = appearancePreferences.LoadConstellationDotSizePercent();
            var savedConstellationMovementSpeedPercent =
                appearancePreferences.LoadConstellationMovementSpeedPercent();
            var savedConstellationPosition =
                appearancePreferences.LoadConstellationPosition();
            var savedResponseWindowSettings = appearancePreferences.LoadResponseWindowSettings();
            suppressAppearancePreferenceSave = true;
            suppressPresencePreferenceSave = true;
            suppressConstellationPreferenceSave = true;
            suppressResponseWindowPreferenceSave = true;
            try
            {
                ThemeMode = savedThemeMode ?? ApplicationThemeMode.System;
                PresenceTimeoutSeconds =
                    savedPresenceTimeoutSeconds ?? PresenceSettings.DefaultTimeoutSeconds;
                ConstellationSizePixels =
                    savedConstellationSizePixels ?? ConstellationSettings.DefaultSizePixels;
                ConstellationDotSizePercent =
                    savedConstellationDotSizePercent ?? ConstellationSettings.DefaultDotSizePercent;
                ConstellationMovementSpeedPercent =
                    savedConstellationMovementSpeedPercent
                    ?? ConstellationSettings.DefaultMovementSpeedPercent;
                SetProperty(
                    ref constellationPosition,
                    savedConstellationPosition,
                    nameof(ConstellationPosition));
                _ = SetResponseWindowSettings(
                    savedResponseWindowSettings ?? ResponseWindowSettings.Default,
                    SecurityAuditInitiator.System);
            }
            finally
            {
                suppressAppearancePreferenceSave = false;
                suppressPresencePreferenceSave = false;
                suppressConstellationPreferenceSave = false;
                suppressResponseWindowPreferenceSave = false;
            }

            var savedMicrophoneId = audioDevicePreferences.LoadMicrophoneId();
            var microphones = voiceRecognition.GetMicrophones();
            Microphones.Clear();
            Microphones.Add(SystemAudioDevices.Microphone);
            foreach (var microphone in microphones)
            {
                Microphones.Add(microphone);
            }

            systemDefaultMicrophone = voiceRecognition.GetDefaultMicrophone();
            var savedMicrophone = Microphones.FirstOrDefault(
                item => string.Equals(item.Id, savedMicrophoneId, StringComparison.Ordinal));
            var savedMicrophoneUnavailable =
                savedMicrophoneId is not null && savedMicrophone is null;
            var selectedSpeechProviderId = SelectedSpeechProvider?.Id;
            var selectedVoiceId = SelectedVoice?.Id;
            var speechProviders = textToSpeech.GetProviders();
            SpeechProviders.Clear();
            foreach (var provider in speechProviders)
            {
                SpeechProviders.Add(provider);
            }

            var voices = textToSpeech.GetVoices();
            var savedOutputDeviceId = audioDevicePreferences.LoadOutputDeviceId();
            var outputDevices = textToSpeech.GetOutputDevices();
            OutputDevices.Clear();
            OutputDevices.Add(SystemAudioDevices.Output);
            foreach (var outputDevice in outputDevices)
            {
                OutputDevices.Add(outputDevice);
            }

            systemDefaultOutputDevice = textToSpeech.GetDefaultOutputDevice();
            var savedOutputDevice = OutputDevices.FirstOrDefault(
                item => string.Equals(item.Id, savedOutputDeviceId, StringComparison.Ordinal));
            var savedOutputDeviceUnavailable =
                savedOutputDeviceId is not null && savedOutputDevice is null;
            suppressAudioDevicePreferenceSave = true;
            try
            {
                SelectedMicrophone = savedMicrophoneId is not null
                    ? savedMicrophone
                    : SystemAudioDevices.Microphone;
                SelectedOutputDevice = savedOutputDeviceId is not null
                    ? savedOutputDevice
                    : SystemAudioDevices.Output;
            }
            finally
            {
                suppressAudioDevicePreferenceSave = false;
            }

            UpdateMicrophoneAvailability(savedMicrophoneUnavailable);
            UpdateOutputDeviceAvailability(savedOutputDeviceUnavailable);
            ToggleListeningCommand.NotifyCanExecuteChanged();
            PreviewVoiceCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsSpeechOutputAvailable));
            NotifyOutputPolicyChanged();

            var savedSpeechProviderId = textToSpeechPreferences.LoadProviderId();
            var savedVoiceId = textToSpeechPreferences.LoadVoiceId();
            var savedResponseMode = responseOutputPreferences.LoadDefaultMode();
            var savedCallAwareSettings = callAwarePreferences.Load() ?? CallAwareSettings.Default;
            suppressResponseModeSave = true;
            suppressCallAwarePreferenceSave = true;
            try
            {
                DefaultResponseMode = savedResponseMode ?? ResponseOutputMode.Hybrid;
                ShowVisualTextDuringCalls = savedCallAwareSettings.ShowVisualTextDuringCalls;
                AllowVoiceActivationDuringCalls = savedCallAwareSettings.AllowVoiceActivationDuringCalls;
            }
            finally
            {
                suppressResponseModeSave = false;
                suppressCallAwarePreferenceSave = false;
            }

            CurrentCallState = callStateService.CurrentState;
            var preferredProviderId = selectedSpeechProviderId
                ?? savedSpeechProviderId
                ?? SpeechProviderIds.Windows;
            var preferredProvider = SpeechProviders.FirstOrDefault(
                    item => string.Equals(
                        item.Id,
                        preferredProviderId,
                        StringComparison.Ordinal))
                ?? SpeechProviders.FirstOrDefault(
                    item => string.Equals(
                        item.Id,
                        SpeechProviderIds.Windows,
                        StringComparison.Ordinal))
                ?? SpeechProviders.FirstOrDefault();
            var preferredVoiceId = selectedSpeechProviderId is not null
                ? selectedVoiceId
                : savedVoiceId;
            suppressSpeechProviderPreferenceSave = true;
            suppressVoicePreferenceSave = true;
            try
            {
                SelectedSpeechProvider = preferredProvider;
                PopulateVoicesForProvider(
                    voices,
                    preferredVoiceId,
                    ShouldReportUnavailableSavedVoice(
                        savedVoiceId,
                        selectedSpeechProviderId,
                        savedSpeechProviderId,
                        preferredProvider));
            }
            finally
            {
                suppressVoicePreferenceSave = false;
                suppressSpeechProviderPreferenceSave = false;
            }

            var statuses = await dependencyBootstrapper.ProbeAsync();
            Dependencies.Clear();
            foreach (var status in statuses)
            {
                Dependencies.Add(status);
            }

            var selectedOutputMuted = EffectiveOutputDevice?.IsMuted == true;
            var setupTitle = selectedOutputMuted
                ? "Audio output is muted."
                : (microphones.Count, Voices.Count, outputDevices.Count) switch
                {
                    (0, 0, _) or (0, _, 0) => "Voice setup is incomplete.",
                    (0, _, _) => "No microphone detected.",
                    (_, 0, _) => "Speech output is unavailable.",
                    (_, _, 0) => "Audio output is unavailable.",
                    _ => "Environment check complete.",
                };
            var setupBody = selectedOutputMuted
                ? "Unmute the selected Windows audio output or raise its volume above zero, then refresh. Visual responses remain available."
                : (microphones.Count, Voices.Count, outputDevices.Count) switch
                {
                    (0, 0, _) => $"Connect a microphone and install a Windows speech pack. {AssistantName} has not opened an audio device.",
                    (0, _, 0) => $"Connect microphone and audio output devices, then refresh. {AssistantName} has not opened an audio device.",
                    (0, _, _) => $"Connect a microphone and refresh. {AssistantName} has not opened an audio capture device.",
                    (_, 0, _) => "Install a Windows text-to-speech voice in Settings, then refresh. Typed and visual commands remain available.",
                    (_, _, 0) => "Connect or enable a Windows audio output device, then refresh. Typed and visual commands remain available.",
                    _ => "Listening starts automatically on the selected microphone. No model or network is required for built-in commands.",
                };
            PresentResponse(AssistantState.Information, setupTitle, setupBody);
            ApplicationLog.EnvironmentRefreshCompleted(
                logger,
                microphones.Count,
                Voices.Count,
                outputDevices.Count);
        }
        catch (InvalidDataException exception)
        {
            ApplicationLog.Error(logger, exception, "Loading a saved preference");
            ShowFailure("A saved setting is invalid.", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            ApplicationLog.Error(logger, exception, "Refreshing the environment because access was denied");
            ShowFailure("Storage or microphone access was denied.", exception.Message);
        }
        catch (IOException exception)
        {
            ApplicationLog.Error(logger, exception, "Refreshing the environment due to an I/O error");
            ShowFailure("Dependency probing failed.", exception.Message);
        }
        catch (AudioOutputDeviceUnavailableException exception)
        {
            ApplicationLog.Error(logger, exception, "Inspecting audio output");
            HandleAudioOutputFailure(exception, "Windows audio output is unavailable.");
        }
        catch (InvalidOperationException exception)
        {
            ApplicationLog.Error(logger, exception, "Refreshing speech services");
            SelectedVoice = null;
            ShowFailure("Speech services are unavailable.", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleListeningAsync()
    {
        if (IsListening)
        {
            await StopListeningAsync();
            SetListeningPauseReason(
                "Microphone closed · listening was disabled manually");
            ApplicationLog.Information(logger, "Voice activation was disabled by the user");
            ShowInformation("Listening disabled.", "The microphone capture device has been released.");
            return;
        }

        await StartListeningAsync();
    }

    private async Task StartListeningAsync()
    {
        IsBusy = true;
        try
        {
            await StartVoiceRecognitionAsync();
            PresentResponse(
                AssistantState.Listening,
                "I'm listening.",
                $"Say “{AssistantName}” followed by a supported command. Recognition stays local to Windows.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ApplicationLog.Error(logger, exception, "Starting the selected microphone");
            ShowFailure("The selected microphone is unavailable.", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            MicrophoneAccessStatus = microphoneAccessService.GetStatus();
            ApplicationLog.Error(logger, exception, "Starting Windows speech recognition");
            ShowFailure("Windows speech recognition is unavailable.", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopListeningAsync()
    {
        await voiceRecognition.StopAsync();
        IsListening = false;
        ApplicationLog.Information(logger, "Voice activation stopped");
    }

    private async Task StartVoiceRecognitionAsync()
    {
        var microphone = SelectedMicrophone!;
        var phrases = GetRecognitionPhrases();
        await voiceRecognition.StartAsync(microphone, phrases);
        IsListening = true;
        SetListeningPauseReason(null);
        ApplicationLog.Information(logger, "Voice activation started on the selected microphone");
    }

    private async Task DownloadSpeechProviderAsync()
    {
        var provider = SelectedSpeechProvider!;
        var audit = StartAudit(
            SecurityAuditCategory.ResourceWrite,
            SpeechProviderInstallAction,
            SecurityAuditInitiator.LocalUser,
            provider.Id);
        IsSpeechProviderOperationActive = true;
        SpeechProviderOperationProgress = 0;
        SpeechProviderOperationStatus = $"Starting the {provider.Name} download.";
        var progress = new DispatcherProgress<SpeechProviderInstallProgress>(
            uiDispatcher,
            UpdateSpeechProviderInstallProgress);
        try
        {
            await textToSpeech.InstallProviderAsync(
                provider.Id,
                progress);
            RefreshSpeechProviderCatalog(provider.Id);
            if (SelectedVoice is not null)
            {
                SaveSpeechProviderPreference(provider.Id);
                SaveVoicePreference(SelectedVoice.Id);
            }

            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
            ShowInformation(
                $"{provider.Name} is ready.",
                "The local neural speech provider is available now. No restart is required.");
            await SpeakCurrentResponseAsync();
        }
        catch (HttpRequestException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "download-failed");
            ApplicationLog.Error(logger, exception, "Downloading a speech provider");
            ShowFailure("The speech provider could not be downloaded.", exception.Message);
            await SpeakCurrentResponseAsync();
        }
        catch (InvalidDataException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "validation-failed");
            ApplicationLog.Error(logger, exception, "Validating a downloaded speech provider");
            ShowFailure("The speech provider download was invalid.", exception.Message);
            await SpeakCurrentResponseAsync();
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Installing a speech provider because access was denied");
            ShowFailure("The speech provider could not be installed.", exception.Message);
            await SpeakCurrentResponseAsync();
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Installing a speech provider due to an I/O error");
            ShowFailure("The speech provider could not be installed.", exception.Message);
            await SpeakCurrentResponseAsync();
        }
        catch (InvalidOperationException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "provider-unavailable");
            ApplicationLog.Error(logger, exception, "Preparing a speech provider");
            ShowFailure("The speech provider could not be prepared.", exception.Message);
            await SpeakCurrentResponseAsync();
        }
        finally
        {
            IsSpeechProviderOperationActive = false;
            UpdateSpeechProviderAvailability();
        }
    }

    private async Task RemoveSpeechProviderAsync()
    {
        var provider = SelectedSpeechProvider!;
        var audit = StartAudit(
            SecurityAuditCategory.ResourceWrite,
            SpeechProviderRemoveAction,
            SecurityAuditInitiator.LocalUser,
            provider.Id);
        IsSpeechProviderOperationActive = true;
        SpeechProviderOperationProgress = 0;
        SpeechProviderOperationStatus = $"Removing {provider.Name}.";
        try
        {
            await textToSpeech.RemoveProviderAsync(provider.Id);
            RefreshSpeechProviderCatalog(provider.Id);
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
            ShowInformation(
                $"{provider.Name} was removed.",
                "Its downloaded model was deleted. The built-in Windows provider remains available.");
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Removing a speech provider because access was denied");
            ShowFailure("The speech provider could not be removed.", exception.Message);
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Removing a speech provider due to an I/O error");
            ShowFailure("The speech provider could not be removed.", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "provider-busy");
            ApplicationLog.Error(logger, exception, "Removing an active speech provider");
            ShowFailure("The speech provider could not be removed.", exception.Message);
        }
        finally
        {
            IsSpeechProviderOperationActive = false;
            UpdateSpeechProviderAvailability();
        }
    }

    internal void UpdateSpeechProviderInstallProgress(
        SpeechProviderInstallProgress progress)
    {
        SpeechProviderOperationProgress = progress.Percentage;
        SpeechProviderOperationStatus = progress.Stage switch
        {
            SpeechProviderInstallStage.Downloading =>
                $"Downloading the local model · {progress.Percentage}%",
            SpeechProviderInstallStage.Verifying =>
                "Verifying the downloaded model.",
            SpeechProviderInstallStage.Extracting =>
                "Installing the downloaded voice assets.",
            SpeechProviderInstallStage.Preparing =>
                "Preparing the local neural speech engine.",
            _ => throw new ArgumentOutOfRangeException(nameof(progress)),
        };
    }

    private void RefreshSpeechProviderCatalog(string providerId)
    {
        var providers = textToSpeech.GetProviders();
        SpeechProviders.Clear();
        foreach (var provider in providers)
        {
            SpeechProviders.Add(provider);
        }

        suppressSpeechProviderPreferenceSave = true;
        suppressVoicePreferenceSave = true;
        try
        {
            SelectedSpeechProvider = SpeechProviders.FirstOrDefault(
                provider => string.Equals(
                    provider.Id,
                    providerId,
                    StringComparison.Ordinal));
            PopulateVoicesForProvider(
                textToSpeech.GetVoices(),
                preferredVoiceId: null,
                savedVoiceUnavailable: false);
        }
        finally
        {
            suppressVoicePreferenceSave = false;
            suppressSpeechProviderPreferenceSave = false;
        }
    }

    private void PopulateVoicesForProvider(
        IReadOnlyList<SpeechVoice> availableVoices,
        string? preferredVoiceId,
        bool savedVoiceUnavailable)
    {
        Voices.Clear();
        if (SelectedSpeechProvider is not null)
        {
            if (SelectedSpeechProvider.IsInstalled)
            {
                foreach (var voice in availableVoices)
                {
                    if (string.Equals(
                            voice.ProviderId,
                            SelectedSpeechProvider.Id,
                            StringComparison.Ordinal))
                    {
                        Voices.Add(voice);
                    }
                }
            }
        }

        var preferredVoice = Voices.FirstOrDefault(
            voice => string.Equals(
                voice.Id,
                preferredVoiceId,
                StringComparison.Ordinal));
        var defaultVoice = Voices.FirstOrDefault(
                voice => string.Equals(
                    voice.Id,
                    SelectedSpeechProvider?.DefaultVoiceId,
                    StringComparison.Ordinal))
            ?? SpeechVoiceSelector.SelectDefault(
                Voices,
                CultureInfo.CurrentUICulture);
        SelectedVoice = preferredVoice ?? defaultVoice;
        if (SelectedSpeechProvider?.IsInstalled == false)
        {
            RetainOrSelectActiveSpeechVoice(availableVoices);
        }

        UpdateVoiceAvailability(
            savedVoiceUnavailable
            && preferredVoiceId is not null
            && preferredVoice is null);
        UpdateSpeechProviderAvailability();
    }

    private static bool ShouldReportUnavailableSavedVoice(
        string? savedVoiceId,
        string? selectedSpeechProviderId,
        string? savedSpeechProviderId,
        SpeechProvider? preferredProvider)
    {
        if (savedVoiceId is null)
        {
            return false;
        }

        if (selectedSpeechProviderId is not null)
        {
            return false;
        }

        var effectiveSavedProviderId =
            savedSpeechProviderId ?? SpeechProviderIds.Windows;
        return string.Equals(
            preferredProvider?.Id,
            effectiveSavedProviderId,
            StringComparison.Ordinal);
    }

    private async Task PreviewVoiceAsync()
    {
        var previewText = $"Hello, I'm {AssistantName}.";
        IsBusy = true;
        IsSpeaking = true;
        activeSpokenText = previewText;
        try
        {
            await textToSpeech.SpeakAsync(
                previewText,
                SelectedVoice!,
                SelectedOutputDevice!);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ApplicationLog.Error(logger, exception, "Previewing the selected speech voice");
            SelectedVoice = null;
            ShowFailure("The selected speech voice is unavailable.", exception.Message);
        }
        catch (AudioOutputDeviceUnavailableException exception)
        {
            ApplicationLog.Error(logger, exception, "Previewing the selected audio output");
            HandleAudioOutputFailure(exception, "The selected audio output is unavailable.");
        }
        catch (InvalidOperationException exception)
        {
            ApplicationLog.Error(logger, exception, "Previewing text-to-speech");
            SelectedVoice = null;
            ShowFailure("Text-to-speech is unavailable.", exception.Message);
        }
        finally
        {
            activeSpokenText = null;
            IsSpeaking = false;
            IsBusy = false;
        }
    }

    private async Task StopSpeakingAsync()
    {
        await textToSpeech.StopAsync();
        activeSpokenText = null;
        IsSpeaking = false;
        ShowInformation("Speech is stopped.", "No speech playback is active.");
    }

    private async Task StopAudioAsync()
    {
        await textToSpeech.StopAsync();
        activeSpokenText = null;
        IsSpeaking = false;
        await StopListeningAsync();
    }

    private async Task ToggleCallVisualOverrideAsync()
    {
        await SetShowVisualTextDuringCallsAsync(!ShowVisualTextDuringCalls);
    }

    private async Task ToggleCallVoiceActivationAsync()
    {
        await SetAllowVoiceActivationDuringCallsAsync(!AllowVoiceActivationDuringCalls);
    }

    public async Task SetShowVisualTextDuringCallsAsync(bool value)
    {
        ShowVisualTextDuringCalls = value;
        await ApplyCallVisualOverrideAsync();
    }

    public async Task SetAllowVoiceActivationDuringCallsAsync(bool value)
    {
        AllowVoiceActivationDuringCalls = value;
        if (!AllowVoiceActivationDuringCalls && IsCallDetected && IsListening)
        {
            await StopListeningAsync();
        }
    }

    public async Task SetAssistantNameAsync(
        string value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            AssistantNameConfigurationAction,
            initiator,
            DeviceLocalPreferencesTarget);
        string normalizedName;
        try
        {
            normalizedName = AssistantNameRules.Normalize(value);
            commandCatalog.GetCommands(normalizedName);
        }
        catch (ArgumentException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Denied, "invalid-name");
            ShowFailure("The assistant name is invalid.", exception.Message);
            return;
        }

        if (string.Equals(normalizedName, AssistantName, StringComparison.Ordinal))
        {
            CompleteAudit(audit, SecurityAuditOutcome.Cancelled, "no-change");
            AssistantNameInput = AssistantName;
            return;
        }

        try
        {
            assistantNamePreferences.SaveName(normalizedName);
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Saving the assistant name preference because access was denied");
            ShowFailure("The assistant name could not be saved.", exception.Message);
            return;
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Saving the assistant name preference due to an I/O error");
            ShowFailure("The assistant name could not be saved.", exception.Message);
            return;
        }

        var restartListening = IsListening;
        if (restartListening)
        {
            await StopListeningAsync();
        }

        ApplyAssistantNameState(normalizedName);
        if (restartListening)
        {
            await StartListeningAsync();
        }
        else
        {
            ShowSuccess(
                $"{AssistantName} is ready.",
                $"The display name, command prefix, and spoken identity now use {AssistantName}.");
        }
    }

    private void SaveMicrophonePreference(MicrophoneDevice microphone)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            MicrophoneConfigurationAction,
            SecurityAuditInitiator.LocalUser,
            DeviceLocalPreferencesTarget);
        try
        {
            if (microphone.IsSystemDefault)
            {
                audioDevicePreferences.ClearMicrophoneId();
            }
            else
            {
                audioDevicePreferences.SaveMicrophoneId(microphone.Id);
            }

            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Saving the microphone preference because access was denied");
            ShowFailure("The microphone preference could not be saved.", exception.Message);
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Saving the microphone preference due to an I/O error");
            ShowFailure("The microphone preference could not be saved.", exception.Message);
        }
    }

    private void SaveOutputDevicePreference(AudioOutputDevice outputDevice)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            OutputDeviceConfigurationAction,
            SecurityAuditInitiator.LocalUser,
            DeviceLocalPreferencesTarget);
        try
        {
            if (outputDevice.IsSystemDefault)
            {
                audioDevicePreferences.ClearOutputDeviceId();
            }
            else
            {
                audioDevicePreferences.SaveOutputDeviceId(outputDevice.Id);
            }

            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Saving the audio output preference because access was denied");
            ShowFailure("The audio output preference could not be saved.", exception.Message);
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Saving the audio output preference due to an I/O error");
            ShowFailure("The audio output preference could not be saved.", exception.Message);
        }
    }

    private void SaveSpeechProviderPreference(string providerId)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            SpeechProviderSelectionConfigurationAction,
            SecurityAuditInitiator.LocalUser,
            DeviceLocalPreferencesTarget);
        try
        {
            textToSpeechPreferences.SaveProviderId(providerId);
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Saving the speech provider preference because access was denied");
            ShowFailure("The speech provider preference could not be saved.", exception.Message);
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Saving the speech provider preference due to an I/O error");
            ShowFailure("The speech provider preference could not be saved.", exception.Message);
        }
    }

    private void SaveVoicePreference(string voiceId)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            VoiceSelectionConfigurationAction,
            SecurityAuditInitiator.LocalUser,
            DeviceLocalPreferencesTarget);
        try
        {
            textToSpeechPreferences.SaveVoiceId(voiceId);
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Saving the speech voice preference because access was denied");
            ShowFailure("The speech voice preference could not be saved.", exception.Message);
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Saving the speech voice preference due to an I/O error");
            ShowFailure("The speech voice preference could not be saved.", exception.Message);
        }
    }

    private void SaveResponseOutputPreference(ResponseOutputMode value)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            ResponseOutputConfigurationAction,
            SecurityAuditInitiator.LocalUser,
            DeviceLocalPreferencesTarget);
        try
        {
            responseOutputPreferences.SaveDefaultMode(value);
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Saving the default response mode because access was denied");
            ShowFailure("The default response mode could not be saved.", exception.Message);
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Saving the default response mode due to an I/O error");
            ShowFailure("The default response mode could not be saved.", exception.Message);
        }
    }

    private bool SaveAppearancePreference(
        ApplicationThemeMode value,
        SecurityAuditInitiator initiator)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            AppearanceThemeConfigurationAction,
            initiator,
            DeviceLocalPreferencesTarget);
        try
        {
            appearancePreferences.SaveThemeMode(value);
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
            return true;
        }

        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Saving the appearance theme because access was denied");
            ShowFailure("The appearance theme could not be saved.", exception.Message);
            return false;
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Saving the appearance theme due to an I/O error");
            ShowFailure("The appearance theme could not be saved.", exception.Message);
            return false;
        }
    }

    private bool SavePresenceTimeoutPreference(
        int value,
        SecurityAuditInitiator initiator)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            PresenceTimeoutConfigurationAction,
            initiator,
            DeviceLocalPreferencesTarget);
        try
        {
            appearancePreferences.SavePresenceTimeoutSeconds(value);
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Saving the presence timeout because access was denied");
            ShowFailure("The presence timeout could not be saved.", exception.Message);
            return false;
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Saving the presence timeout due to an I/O error");
            ShowFailure("The presence timeout could not be saved.", exception.Message);
            return false;
        }
    }

    private bool SetResponseWindowSettings(
        ResponseWindowSettings value,
        SecurityAuditInitiator initiator)
    {
        if (responseWindowSettings == value)
        {
            return true;
        }

        if (!suppressResponseWindowPreferenceSave
            && !SaveResponseWindowPreference(value, initiator))
        {
            OnPropertyChanged(nameof(IsResponseAlwaysVisible));
            OnPropertyChanged(nameof(IsResponseWindowTopmost));
            OnPropertyChanged(nameof(ResponseWindowPosition));
            return false;
        }

        responseWindowSettings = value;
        OnPropertyChanged(nameof(IsResponseAlwaysVisible));
        OnPropertyChanged(nameof(IsResponseWindowTopmost));
        OnPropertyChanged(nameof(ResponseWindowPosition));
        return true;
    }

    private bool SaveResponseWindowPreference(
        ResponseWindowSettings value,
        SecurityAuditInitiator initiator)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            ResponseWindowConfigurationAction,
            initiator,
            DeviceLocalPreferencesTarget);
        try
        {
            appearancePreferences.SaveResponseWindowSettings(value);
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(
                logger,
                exception,
                "Saving the response window settings because access was denied");
            ShowFailure("The response window settings could not be saved.", exception.Message);
            return false;
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(
                logger,
                exception,
                "Saving the response window settings due to an I/O error");
            ShowFailure("The response window settings could not be saved.", exception.Message);
            return false;
        }
    }

    private bool SaveConstellationPreference(
        Action savePreference,
        string actionId,
        string settingName,
        SecurityAuditInitiator initiator)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            actionId,
            initiator,
            DeviceLocalPreferencesTarget);
        try
        {
            savePreference();
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(
                logger,
                exception,
                $"Saving the {settingName} because access was denied");
            ShowFailure($"The {settingName} could not be saved.", exception.Message);
            return false;
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(
                logger,
                exception,
                $"Saving the {settingName} due to an I/O error");
            ShowFailure($"The {settingName} could not be saved.", exception.Message);
            return false;
        }
    }

    private void SaveCallAwareSettings()
    {
        if (suppressCallAwarePreferenceSave)
        {
            return;
        }

        var audit = StartAudit(
            SecurityAuditCategory.ConfigurationWrite,
            CallAwareConfigurationAction,
            SecurityAuditInitiator.LocalUser,
            DeviceLocalPreferencesTarget);
        try
        {
            callAwarePreferences.Save(new CallAwareSettings(
                ShowVisualTextDuringCalls,
                AllowVoiceActivationDuringCalls));
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
            ApplicationLog.Information(logger, "Call-aware preferences were updated");
        }
        catch (UnauthorizedAccessException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Saving call-aware preferences because access was denied");
            ShowFailure("The call-aware settings could not be saved.", exception.Message);
        }
        catch (IOException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Saving call-aware preferences due to an I/O error");
            ShowFailure("The call-aware settings could not be saved.", exception.Message);
        }
    }

    private void UpdateVoiceAvailability(bool savedVoiceUnavailable)
    {
        VoiceAvailabilityMessage = SelectedVoice switch
        {
            not null when savedVoiceUnavailable =>
                $"The saved voice is unavailable. Using {SelectedVoice.Name} ({SelectedVoice.Culture}) for this profile culture.",
            not null => $"{SelectedVoice.Name} ({SelectedVoice.Culture}) is selected.",
            null when SelectedSpeechProvider?.IsInstalled == false =>
                $"Download {SelectedSpeechProvider.Name} before selecting one of its voices.",
            null when string.Equals(
                SelectedSpeechProvider?.Id,
                SpeechProviderIds.Windows,
                StringComparison.Ordinal) =>
                "No Windows speech pack is available. Install a text-to-speech voice in Windows Settings.",
            null when Voices.Count == 0 =>
                "The selected provider has no compatible speech voices.",
            _ =>
                "No voice matches the Windows profile culture. Choose another voice.",
        };
    }

    private void UpdateSpeechProviderAvailability()
    {
        if (IsSpeechProviderOperationActive)
        {
            SpeechProviderAvailabilityMessage = SpeechProviderOperationStatus;
            return;
        }

        SpeechProviderAvailabilityMessage = SelectedSpeechProvider switch
        {
            { IsBuiltIn: true } provider =>
                $"{provider.Description} It is built into Windows and is always available.",
            { IsInstalled: true } provider =>
                $"{provider.Description} Its model is installed locally and ready.",
            not null => $"{SelectedSpeechProvider.Description} Its model is not installed.",
            null => "No speech provider is available.",
        };
    }

    private void NotifySpeechProviderStateChanged()
    {
        DownloadSpeechProviderCommand.NotifyCanExecuteChanged();
        RemoveSpeechProviderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanDownloadSpeechProvider));
        OnPropertyChanged(nameof(CanRemoveSpeechProvider));
        OnPropertyChanged(nameof(SpeechProviderDownloadButtonText));
        UpdateSpeechProviderAvailability();
    }

    private void SetListeningPauseReason(string? value)
    {
        if (!string.Equals(listeningPauseReason, value, StringComparison.Ordinal))
        {
            listeningPauseReason = value;
            OnPropertyChanged(nameof(ListeningStatus));
        }
    }

    private void UpdateMicrophoneAvailability(bool selectedMicrophoneUnavailable = false)
    {
        MicrophoneAvailabilityMessage = SelectedMicrophone switch
        {
            { IsSystemDefault: true } when systemDefaultMicrophone is not null =>
                $"System is selected and follows the Windows default microphone for {AssistantName} capture.",
            { IsSystemDefault: true } =>
                "System is selected, but Windows has no active default microphone.",
            not null => $"{SelectedMicrophone.Name} is selected for {AssistantName} capture.",
            null when selectedMicrophoneUnavailable =>
                "The saved microphone is no longer available. Select another microphone.",
            _ => "No Windows microphone is available. Connect or enable a microphone, then refresh.",
        };
    }

    private void UpdateOutputDeviceAvailability(bool selectedDeviceUnavailable = false)
    {
        OutputDeviceAvailabilityMessage = SelectedOutputDevice switch
        {
            { IsSystemDefault: true } when systemDefaultOutputDevice is { IsMuted: true } =>
                "The Windows default audio output is muted or its volume is zero. Visual text is forced.",
            { IsSystemDefault: true } when systemDefaultOutputDevice is not null =>
                $"System is selected and follows the Windows default audio output for {AssistantName} playback.",
            { IsSystemDefault: true } =>
                "System is selected, but Windows has no active default audio output. Visual text is forced.",
            { IsMuted: true } =>
                $"{SelectedOutputDevice.Name} is muted or its Windows volume is zero. Visual text is forced.",
            not null => $"{SelectedOutputDevice.Name} is selected for {AssistantName} playback.",
            null when selectedDeviceUnavailable =>
                "The saved audio output device is no longer available. Select another device.",
            null when OutputDevices.Count == 0 =>
                "No Windows audio output device is available. Connect or enable speakers or headphones.",
            _ => "Select an audio output device to enable speech.",
        };
    }

    private async Task RunTypedCommandAsync() =>
        await HandleTranscriptAsync(
            CommandText,
            1,
            SecurityAuditInitiator.TypedCommand);

    private async void OnTranscriptRecognized(object? sender, VoiceTranscriptEventArgs eventArgs)
    {
        try
        {
            await uiDispatcher.InvokeAsync(
                () => HandleRecognizedVoiceTranscriptAsync(eventArgs));
        }
        catch (InvalidOperationException exception)
        {
            ShowFailure("The command could not be completed.", exception.Message);
        }
    }

    private void OnRecognitionFailed(object? sender, VoiceRecognitionFailureEventArgs eventArgs)
    {
        uiDispatcher.Post(() =>
        {
            if (IsSpeaking)
            {
                return;
            }

            ShowInformation("Command not recognized.", eventArgs.Message);
        });
    }

    private async Task HandleRecognizedVoiceTranscriptAsync(
        VoiceTranscriptEventArgs eventArgs)
    {
        if (IsSpeaking)
        {
            if (!commandRouter.IsActivationPrefixed(
                    eventArgs.Transcript,
                    AssistantName))
            {
                ApplicationLog.Debug(
                    logger,
                    "Ignoring unprefixed recognition while speech output is active");
                return;
            }

            if (activeSpokenText is not null
                && commandRouter.ContainsNormalizedPhrase(
                    activeSpokenText,
                    eventArgs.Transcript))
            {
                ApplicationLog.Debug(
                    logger,
                    "Ignoring a recognition result that matches active speech output");
                return;
            }

            await textToSpeech.StopAsync();
            activeSpokenText = null;
            IsSpeaking = false;
        }

        await HandleTranscriptAsync(
            eventArgs.Transcript,
            eventArgs.Confidence,
            SecurityAuditInitiator.VoiceCommand);
    }

    private async Task HandleTranscriptAsync(
        string spokenText,
        float confidence,
        SecurityAuditInitiator initiator)
    {
        Transcript = $"“{spokenText}” · {confidence:P0} confidence";
        State = AssistantState.Calculating;
        if (initiator == SecurityAuditInitiator.VoiceCommand)
        {
            WindowActionRequested?.Invoke(this, WindowAction.ShowPresence);
        }

        var match = commandRouter.Match(spokenText, AssistantName);
        if (!match.IsMatch || match.Command is null)
        {
            ShowInformation(
                "That isn't a supported built-in command.",
                $"Say “{AssistantName}, what can you do?” to see the deterministic local command catalogue.");
            await SpeakCurrentResponseAsync();
            return;
        }

        await ExecuteAsync(match.Command, initiator);
        if (ShouldSpeakResponse(match.Command.Action))
        {
            await SpeakCurrentResponseAsync();
        }
    }

    private async Task SpeakCurrentResponseAsync()
    {
        if (!IsSpeechResponseEnabled)
        {
            return;
        }

        var spokenText = $"{ResponseTitle}. {ResponseBody}";
        IsSpeaking = true;
        activeSpokenText = spokenText;
        ApplicationLog.Debug(logger, "Starting spoken response output");
        try
        {
            await textToSpeech.SpeakAsync(
                spokenText,
                activeSpeechVoice!,
                SelectedOutputDevice!);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ApplicationLog.Error(logger, exception, "Playing a response with the selected speech voice");
            ClearActiveSpeechVoice();
            ShowFailure("The selected speech voice is unavailable.", exception.Message);
        }
        catch (AudioOutputDeviceUnavailableException exception)
        {
            ApplicationLog.Error(logger, exception, "Playing a response through audio output");
            HandleAudioOutputFailure(exception, "The selected audio output is unavailable.");
        }
        catch (InvalidOperationException exception)
        {
            ApplicationLog.Error(logger, exception, "Playing a response with text-to-speech");
            ClearActiveSpeechVoice();
            ShowFailure("Text-to-speech is unavailable.", exception.Message);
        }
        finally
        {
            activeSpokenText = null;
            IsSpeaking = false;
        }
    }

    private void RetainOrSelectActiveSpeechVoice(
        IReadOnlyList<SpeechVoice> availableVoices)
    {
        var installedProviderIds = SpeechProviders
            .Where(provider => provider.IsInstalled)
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);
        var retainedVoice = activeSpeechVoice is null
            ? null
            : availableVoices.FirstOrDefault(
                voice => string.Equals(
                             voice.Id,
                             activeSpeechVoice.Id,
                             StringComparison.Ordinal)
                         && string.Equals(
                             voice.ProviderId,
                             activeSpeechVoice.ProviderId,
                             StringComparison.Ordinal)
                         && installedProviderIds.Contains(voice.ProviderId));
        if (retainedVoice is not null)
        {
            SetActiveSpeechVoice(retainedVoice);
            return;
        }

        var fallbackProvider = SpeechProviders.FirstOrDefault(
                provider => provider.IsInstalled
                            && string.Equals(
                                provider.Id,
                                SpeechProviderIds.Windows,
                                StringComparison.Ordinal))
            ?? SpeechProviders.FirstOrDefault(provider => provider.IsInstalled);
        var fallbackVoices = availableVoices
            .Where(voice => string.Equals(
                voice.ProviderId,
                fallbackProvider?.Id,
                StringComparison.Ordinal))
            .ToArray();
        var fallbackVoice = fallbackVoices.FirstOrDefault(
                voice => string.Equals(
                    voice.Id,
                    fallbackProvider?.DefaultVoiceId,
                    StringComparison.Ordinal))
            ?? SpeechVoiceSelector.SelectDefault(
                fallbackVoices,
                CultureInfo.CurrentUICulture);
        SetActiveSpeechVoice(fallbackVoice);
    }

    private void SetActiveSpeechVoice(SpeechVoice? voice)
    {
        activeSpeechVoice = voice;
        OnPropertyChanged(nameof(IsSpeechOutputAvailable));
        NotifyOutputPolicyChanged();
    }

    private void ClearActiveSpeechVoice()
    {
        SelectedVoice = null;
        SetActiveSpeechVoice(null);
    }

    private static bool ShouldSpeakResponse(BuiltInAction action) =>
        action is not (
            BuiltInAction.HideApplication
            or BuiltInAction.ExitApplication
            or BuiltInAction.RestartApplication
            or BuiltInAction.StopSpeaking
            or BuiltInAction.LockMachine);

    private void HandleAudioOutputFailure(
        AudioOutputDeviceUnavailableException exception,
        string unavailableTitle)
    {
        if (SelectedOutputDevice?.IsSystemDefault == true)
        {
            systemDefaultOutputDevice = exception.Reason == AudioOutputFailureReason.Muted
                ? systemDefaultOutputDevice! with { IsMuted = true }
                : null;
            PreviewVoiceCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsSpeechOutputAvailable));
            NotifyOutputPolicyChanged();
            UpdateOutputDeviceAvailability();
            ShowFailure(
                exception.Reason == AudioOutputFailureReason.Muted
                    ? "Audio output is muted."
                    : unavailableTitle,
                exception.Message);
            return;
        }

        if (exception.Reason == AudioOutputFailureReason.Muted
            && SelectedOutputDevice is not null)
        {
            SelectedOutputDevice = SelectedOutputDevice with { IsMuted = true };
            ShowFailure("Audio output is muted.", exception.Message);
            return;
        }

        SelectedOutputDevice = null;
        ShowFailure(unavailableTitle, exception.Message);
    }

    internal async Task ExecuteAsync(
        CommandDefinition command,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.System)
    {
        ApplicationLog.BuiltInActionExecuting(logger, command.Action);
        switch (command.Action)
        {
            case BuiltInAction.ShowApplication:
                ShowApplication();
                ShowSuccess(
                    $"{AssistantName} is visible.",
                    "The existing application instance was shown.",
                    requestWindow: false);
                break;
            case BuiltInAction.HideApplication:
                HideApplication();
                break;
            case BuiltInAction.ExitApplication:
                await ExitAsync();
                break;
            case BuiltInAction.RestartApplication:
                await RestartApplicationAsync(initiator);
                break;
            case BuiltInAction.OpenSettings:
                ShowInformation(
                    "Settings",
                    "Speech, audio, response, call, and readiness settings are available in the settings window.",
                    requestWindow: false);
                ShowSettings();
                break;
            case BuiltInAction.OpenDocumentation:
                ShowInformation(
                    "Documentation",
                    $"The {AssistantName} user guide is open in the documentation window.",
                    requestWindow: false);
                ShowDocumentation();
                break;
            case BuiltInAction.OpenSetup:
                await DetectMicrophonesAsync();
                break;
            case BuiltInAction.ShowHelp:
                ShowInformation(
                    "Built-in commands are ready.",
                    $"{Commands.Count} deterministic commands are registered. Say “{AssistantName}, open documentation” and choose Commands for the full list, or try lock, lifecycle, status, and protected power proposals.");
                break;
            case BuiltInAction.ShowVersion:
                ShowInformation(
                    $"{AssistantName} version",
                    $"{applicationInfo.Version} · local Windows speech · no model configured");
                break;
            case BuiltInAction.ShowStatus:
                ShowInformation(
                    IsListening ? "Waiting for your command." : "Voice is not active.",
                    IsListening ? ListeningStatus : "Enable listening or use the typed proof field.");
                break;
            case BuiltInAction.CancelTask:
                CancelPendingPowerAudit("task-cancelled");
                ShowInformation("Cancelled.", $"No pending {AssistantName} task or power proposal will continue.");
                break;
            case BuiltInAction.StopSpeaking:
                await StopSpeakingAsync();
                break;
            case BuiltInAction.LockMachine:
                await LockCurrentSessionAsync(initiator);
                break;
            case BuiltInAction.ProposeShutdown:
            case BuiltInAction.ProposeRestart:
                CreatePowerProposal(command.Action, initiator);
                PresentResponse(
                    AssistantState.Waiting,
                    command.Action == BuiltInAction.ProposeShutdown
                        ? "Shutdown request recognized."
                        : "Restart request recognized.",
                    "This bootstrap proves voice routing without executing disruptive power actions. No OS power request was sent.");
                break;
            case BuiltInAction.CancelPowerAction:
                var hadPendingAction = CancelPendingPowerAudit("user-cancelled");
                ShowInformation(
                    hadPendingAction ? "Power proposal cancelled." : $"No {AssistantName} power action is pending.",
                    "No operating-system action was sent.");
                break;
            case BuiltInAction.ShowPowerStatus:
                ShowInformation(
                    pendingPowerAction switch
                    {
                        BuiltInAction.ProposeShutdown => "Shutdown proposal pending.",
                        BuiltInAction.ProposeRestart => "Computer restart proposal pending.",
                        _ => "No power action is pending.",
                    },
                    "Power execution is intentionally disabled in this bootstrap proof.");
                break;
            default:
                throw new InvalidOperationException($"Unsupported built-in action: {command.Action}.");
        }
    }

    private async Task RestartApplicationAsync(SecurityAuditInitiator initiator)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ApplicationExecution,
            ApplicationRestartAction,
            initiator,
            CurrentApplicationTarget);
        try
        {
            await StopAudioAsync();
            applicationProcessController.RestartCurrentApplication();
            CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
            WindowActionRequested?.Invoke(this, WindowAction.Close);
        }
        catch (InvalidOperationException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "restart-failed");
            ApplicationLog.Error(logger, exception, "Restarting Kora");
            ShowFailure($"{AssistantName} could not restart.", exception.Message);
        }
    }

    private Task OpenMicrophonePrivacySettingsAsync()
    {
        try
        {
            applicationProcessController.OpenWindowsMicrophonePrivacySettings();
        }
        catch (InvalidOperationException exception)
        {
            ApplicationLog.Error(logger, exception, "Opening Windows microphone privacy settings");
            ShowFailure("Windows microphone settings could not be opened.", exception.Message);
        }

        return Task.CompletedTask;
    }

    private async Task LockCurrentSessionAsync(SecurityAuditInitiator initiator)
    {
        var audit = StartAudit(
            SecurityAuditCategory.ProtectedOperation,
            SessionLockAction,
            initiator,
            CurrentWindowsSessionTarget);
        try
        {
            await StopAudioAsync();
            if (sessionController.LockCurrentSession())
            {
                CompleteAudit(audit, SecurityAuditOutcome.Succeeded);
                return;
            }

            CompleteAudit(audit, SecurityAuditOutcome.Failed, "os-request-rejected");
            ShowFailure("Windows did not accept the lock request.", "The microphone remains disabled.");
        }
        catch (InvalidOperationException exception)
        {
            CompleteAudit(audit, SecurityAuditOutcome.Failed, "lock-failed");
            ApplicationLog.Error(logger, exception, "Locking the current Windows session");
            ShowFailure("Windows could not lock the current session.", exception.Message);
        }
    }

    private void CreatePowerProposal(
        BuiltInAction action,
        SecurityAuditInitiator initiator)
    {
        CancelPendingPowerAudit("superseded");
        pendingPowerAction = action;
        pendingPowerAudit = StartAudit(
            SecurityAuditCategory.SecurityApproval,
            action == BuiltInAction.ProposeShutdown
                ? ShutdownApprovalAction
                : RestartApprovalAction,
            initiator,
            CurrentMachineTarget,
            Guid.NewGuid());
    }

    private bool CancelPendingPowerAudit(string reasonCode)
    {
        if (pendingPowerAudit is null)
        {
            return false;
        }

        CompleteAudit(pendingPowerAudit, SecurityAuditOutcome.Cancelled, reasonCode);
        pendingPowerAudit = null;
        pendingPowerAction = null;
        return true;
    }

    private SecurityAuditEvent StartAudit(
        SecurityAuditCategory category,
        string actionId,
        SecurityAuditInitiator initiator,
        string targetId,
        Guid? approvalId = null)
    {
        var audit = new SecurityAuditEvent(
            Guid.NewGuid(),
            category,
            actionId,
            SecurityAuditOutcome.Requested,
            initiator,
            targetId,
            approvalId);
        securityAuditLog.Write(audit);
        return audit;
    }

    private void CompleteAudit(
        SecurityAuditEvent audit,
        SecurityAuditOutcome outcome,
        string? reasonCode = null) =>
        securityAuditLog.Write(audit.WithOutcome(outcome, reasonCode));

    private void ShowInformation(string title, string body, bool requestWindow = true)
    {
        PresentResponse(AssistantState.Information, title, body, requestWindow);
    }

    private void ShowSuccess(string title, string body, bool requestWindow = true)
    {
        PresentResponse(AssistantState.Success, title, body, requestWindow);
    }

    private void ShowFailure(string title, string body)
    {
        PresentResponse(AssistantState.Failure, title, body);
    }

    private void PresentResponse(
        AssistantState responseState,
        string title,
        string body,
        bool requestWindow = true)
    {
        State = responseState;
        ResponseTitle = title;
        ResponseBody = body;
        forceVisualResponse = responseState == AssistantState.Failure || !IsSpeechOutputAvailable;
        NotifyOutputPolicyChanged();
        if (!isInitializing
            && requestWindow
            && responseState != AssistantState.Listening
            && IsVisualResponseVisible)
        {
            WindowActionRequested?.Invoke(this, WindowAction.Show);
        }
    }

    private void NotifyOutputPolicyChanged()
    {
        OnPropertyChanged(nameof(EffectiveResponseMode));
        OnPropertyChanged(nameof(IsVisualResponseVisible));
        OnPropertyChanged(nameof(IsSpeechResponseEnabled));
        OnPropertyChanged(nameof(ResponseOutputStatus));
    }

    private string[] GetRecognitionPhrases()
    {
        var commands = commandCatalog.GetCommands(AssistantName)
            .SelectMany(command => command.AllPhrases);
        return commands
            .SelectMany(phrase => new[] { phrase, $"{AssistantName} {phrase}" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool CanApplyAssistantName() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(AssistantNameInput)
        && !string.Equals(
            AssistantNameInput.Trim(),
            AssistantName,
            StringComparison.Ordinal);

    private void ApplyAssistantNameState(string value)
    {
        var normalizedName = AssistantNameRules.Normalize(value);
        commandCatalog.GetCommands(normalizedName);
        var previousDefaultCommand = $"{AssistantName}, what can you do?";
        var replaceDefaultCommand = string.Equals(
            CommandText,
            previousDefaultCommand,
            StringComparison.Ordinal);
        if (!AssistantName.Equals(normalizedName, StringComparison.Ordinal))
        {
            AssistantName = normalizedName;
            OnPropertyChanged(nameof(AssistantInitial));
            OnPropertyChanged(nameof(SettingsWindowTitle));
            OnPropertyChanged(nameof(SettingsSubtitle));
            OnPropertyChanged(nameof(AppearanceSettingsDescription));
            OnPropertyChanged(nameof(AppearanceThemeDescription));
            OnPropertyChanged(nameof(MainCaptureDescription));
            OnPropertyChanged(nameof(SpeechAudioSettingsDescription));
            OnPropertyChanged(nameof(ResponseSettingsDescription));
            OnPropertyChanged(nameof(ReadinessSettingsDescription));
            OnPropertyChanged(nameof(CallVoiceActivationStatus));
            OnPropertyChanged(nameof(Commands));
            UpdateMicrophoneAvailability();
            UpdateOutputDeviceAvailability();
        }

        AssistantNameInput = normalizedName;
        UpdateAssistantNameSettingStatus();
        if (replaceDefaultCommand)
        {
            CommandText = $"{AssistantName}, what can you do?";
        }

        ApplyAssistantNameCommand.NotifyCanExecuteChanged();
    }

    private void UpdateAssistantNameSettingStatus()
    {
        try
        {
            var normalizedName = AssistantNameRules.Normalize(AssistantNameInput);
            AssistantNameSettingStatus = string.Equals(
                normalizedName,
                AssistantName,
                StringComparison.Ordinal)
                ? $"The current name is {AssistantName}."
                : $"Apply to use {normalizedName} in the interface, command prefix, and speech.";
        }
        catch (ArgumentException exception)
        {
            AssistantNameSettingStatus = exception.Message;
        }
    }

    private static void ValidateResponseModeOverride(ResponseOutputMode? mode, string parameterName)
    {
        if (mode is not null && !Enum.IsDefined(mode.Value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                mode,
                "The response output mode is invalid.");
        }
    }

    private void ValidateResponseModeOverrideOption(
        ResponseModeOverrideOption? option,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(option, parameterName);
        if (!ResponseModeOverrideOptions.Contains(option))
        {
            throw new ArgumentException(
                "The response mode override option is unavailable.",
                parameterName);
        }
    }

    private sealed class DispatcherProgress<T>(
        IUiDispatcher dispatcher,
        Action<T> report) : IProgress<T>
    {
        public void Report(T value) =>
            dispatcher.Post(() => report(value));
    }
}