using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

using Kora.Application.Dependencies;
using Kora.Application.Auditing;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Application.Hosting;
using Kora.Application.Network;
using Kora.Application.Presentation;
using Kora.Application.Voice;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Presentation;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private const string ApplicationRestartAction = "application.restart";
    private const string PresencePositionConfigurationAction = "configuration.presence-position";
    private const string ResponseWindowConfigurationAction = "configuration.response-window";
    private const string CallAwareConfigurationAction = "configuration.call-aware-policy";
    private const string CurrentApplicationTarget = "application.current";
    private const string CurrentMachineTarget = "machine.current";
    private const string DeviceLocalPreferencesTarget = "preferences.device-local";
    private const string MicrophoneConfigurationAction = InputDevicePreferenceService.AuditAction;
    private const string ResponseOutputConfigurationAction = "configuration.response-output";
    private const string MutedOutputFallbackConfigurationAction = "configuration.muted-output-visual-fallback";
    private const string SpeechProviderInstallAction = "speech-provider.install";
    private const string LocalModelInstallAction = "local-model.install";
    private const string PowerShellInstallAction = "powershell.install";


    private const string ModelExecutionPreferenceAction = "configuration.model-execution";
    private const string SpeechProviderRemoveAction = "speech-provider.remove";
    private const string SpeechProviderSelectionConfigurationAction = "configuration.speech-provider";
    private const string ShutdownApprovalAction = "power.shutdown";
    private const string RestartApprovalAction = "power.restart";

    private readonly BuiltInCommandCatalog commandCatalog;
    private readonly BuiltInCommandRouter commandRouter;
    private readonly ArtifactCommandRouter artifactCommandRouter;
    private readonly ArtifactCatalogue artifactCatalogue;
    private readonly DependencyBootstrapper dependencyBootstrapper;
    private readonly DependencySetupWorkflow dependencySetup;


    private readonly IModelExecutionPreferences modelExecutionPreferences;
    private readonly IMicrophoneAccessService microphoneAccessService;
    private readonly IActivatedVoiceRecognitionService voiceRecognition;
    private readonly ITextToSpeechService textToSpeech;
    private readonly IAppearancePreferences appearancePreferences;
    private readonly IOptionalSpeechOfferPreferences optionalSpeechOfferPreferences;
    private readonly IAudioDevicePreferences audioDevicePreferences;
    private readonly IResponseOutputPreferences responseOutputPreferences;
    private readonly ICallAwarePreferences callAwarePreferences;
    private readonly ISessionController sessionController;
    private readonly IApplicationProcessController applicationProcessController;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ICurrentUserNameProvider currentUserNameProvider;
    private readonly IApplicationInfo applicationInfo;
    private readonly ISecurityAuditLog securityAuditLog;
    private readonly AuditedPreferenceWriter preferenceWriter;
    private readonly BoundedAudioOutputCatalog outputCatalog;
    private int pendingAuditWrites;
    private bool auditEvidenceUnavailable;
    private readonly Lock auditPersistenceGate = new();
    private Task auditPersistence = Task.CompletedTask;
    private Task AuditPersistence
    {
        get
        {
            lock (auditPersistenceGate)
            {
#pragma warning disable VSTHRD003 // Snapshot of owned thread-pool writes; no dispatcher continuation is required for their completion.
                return auditPersistence;
#pragma warning restore VSTHRD003
            }
        }
    }
    private long approvalPreparationRevision;

    private int activeApprovalPreparations;
    public Task ApprovalPreparation { get; private set; } = Task.CompletedTask;
    private readonly ILogger<MainViewModel> logger;
    private readonly IVoiceConsentPreferences voiceConsentPreferences;
    private readonly IWindowsPrivacyObservationService privacyObservation;
    private readonly DurableVersionQuery durableVersionQuery;
    public string LocalStorageDisclosure => DurableVersionQuery.StorageDisclosure;
    private bool? voiceConsent;
    private int voiceEnabled;
    private long voiceRecoveryRevision;
    private long microphoneTopologyRevision;
    private bool lifecycleAdmissionClosed;
    private bool hostExitRequested;
    private int handoffPreparationActive;
    private int privacyPresentationHeld;
    private bool voiceStartupApplied;
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
    private bool isLocalModelSetupActive;
    private bool isPowerShellSetupActive;
    private CancellationTokenSource? localModelCancellation;
    private CancellationTokenSource? powerShellSetupCancellation;






    private bool localModelsEnabled = ModelExecutionSettings.Default.LocalModelsEnabled;
    private bool hostedModelsEnabled = ModelExecutionSettings.Default.HostedModelsEnabled;
    private int stoppingAudioOperations;


















    private string localModelSetupStatus = "Checking local model readiness.";
    private int localModelSetupProgress;
    private bool isLocalModelSetupProgressIndeterminate;
    private string powerShellSetupStatus = "Checking PowerShell 7 readiness.";
    private string? savedSpeechProviderIdForOffer;
    private bool suppressVoicePreferenceSave;
    private bool suppressSpeechProviderPreferenceSave;
    private bool suppressAudioDevicePreferenceSave;
    private bool suppressPresenceAppearancePreferenceSave;
    private bool suppressResponseWindowPreferenceSave;
    private bool suppressResponseModeSave;
    private bool isInitializing;
    private bool forceVisualResponse;
    private string microphoneAvailabilityMessage = "Checking Windows microphone input devices.";
    private string speechProviderAvailabilityMessage = "Checking speech providers.";
    private string speechProviderOperationStatus = string.Empty;
    private int speechProviderOperationProgress;
    private string voiceAvailabilityMessage = "Checking installed Windows speech voices.";
    private string outputDeviceAvailabilityMessage = "Checking Windows audio output devices.";
    private ApplicationThemeMode themeMode = ApplicationThemeMode.System;
    private bool isPresenceDisplayEnabled = PresenceSettings.DefaultDisplayEnabled;
    private int presenceTimeoutSeconds = PresenceSettings.DefaultTimeoutSeconds;
    private int responseTimeoutSeconds = ResponseWindowSettings.DefaultTimeoutSeconds;
    private int presenceSizePixels = PresenceSettings.DefaultSizePixels;
    private int presenceDotSizePercent = PresenceSettings.DefaultDotSizePercent;
    private int presenceDotDensityPercent = PresenceSettings.DefaultDotDensityPercent;
    private int presenceMovementSpeedPercent = PresenceSettings.DefaultMovementSpeedPercent;
    private bool isPresenceSpeechScalingEnabled = PresenceSettings.DefaultSpeechScalingEnabled;
    private int presenceSpeechScaleAmountPercent = PresenceSettings.DefaultSpeechScaleAmountPercent;
    private PresencePosition? presencePosition;
    private ResponseWindowSettings responseWindowSettings = ResponseWindowSettings.Default;
    private ResponseOutputMode defaultResponseMode = ResponseOutputMode.Hybrid;
    private bool fallbackToVisualWhenOutputMuted = true;
    private ResponseOutputMode? queueResponseMode;
    private ResponseOutputMode? taskResponseMode;
    private CallState currentCallState;
    private bool showVisualTextDuringCalls = true;
    private bool allowVoiceActivationDuringCalls = true;
    private MicrophoneAccessStatus microphoneAccessStatus = new(
        MicrophoneAccessState.Unknown,
        "Windows microphone access has not been checked.");
    private string? listeningPauseReason;
    private WindowsSessionState? listeningPauseSessionState;
    private string? activeSpokenText;
    private SpeechVoice? activeSpeechVoice;
    private BuiltInAction? pendingPowerAction;
    private SecurityAuditEvent? pendingPowerAudit;

    public MainViewModel(
        BuiltInCommandCatalog commandCatalog,
        BuiltInCommandRouter commandRouter,
        ArtifactCatalogue artifactCatalogue,
        ArtifactCommandRouter artifactCommandRouter,
        DependencyBootstrapper dependencyBootstrapper,
        DependencySetupWorkflow dependencySetup,


        IModelExecutionPreferences modelExecutionPreferences,
        IMicrophoneAccessService microphoneAccessService,
        IActivatedVoiceRecognitionService voiceRecognition,
        ITextToSpeechService textToSpeech,
        AssistantNameConfigurationService assistantNameConfiguration,
        IAppearancePreferences appearancePreferences,
        IOptionalSpeechOfferPreferences optionalSpeechOfferPreferences,
        IAudioDevicePreferences audioDevicePreferences,
        IResponseOutputPreferences responseOutputPreferences,
        ICallAwarePreferences callAwarePreferences,
        ICallStateService callStateService,
        ISessionController sessionController,
        IApplicationProcessController applicationProcessController,
        IUiDispatcher uiDispatcher,
        ICurrentUserNameProvider currentUserNameProvider,
        IApplicationInfo applicationInfo,
        ISecurityAuditLog securityAuditLog,
        ILogger<MainViewModel> logger,
        IVoiceConsentPreferences voiceConsentPreferences,
        IWindowsPrivacyObservationService privacyObservation,
        DurableVersionQuery durableVersionQuery,
        Kora.Application.Tools.ReadOnlyCapabilityRegistry capabilityRegistry,
        AppearanceConfigurationService appearanceConfiguration,
        SpeechConfigurationService speechConfiguration,
        Kora.Tools.Clipboard.ClipboardSnapshotBroker clipboardPreview,
        Kora.Tools.Clipboard.ClipboardRead clipboardRead,
        Kora.Tools.Clipboard.ClipboardReuse clipboardReuse,
        Kora.Tools.Clipboard.ClipboardRevoke clipboardRevoke,
        Kora.Tools.Clipboard.ClipboardExplain clipboardExplain,
        AuditedPreferenceWriter preferenceWriter,
        BoundedAudioOutputCatalog outputCatalog,
        AudioPlaybackSettings audioPlaybackSettings,
        BoundedMicrophoneCatalog? microphoneCatalog = null,
        OutputDeviceConfigurationService? outputConfiguration = null,
        ResponseModeConfigurationService? responseModeConfiguration = null,
        DiagnosticRetentionConfigurationService? diagnosticRetentionConfiguration = null,
        ManualCallControl? manualCallControl = null,
        AuditRetentionConfigurationService? auditRetentionConfiguration = null,
        InCallFeedbackConfigurationService? inCallFeedbackConfiguration = null,
        SpeechTextConfigurationService? speechTextConfiguration = null,
        SessionRetentionConfigurationService? sessionRetentionConfiguration = null,
        ProviderModeConfigurationService? providerModeConfiguration = null,
        Kora.Tools.Network.PreapprovedUriList? preapprovedUriList = null,
        Kora.Tools.Network.PreapprovedUriAdd? preapprovedUriAdd = null,
        Kora.Tools.Network.PreapprovedUriRemove? preapprovedUriRemove = null,
        Kora.Tools.Network.PreapprovedUriClear? preapprovedUriClear = null,
        Kora.Tools.Network.WebPageGet? webPageGet = null,
        ArtifactDirectoryConfigurationService? artifactDirectoryConfiguration = null,
        McpServerConfigurationService? mcpServerConfiguration = null,
        ProviderProfileConfigurationService? providerProfileConfiguration = null,
        AuditImpactPreviewService? auditImpactPreview = null,
        SessionRetentionPlannerService? sessionRetentionPlanner = null,
        Kora.Application.Voice.SpeechPreparationCoordinator? speechPreparation = null,
        DiagnosticImpactPreviewService? diagnosticImpactPreview = null,
        Kora.Application.Context.ClipboardExplanationWorkflow? clipboardExplanation = null,
        SpeechInputConfigurationService? speechInputConfiguration = null,
        SpeechComponentSetupService? speechComponentSetup = null,
        ConversationalReplyConfigurationService? conversationalReplies = null,
        CommunicationActivityConfigurationService? communicationActivityConfiguration = null,
        OrdinaryTurnDeadlineConfigurationService? ordinaryDeadlineConfiguration = null,
        SpeechSetupComparison? speechSetupComparison = null,
        ConversationExecutionConfigurationService? conversationExecution = null,
        LogonStartupConfigurationService? logonStartupConfiguration = null,
        StartupPresentationConfigurationService? startupPresentationConfiguration = null,
        ForegroundPttConfigurationService? foregroundPtt = null,
        VoiceCaptureTimingConfigurationService? voiceCaptureTimingConfiguration = null,
        DetailPresentationConfigurationService? detailPresentationConfiguration = null,
        QuietHoursConfigurationService? quietHoursConfiguration = null,
        MarkdownDisplayConfigurationService? markdownDisplayConfiguration = null)
    {
        this.commandCatalog = commandCatalog;
        this.commandRouter = commandRouter;
        this.artifactCatalogue = artifactCatalogue;
        artifactCatalogue.Changed += OnArtifactCatalogueChanged;
        artifactCatalogue.ReloadFailed += OnArtifactCatalogueReloadFailed;
        this.artifactCommandRouter = artifactCommandRouter;
        this.dependencyBootstrapper = dependencyBootstrapper;
        this.dependencySetup = dependencySetup;
        this.speechComponentSetup = speechComponentSetup;


        this.modelExecutionPreferences = modelExecutionPreferences;
        this.microphoneAccessService = microphoneAccessService;
        this.voiceRecognition = voiceRecognition;
        this.textToSpeech = textToSpeech;
        this.speechPreparation = speechPreparation;
        if (speechPreparation is not null) { speechPreparation.Changed += OnSpeechPreparationChanged; }
        this.assistantNameConfiguration = assistantNameConfiguration;
        assistantNameConfiguration.Changed += OnAssistantNameConfigurationChanged;
        this.appearancePreferences = appearancePreferences;
        this.optionalSpeechOfferPreferences = optionalSpeechOfferPreferences;
        this.audioDevicePreferences = audioDevicePreferences;
        inputDevicePreferences = new(audioDevicePreferences, securityAuditLog);
        this.responseOutputPreferences = responseOutputPreferences;
        this.callAwarePreferences = callAwarePreferences;
        communicationPolicy = new CallCommunicationPolicy(callStateService);
        communicationActivity = callStateService as CommunicationActivityCallStateService;
        this.communicationActivityConfiguration = communicationActivityConfiguration;
        if (communicationActivity is not null)
        {
            communicationActivity.Changed += OnCommunicationActivityChanged;
        }
        this.manualCallControl = manualCallControl;
        currentCallState = callStateService.CurrentState;
        this.sessionController = sessionController;
        this.applicationProcessController = applicationProcessController;
        this.uiDispatcher = uiDispatcher;
        this.currentUserNameProvider = currentUserNameProvider;
        this.applicationInfo = applicationInfo;
        this.securityAuditLog = securityAuditLog;
        this.logger = logger;
        this.voiceConsentPreferences = voiceConsentPreferences;
        this.privacyObservation = privacyObservation;
        this.durableVersionQuery = durableVersionQuery;
        this.capabilityRegistry = capabilityRegistry;
        this.appearanceConfiguration = appearanceConfiguration;
        appearanceConfiguration.Changed += OnAppearanceChanged;
        this.speechConfiguration = speechConfiguration;
        this.audioPlaybackSettings = audioPlaybackSettings;
        audioPlaybackSettings.OutputInvalidated += OnAudioPlaybackOutputInvalidated;
        audioPlaybackSettings.Changed += OnAudioPlaybackSettingsChanged;
        speechConfiguration.Changed += OnSpeechConfigurationChanged;
        this.outputConfiguration = outputConfiguration;
        this.responseModeConfiguration = responseModeConfiguration;
        settingsUndo = new(appearanceConfiguration, speechConfiguration, audioPlaybackSettings.VolumeConfiguration,
            responseModeConfiguration, communicationPolicy, TimeProvider.System);
        settingsUndo.Changed += OnSettingsUndoChanged;
        this.providerModeConfiguration = providerModeConfiguration;
        this.providerProfileConfiguration = providerProfileConfiguration;
        this.artifactDirectoryConfiguration = artifactDirectoryConfiguration;
        this.mcpServerConfiguration = mcpServerConfiguration;
        if (mcpServerConfiguration is not null)
        {
            mcpServerConfiguration.Changed += OnMcpServerConfigurationChanged;
        }
        this.preapprovedUriList = preapprovedUriList;
        this.preapprovedUriAdd = preapprovedUriAdd;
        this.preapprovedUriRemove = preapprovedUriRemove;
        this.preapprovedUriClear = preapprovedUriClear;
        this.webPageGet = webPageGet;
        this.inCallFeedbackConfiguration = inCallFeedbackConfiguration;
        if (inCallFeedbackConfiguration is not null)
        {
            try { inCallFeedbackConfiguration.Observe(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                ApplicationLog.Error(logger, exception, "Reading in-call feedback");
            }
            inCallFeedbackConfiguration.Changed += OnInCallFeedbackChanged;
        }
        this.speechTextConfiguration = speechTextConfiguration;
        if (speechTextConfiguration is not null)
        {
            try { speechTextConfiguration.Observe(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                speechTextConfiguration.HoldUnavailable();
                ApplicationLog.Error(logger, exception, "Reading speech-text presentation preference");
            }
            speechTextConfiguration.Changed += OnSpeechTextConfigurationChanged;
        }
        this.diagnosticRetentionConfiguration = diagnosticRetentionConfiguration;
        this.sessionRetentionConfiguration = sessionRetentionConfiguration;
        this.ordinaryDeadlineConfiguration = ordinaryDeadlineConfiguration;
        this.sessionRetentionPlanner = sessionRetentionPlanner;
        if (sessionRetentionPlanner is not null) { sessionRetentionPlanner.Changed += OnSessionRetentionPlannerChanged; }
        this.auditRetentionConfiguration = auditRetentionConfiguration;
        this.auditImpactPreview = auditImpactPreview;
        this.diagnosticImpactPreview = diagnosticImpactPreview;
        if (auditRetentionConfiguration is not null)
        {
            try { auditRetentionConfiguration.Observe(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                auditRetentionConfiguration.HoldUnavailable();
                ApplicationLog.Error(logger, exception, "Reading future-only audit retention");
            }
            selectedAuditRetentionDays = auditRetentionConfiguration.Get().Desired.Days;
            auditRetentionConfiguration.Changed += OnAuditRetentionChanged;
        }
        if (diagnosticRetentionConfiguration is not null)
        {
            try { diagnosticRetentionConfiguration.Observe(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                diagnosticRetentionConfiguration.HoldUnavailable();
                ApplicationLog.Error(logger, exception, "Reading SQLite diagnostic retention");
            }
            selectedDiagnosticRetentionDays = diagnosticRetentionConfiguration.Get().Desired?.Days ?? DiagnosticRetentionDays.DefaultDays;
            diagnosticRetentionConfiguration.Changed += OnDiagnosticRetentionChanged;
        }
        if (responseModeConfiguration is not null)
        {
            responseModeConfiguration.Changed += OnResponseModeConfigurationChanged;
        }
        if (providerModeConfiguration is not null)
        {
            providerModeConfiguration.Changed += OnProviderModeConfigurationChanged;
        }
        if (providerProfileConfiguration is not null)
        {
            providerProfileConfiguration.Changed += OnProviderProfileConfigurationChanged;
            providerProfileConfiguration.SuggestionChanged += OnProviderSuggestionChanged;
        }
        if (outputConfiguration is not null)
        {
            outputConfiguration.Changed += OnOutputConfigurationChanged;
        }
        this.clipboardPreview = clipboardPreview;
        this.preferenceWriter = preferenceWriter;
        this.outputCatalog = outputCatalog;
        this.clipboardRead = clipboardRead;
        this.clipboardReuse = clipboardReuse;
        this.clipboardRevoke = clipboardRevoke;
        this.clipboardExplain = clipboardExplain;
        this.clipboardExplanation = clipboardExplanation;
        if (clipboardExplanation is not null) { clipboardExplanation.Changed += OnClipboardPreviewChanged; }
        clipboardPreview.Changed += OnClipboardPreviewChanged;

        AsyncCommand CreateCommand(Func<Task> execute, Func<bool>? canExecute = null, Func<bool>? canExecuteWhileExecuting = null) =>
            new(() => Kora.Application.Hosting.HostRequestRunner.RunAsync(
                    HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi, execute),
                exception => Kora.Application.Hosting.HostRequestRunner.Run(
                    HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi,
                    () => HandleCommandException(exception)), canExecute, canExecuteWhileExecuting);

        InitializeSpeechInputConfiguration(speechInputConfiguration, execute => CreateCommand(execute));
        InitializeVoiceCaptureTiming(voiceCaptureTimingConfiguration, execute => CreateCommand(execute));
        InitializeSpeechComparison(speechSetupComparison, execute => CreateCommand(execute));
        InitializeConversationalReplies(conversationalReplies, execute => CreateCommand(execute));
        InitializeForegroundPtt(foregroundPtt, execute => CreateCommand(execute));
        InitializeConversationExecution(conversationExecution, execute => CreateCommand(execute));
        InitializeLogonStartup(logonStartupConfiguration, execute => CreateCommand(execute));
        InitializeStartupPresentation(startupPresentationConfiguration, execute => CreateCommand(execute));
        InitializeQuietHours(quietHoursConfiguration, execute => CreateCommand(execute));
        InitializeDetailPresentation(detailPresentationConfiguration, execute => CreateCommand(execute));
        InitializeMarkdownDisplay(markdownDisplayConfiguration, execute => CreateCommand(execute));
        ResetThemeCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.Theme));
        ResetReaderTextScaleCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.ReaderTextScale));
        ResetReducedMotionCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.ReducedMotion));
        ResetConversationSideCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.ConversationOnRight));
        ResetResponseTimeoutCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.ResponseTimeout));
        ResetPresenceDisplayCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.PresenceDisplay));
        ResetPresenceRendererCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.NativePresenceRenderer));
        ResetPresenceTimeoutCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.PresenceTimeout));
        ResetPresenceSizeCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.PresenceSize));
        ResetPresenceDotSizeCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.DotSize));
        ResetPresenceDotDensityCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.DotDensity));
        ResetPresenceMovementSpeedCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.MovementSpeed));
        ResetPresenceSpeechScalingCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.SpeechScaling));
        ResetPresenceSpeechScaleAmountCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.SpeechScaleAmount));
#pragma warning disable VSTHRD003 // Await the owned preference queue started by these reset requests without blocking the dispatcher.
        ResetResponseAlwaysVisibleCommand = CreateCommand(() =>
        {
            IsResponseAlwaysVisible = ResponseWindowSettings.Default.AlwaysShow;
            return PreferenceUpdate;
        });
        ResetResponseWindowTopmostCommand = CreateCommand(() =>
        {
            IsResponseWindowTopmost = ResponseWindowSettings.Default.Topmost;
            return PreferenceUpdate;
        });
#pragma warning restore VSTHRD003
        ResetSpeechProviderCommand = CreateCommand(() => ResetSpeechAsync(SpeechOption.Provider));
        ResetSpeechVoiceCommand = CreateCommand(() => ResetSpeechAsync(SpeechOption.Voice));
        RefreshOutputDevicesCommand = CreateCommand(RefreshOutputConfigurationAsync);
        SaveOutputDeviceCommand = CreateCommand(() => SaveOutputChoiceAsync(false));
        ResetOutputDeviceCommand = CreateCommand(() => SaveOutputChoiceAsync(true));
        RefreshPlaybackVolumeCommand = CreateCommand(() => ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.LocalUser));
        SavePlaybackVolumeCommand = CreateCommand(() => ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Set,
            SelectedPlaybackVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)), SecurityAuditInitiator.LocalUser));
        ResetPlaybackVolumeCommand = CreateCommand(() => ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser));
        RefreshWindowsSpeechRateCommand = CreateCommand(() => ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.LocalUser));
        SaveWindowsSpeechRateCommand = CreateCommand(() => ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Set,
            SelectedWindowsSpeechRate.ToString(System.Globalization.CultureInfo.InvariantCulture)), SecurityAuditInitiator.LocalUser));
        ResetWindowsSpeechRateCommand = CreateCommand(() => ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser));
        RefreshDiagnosticRetentionCommand = CreateCommand(() => ExecuteDiagnosticRetentionCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.LocalUser));
        RefreshSessionRetentionCommand = CreateCommand(() => ConfigureSessionRetentionAsync(save: false, reset: false));
        SaveSessionRetentionCommand = CreateCommand(() => ConfigureSessionRetentionAsync(save: true, reset: false));
        ResetSessionRetentionCommand = CreateCommand(() => ConfigureSessionRetentionAsync(save: true, reset: true));
        RefreshOrdinaryDeadlineCommand = CreateCommand(() => ExecuteOrdinaryDeadlineCommandAsync(
            new(AppearanceCommandOperation.Get), SecurityAuditInitiator.LocalUser));
        SaveOrdinaryDeadlineCommand = CreateCommand(SaveOrdinaryDeadlineAsync);
        ResetOrdinaryDeadlineCommand = CreateCommand(() => ExecuteOrdinaryDeadlineCommandAsync(
            new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser));
        PreviewSessionRetentionCommand = new(PreviewSessionRetentionAsync,
            exception => ApplicationLog.SessionRetentionPreviewFailed(logger, exception.GetType().Name),
            () => CanPreviewSessionRetention);
        SaveDiagnosticRetentionCommand = CreateCommand(() => ExecuteDiagnosticRetentionCommandAsync(new(AppearanceCommandOperation.Set,
            SelectedDiagnosticRetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture)), SecurityAuditInitiator.LocalUser));
        ResetDiagnosticRetentionCommand = CreateCommand(() => ExecuteDiagnosticRetentionCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser));
        RefreshAuditRetentionCommand = CreateCommand(() => ExecuteAuditRetentionCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.LocalUser));
        SaveAuditRetentionCommand = CreateCommand(() => ExecuteAuditRetentionCommandAsync(new(AppearanceCommandOperation.Set,
            SelectedAuditRetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture)), SecurityAuditInitiator.LocalUser));
        ResetAuditRetentionCommand = CreateCommand(() => ExecuteAuditRetentionCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser));
        PreviewAuditImpactCommand = new(PreviewAuditImpactAsync, exception =>
            ApplicationLog.Error(logger, exception, "Observing audit-retention impact"), () => CanPreviewAuditImpact);
        PreviewDiagnosticImpactCommand = new(PreviewDiagnosticImpactAsync, FailDiagnosticImpactPreview,
            () => CanPreviewDiagnosticImpact);
        ClearDiagnosticImpactCommand = new(ClearDiagnosticImpactAsync, FailDiagnosticImpactPreview,
            () => CanClearDiagnosticImpact);
        CancelDiagnosticImpactCommand = new(CancelDiagnosticImpactAsync, FailDiagnosticImpactPreview,
            () => CanCancelDiagnosticImpact);
        // Preference observation can retire previews synchronously; attach only after their commands exist.
        if (diagnosticImpactPreview is not null) { diagnosticImpactPreview.Changed += OnDiagnosticImpactPreviewChanged; }
        PropertyChanged += OnDiagnosticImpactHostPropertyChanged;
        if (auditImpactPreview is not null) { auditImpactPreview.Changed += OnAuditImpactPreviewChanged; }
        RefreshResponseModeCommand = CreateCommand(() => RunNativeResponseModeAsync(AppearanceCommandOperation.Get));
        SaveResponseModeCommand = CreateCommand(() => RunNativeResponseModeAsync(AppearanceCommandOperation.Set));
        ResetResponseModeCommand = CreateCommand(() => RunNativeResponseModeAsync(AppearanceCommandOperation.Reset));
        RefreshProviderModeCommand = CreateCommand(() => RunNativeProviderModeAsync(AppearanceCommandOperation.Get));
        SaveProviderModeCommand = CreateCommand(() => RunNativeProviderModeAsync(AppearanceCommandOperation.Set));
        ResetProviderModeCommand = CreateCommand(() => RunNativeProviderModeAsync(AppearanceCommandOperation.Reset));
        RefreshProviderProfileCommand = CreateCommand(() => RunNativeProviderProfileAsync(new(AppearanceCommandOperation.Get, Refresh: true)));
        ReviewProviderSuggestionCommand = CreateCommand(() => RunNativeProviderProfileAsync(new(AppearanceCommandOperation.Get, ReviewSuggestion: true)));
        DiscardProviderSuggestionCommand = CreateCommand(() => RunNativeProviderProfileAsync(new(AppearanceCommandOperation.Get, DiscardSuggestion: true)));
        SaveProviderServiceCommand = CreateCommand(() => RunNativeProviderProfileAsync(new(AppearanceCommandOperation.Set, Kora.Core.Providers.ProviderService.Unset)));
        ResetProviderServiceCommand = CreateCommand(() => RunNativeProviderProfileAsync(new(AppearanceCommandOperation.Reset)));
        SaveProviderProfileCommand = CreateCommand(() => RunNativeProviderProfileAsync(new(AppearanceCommandOperation.Set)));
        ReviewProviderModelCommand = CreateCommand(() => ReviewNativeProviderField(Kora.Core.Providers.ProviderProfileField.Model));
        ReviewProviderReasoningCommand = CreateCommand(() => ReviewNativeProviderField(Kora.Core.Providers.ProviderProfileField.Reasoning));
        ReviewProviderContextCommand = CreateCommand(() => ReviewNativeProviderField(Kora.Core.Providers.ProviderProfileField.Context));
        ConfirmProviderFieldCommand = CreateCommand(() => ExecuteProviderProfileCommandAsync(
            new(AppearanceCommandOperation.Set, Confirmation: ProviderFieldConfirmationId ?? Guid.Empty), SecurityAuditInitiator.LocalUser));
        CancelProviderFieldCommand = CreateCommand(() => ExecuteProviderProfileCommandAsync(
            new(AppearanceCommandOperation.Set, Confirmation: ProviderFieldConfirmationId ?? Guid.Empty, Cancel: true), SecurityAuditInitiator.LocalUser));
        ResetProviderProfileCommand = CreateCommand(() => RunNativeProviderProfileAsync(new(AppearanceCommandOperation.Reset, TupleOnly: true)));
        ProviderSignInCommand = CreateCommand(() => RunNativeProviderProfileAsync(new(AppearanceCommandOperation.Get, SignIn: true)));
        RefreshPreapprovedUrisCommand = CreateCommand(
            () => RunNativePreapprovedUriCommandAsync(PreapprovedUriCommandOperation.List),
            () => CanChangePreapprovedUris);
        AddPreapprovedUriCommand = CreateCommand(
            () => RunNativePreapprovedUriCommandAsync(PreapprovedUriCommandOperation.Add, PreapprovedUriInput),
            () => CanChangePreapprovedUris && !string.IsNullOrWhiteSpace(PreapprovedUriInput));
        RemovePreapprovedUriCommand = CreateCommand(
            () => RunNativePreapprovedUriCommandAsync(PreapprovedUriCommandOperation.Remove, SelectedPreapprovedUri),
            () => CanChangePreapprovedUris && SelectedPreapprovedUri is not null);
        ClearPreapprovedUrisCommand = CreateCommand(
            () => RunNativePreapprovedUriCommandAsync(PreapprovedUriCommandOperation.Clear),
            () => CanChangePreapprovedUris && PreapprovedUriPatterns.Count > 0);
        RefreshArtifactDirectoriesCommand = CreateCommand(
            RefreshArtifactDirectoriesAsync,
            () => CanChangeArtifactDirectories);
        AddArtifactDirectoryCommand = CreateCommand(
            AddArtifactDirectoryAsync,
            () => CanChangeArtifactDirectories && !string.IsNullOrWhiteSpace(ArtifactDirectoryInput));
        RemoveArtifactDirectoryCommand = CreateCommand(
            RemoveArtifactDirectoryAsync,
            () => CanChangeArtifactDirectories && SelectedArtifactDirectory is not null);
        IncludeMcpServerCommand = CreateCommand(
            () => SetSelectedMcpServerIncludedAsync(included: true),
            () => CanChangeArtifactDirectories && SelectedMcpServer is { IsIncluded: false });
        ExcludeMcpServerCommand = CreateCommand(
            () => SetSelectedMcpServerIncludedAsync(included: false),
            () => CanChangeArtifactDirectories && SelectedMcpServer is { IsIncluded: true });
        RefreshArtifactDirectories();
        RefreshInCallFeedbackCommand = CreateCommand(() => ExecuteInCallFeedbackCommandAsync(
            new(AppearanceCommandOperation.Get), SecurityAuditInitiator.LocalUser));
        SaveInCallFeedbackCommand = CreateCommand(() => ExecuteInCallFeedbackCommandAsync(
            new(AppearanceCommandOperation.Set), SecurityAuditInitiator.LocalUser,
            SelectedInCallFeedbackChoice ?? throw new InvalidOperationException("Refresh and choose one exact in-call feedback mode.")));
        ResetInCallFeedbackCommand = CreateCommand(() => ExecuteInCallFeedbackCommandAsync(
            new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser));
        InspectCommunicationActivityCommand = CreateCommand(() => ExecuteCommunicationActivityAsync(
            new(AppearanceCommandOperation.Get), SecurityAuditInitiator.LocalUser));
        EnableCommunicationActivityCommand = CreateCommand(() => ExecuteCommunicationActivityAsync(
            new(AppearanceCommandOperation.Set, CommunicationActivitySelection.WindowsCommunicationsAudio), SecurityAuditInitiator.LocalUser));
        DisableCommunicationActivityCommand = CreateCommand(() => ExecuteCommunicationActivityAsync(
            new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser));
        RefreshSpeechTextCommand = CreateCommand(() => RunNativeSpeechTextAsync(AppearanceCommandOperation.Get));
        SaveSpeechTextCommand = CreateCommand(() => RunNativeSpeechTextAsync(AppearanceCommandOperation.Set));
        ResetSpeechTextCommand = CreateCommand(() => RunNativeSpeechTextAsync(AppearanceCommandOperation.Reset));
        ToggleSpeechCaptionPinCommand = CreateCommand(() =>
        {
            ChangeSpeechCaptionPin(!IsSpeechCaptionPinned, SecurityAuditInitiator.LocalUser);
            return Task.CompletedTask;
        });
        SaveSpeechCaptionOptionCommand = CreateCommand(() => ExecuteSpeechTextCommandAsync(
            new(AppearanceCommandOperation.Set, CaptionOption: SelectedSpeechCaptionOption), SecurityAuditInitiator.LocalUser,
            SelectedSpeechCaptionChoice));
        ResetSpeechCaptionOptionCommand = CreateCommand(() => ExecuteSpeechTextCommandAsync(
            new(AppearanceCommandOperation.Reset, CaptionOption: SelectedSpeechCaptionOption), SecurityAuditInitiator.LocalUser));
        ResetSummarySentencesCommand = CreateCommand(() => ResetSpeechAsync(SpeechOption.SummarySentences));
        ResetSummaryWordsCommand = CreateCommand(() => ResetSpeechAsync(SpeechOption.SummaryWords));
        ToggleListeningCommand = CreateCommand(
            ToggleListeningAsync,
            () => !lifecycleAdmissionClosed && !IsBusy
                  && (IsVoiceEnabled
                      || (HasVoiceConsent && EffectiveMicrophone is not null
                      && IsVoiceActivationAvailable && !IsMicrophoneAccessDenied)));
        // Call-time admission must retain an existing request's original invocation identity.
        BeginPushToTalkCommand = new(
            () => HostActivity.HasScope ? BeginPushToTalkAsync()
                : Kora.Application.Hosting.HostRequestRunner.RunAsync(RequestOrigin.LocalUi, BeginPushToTalkAsync),
            exception => Kora.Application.Hosting.HostRequestRunner.Run(
                HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi, () => HandleCommandException(exception)),
            () => CanPushToTalk);
        EndPushToTalkCommand = CreateCommand(EndPushToTalkAsync);
        WithdrawVoiceConsentCommand = CreateCommand(() => SetVoiceConsentAsync(false));
        EnableVoiceConsentCommand = CreateCommand(() => SetVoiceConsentAsync(true));
        RefreshMicrophonesCommand = CreateCommand(RefreshMicrophonesAsync);
        this.microphoneCatalog = microphoneCatalog
            ?? new BoundedMicrophoneCatalog(voiceRecognition, privacyObservation, microphoneAccessService, logger);
        listeningStop = new(voiceRecognition, logger);
        RefreshCommand = CreateCommand(
            RefreshAsync,
            () => !IsBusy && !IsLocalModelSetupActive && !IsPowerShellSetupActive);
        RunTypedCommand = CreateCommand(
            RunTypedCommandAsync,
            () => !string.IsNullOrWhiteSpace(CommandText)
                  && (!IsBusy || IsSetupStatusCommand() || IsListeningStopCommand(CommandText)
                      || SessionCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Mcp.McpCommand.Parse(CommandText, AssistantName) is not null
                      || SessionDispositionReviewCommand.Parse(CommandText, AssistantName) is not null
                      || WorkspaceSurfaceCommand.Parse(CommandText, AssistantName) is not null
                      || AttachmentCataloguePreparationCommand.Parse(CommandText, AssistantName) is not null
                      || EvidenceTraceCommand.Parse(CommandText, AssistantName) is not null
                      || PendingQuestionCommand.Parse(CommandText, AssistantName) is not null
                      || MemoryCommand.Parse(CommandText, AssistantName) is not null
                      || IsInstructionEditorCommand(CommandText)
                      || Kora.Application.Definitions.CustomizationCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Tools.CustomizationDiscoveryCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Tools.SessionMetadataCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Skills.OwnedSkillAuthoringCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Skills.SkillReviewCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Skills.SharedSkillInvocationCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Skills.NamedSkillCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.KnowledgeSources.KnowledgeSourceAnswerCommand.Parse(CommandText, AssistantName) is not null
                      || KnowledgeSourceCaptureLimitsCommand.Parse(CommandText, AssistantName) is not null
                      || NewRequestOpenCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Core.Interaction.LocalEventCommand.Parse(CommandText, AssistantName) is not null
                      || ManualCallCommand.Parse(CommandText, AssistantName) is not null),
            () => Kora.Application.Skills.OwnedSkillAuthoringCommand.Parse(CommandText, AssistantName)?.Operation is "close-use"
                  || CallSpeechCommand.Parse(CommandText, AssistantName)?.Operation == CallSpeechOperation.Cancel);
        PreviewVoiceCommand = CreateCommand(
            PreviewVoiceAsync,
            () => SelectedVoice is not null && SelectedOutputDevice is not null
                && EffectiveOutputDevice is { IsMuted: false }
                && !IsBusy && !IsSpeaking && !IsListening && !voiceRecognition.IsListening && !speechInterruptionActive);
        StopSpeechCommand = CreateCommand(StopSpeakingAsync, () => IsSpeaking);
        speechReadOnce = new Kora.Tools.Speech.SpeechReadOnce(this);
        ReadCurrentResponseCommand = CreateCommand(
            () => ReadCurrentResponseAsync(CaptureResponseReadInput()), () => CanReadCurrentResponse);
        PlanCurrentResponseReadCommand = CreateCommand(
            () => RunResponseReadProposalDecision(responseReadProposal, "plan"), () => CanPlanCurrentResponseRead);
        ConfirmCurrentResponseReadCommand = CreateCommand(
            () => CaptureResponseReadProposalDecision(confirm: true)(), () => CanConfirmCurrentResponseRead);
        CancelCurrentResponseReadCommand = CreateCommand(
            () => CaptureResponseReadProposalDecision(confirm: false)());
        ReviewCallSpeechCommand = CreateCallSpeechCommand(CallSpeechOperation.Review,
            () => CanReviewCallSpeech);
        ConfirmCallSpeechCommand = CreateCallSpeechCommand(CallSpeechOperation.Confirm,
            () => CanConfirmCallSpeech);
        CancelCallSpeechCommand = CreateCallSpeechCommand(CallSpeechOperation.Cancel);
        GetCallSpeechStatusCommand = CreateCallSpeechCommand(CallSpeechOperation.Status);
        DownloadSpeechProviderCommand = CreateCommand(
            DownloadSpeechProviderAsync,
            () => CanDownloadSpeechProvider);
        RemoveSpeechProviderCommand = CreateCommand(
            RemoveSpeechProviderAsync,
            () => CanRemoveSpeechProvider);
        ToggleCallVisualOverrideCommand = CreateCommand(ToggleCallVisualOverrideAsync);
        ToggleCallVoiceActivationCommand = CreateCommand(ToggleCallVoiceActivationAsync);
        EnableManualCallCommand = CreateCommand(() => SetManualCallAsync(true, RequestOrigin.LocalUi, CallPolicyRevision));
        ClearManualCallCommand = CreateCommand(() => SetManualCallAsync(false, RequestOrigin.LocalUi, CallPolicyRevision));
        ResetManualCallCommand = CreateCommand(() => ExecuteManualCallCommandAsync(new(AppearanceCommandOperation.Reset),
            CaptureManualCallInput(RequestOrigin.LocalUi, CallPolicyRevision, native: true)));
        GetManualCallStatusCommand = CreateCommand(() => ExecuteManualCallCommandAsync(new(AppearanceCommandOperation.Get),
            CaptureManualCallInput(RequestOrigin.LocalUi, CallPolicyRevision)));
        OpenMicrophonePrivacySettingsCommand = CreateCommand(OpenMicrophonePrivacySettingsAsync);
        ApplyAssistantNameCommand = CreateCommand(
            () => SetAssistantNameAsync(AssistantNameInput),
            CanApplyAssistantName);
        ResetAssistantNameCommand = CreateCommand(ResetAssistantNameAsync);
        voiceRecognition.TranscriptRecognized += OnTranscriptRecognized;
        voiceRecognition.RecognitionFailed += OnRecognitionFailed;
        voiceRecognition.CaptureStateChanged += OnCaptureStateChanged;
        voiceRecognition.RecognitionCompleted += OnRecognitionCompleted;
        communicationPolicy.Changed += OnCommunicationPolicyChanged;
        dependencyBootstrapper.Tasks.Changed += (_, _) =>
            uiDispatcher.Post(() => OnPropertyChanged(nameof(SetupTasks)));
        privacyObservation.Changed += OnWindowsPrivacyChanged;
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
                OnPropertyChanged(nameof(CanPushToTalk));
                ToggleListeningCommand.NotifyCanExecuteChanged();
                BeginPushToTalkCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string MicrophoneAccessMessage => MicrophoneAccessStatus.Detail;

    public bool IsMicrophoneAccessDenied =>
        MicrophoneAccessStatus.State == MicrophoneAccessState.Denied;

    public event EventHandler<WindowAction>? WindowActionRequested;
    public event EventHandler? ResponseRestoreRequested;

    public event EventHandler? SettingsRequested;
    public event EventHandler? ReadinessRequested;

    public event EventHandler? DocumentationRequested;


    public ObservableCollection<MicrophoneDevice> Microphones { get; } = [];

    public ObservableCollection<SpeechProvider> SpeechProviders { get; } = [];

    public ObservableCollection<SpeechVoice> Voices { get; } = [];

    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = [];

    public ObservableCollection<DependencyStatus> Dependencies { get; } = [];

    public ObservableCollection<ArtifactCommandOption> ArtifactCommandOptions { get; } = [];

    public IReadOnlyList<SetupTask> SetupTasks => dependencyBootstrapper.Tasks.Tasks;

    public bool IsResponseInteractionPending => HasDetailsQuestion;
    public IReadOnlyList<ResponseAction> ResponseActions { get; private set; } = [];
    public bool HasResponseActions => ResponseActions.Count > 0;
    public string CommandInputPlaceholder => HasDetailsQuestion ? "Answer this exact Details question: Yes or Not now"
        : "Type a command or / for artifacts";
    public string CommandSubmitLabel => HasDetailsQuestion ? "Answer" : "Run";
    internal static bool ShouldForceVisualResponse(
        bool interactionPending, AssistantState state) =>
        interactionPending || state == AssistantState.Failure;

    public bool LocalModelsEnabled
    {
        get => !modelPreferenceWriteActive && !modelPreferencesUnavailable && localModelsEnabled;
        set => QueuePreference(nameof(LocalModelsEnabled), () =>
            TrySetModelExecutionSettingsAsync(value, hostedModelsEnabled, SecurityAuditInitiator.LocalUser));
    }

    public bool HostedModelsEnabled
    {
        get => !modelPreferenceWriteActive && !modelPreferencesUnavailable && hostedModelsEnabled;
        set => QueuePreference(nameof(HostedModelsEnabled), () =>
            TrySetModelExecutionSettingsAsync(localModelsEnabled, value, SecurityAuditInitiator.LocalUser));
    }

    public string HostedModelExecutionDescription => HostedModelsEnabled
        ? "Online AI models are allowed, but no online service is set up in this version."
        : "Online AI models are blocked. No online service is set up in this version.";

    private async Task<bool> TrySetModelExecutionSettingsAsync(
        bool enableLocalModels,
        bool enableHostedModels,
        SecurityAuditInitiator initiator)
    {
        if (!AdmitModelPreferenceChange()) { return false; }
        if (enableLocalModels == localModelsEnabled
            && enableHostedModels == hostedModelsEnabled)
        {
            return true;
        }

        if (!await SavePreferenceAsync(() => modelExecutionPreferences.Save(new ModelExecutionSettings(
            enableLocalModels, enableHostedModels)), ModelExecutionPreferenceAction, initiator, "model settings"))
        {
            return false;
        }

        localModelsEnabled = enableLocalModels;
        hostedModelsEnabled = enableHostedModels;
        OnPropertyChanged(nameof(LocalModelsEnabled));
        OnPropertyChanged(nameof(HostedModelsEnabled));
        OnPropertyChanged(nameof(HostedModelExecutionDescription));

        return true;
    }

    private string DescribeModelExecution()
    {
        var localReadiness = Dependencies.FirstOrDefault(status =>
            string.Equals(status.Id, "local.inference", StringComparison.Ordinal))?.Readiness;
        var localStatus = LocalModelsEnabled
            ? localReadiness == DependencyReadiness.Ready
                ? "Local models are enabled and Ollama is ready."
                : "Local models are enabled, but Ollama is not ready."
            : "Local models are disabled.";
        var hostedStatus = HostedModelsEnabled
            ? "Hosted models are enabled, but no hosted provider is configured in this build."
            : "Hosted models are disabled.";
        return $"{localStatus} {hostedStatus}";
    }

    private bool isLocalTaskCancellable;

    public bool IsLocalTaskCancellable
    {
        get => isLocalTaskCancellable;
        private set
        {
            SetProperty(ref isLocalTaskCancellable, value);
            OnPropertyChanged(nameof(IsCancelTaskVisible));
        }
    }

    public bool IsCancelTaskVisible => IsLocalTaskCancellable || IsSpeaking || clipboardPreview.IsReading
        || clipboardExplanation?.IsQuiescent == false
        || IsWebPageRetrievalActive || IsWebSummaryActive || HasWebResultDetails
        || filePreview?.IsBusy == true || sessionFiles?.IsQuiescent == false || FileReview is not null || FileRevision is not null
        || FolderReview is not null || FolderRevision is not null;

    public bool IsLocalModelSetupActive
    {
        get => isLocalModelSetupActive;
        private set
        {
            if (SetProperty(ref isLocalModelSetupActive, value))
            {
                OnPropertyChanged(nameof(CanInstallLocalModel));
                OnPropertyChanged(nameof(CanInstallPowerShell));
                RefreshCommand.NotifyCanExecuteChanged();



            }
        }
    }

    public bool CanInstallLocalModel => !IsBusy && !IsLocalModelSetupActive
        && !IsPowerShellSetupActive;

    public bool IsPowerShellSetupActive
    {
        get => isPowerShellSetupActive;
        private set
        {
            if (SetProperty(ref isPowerShellSetupActive, value))
            {
                OnPropertyChanged(nameof(CanInstallPowerShell));
                OnPropertyChanged(nameof(CanInstallLocalModel));
                RefreshCommand.NotifyCanExecuteChanged();



            }
        }
    }

    public bool CanInstallPowerShell => !IsBusy && !IsPowerShellSetupActive
        && !IsLocalModelSetupActive;

    public string PowerShellSetupStatus
    {
        get => powerShellSetupStatus;
        private set => SetProperty(ref powerShellSetupStatus, value);
    }

    public string LocalModelSetupStatus
    {
        get => localModelSetupStatus;
        private set => SetProperty(ref localModelSetupStatus, value);
    }

    public int LocalModelSetupProgress
    {
        get => localModelSetupProgress;
        private set => SetProperty(ref localModelSetupProgress, value);
    }

    public bool IsLocalModelSetupProgressIndeterminate
    {
        get => isLocalModelSetupProgressIndeterminate;
        private set => SetProperty(ref isLocalModelSetupProgressIndeterminate, value);
    }

    public bool ShouldOfferLocalModelSetup => Dependencies.Any(status =>
        string.Equals(status.Id, "local.inference", StringComparison.Ordinal)
        && status.Readiness != DependencyReadiness.Ready);

    public bool ShouldOfferPowerShellSetup => Dependencies.Any(status =>
        string.Equals(status.Id, dependencySetup.PowerShellTaskId, StringComparison.Ordinal)
        && status.Readiness != DependencyReadiness.Ready);

    public IReadOnlyList<CommandDefinition> Commands => commandCatalog.GetCommands(AssistantName);

    public IReadOnlyList<ResponseOutputMode> ResponseOutputModes { get; } =
        Enum.GetValues<ResponseOutputMode>();

    public IReadOnlyList<ResponseModeOverrideOption> ResponseModeOptions { get; } =
    [
        new(Presentation.SettingsText.ResponseMode(ResponseOutputMode.Hybrid), ResponseOutputMode.Hybrid),
        new(Presentation.SettingsText.ResponseMode(ResponseOutputMode.VoiceOnly), ResponseOutputMode.VoiceOnly),
        new(Presentation.SettingsText.ResponseMode(ResponseOutputMode.VisualOnly), ResponseOutputMode.VisualOnly),
    ];

    public IReadOnlyList<ResponseModeOverrideOption> ResponseModeOverrideOptions { get; } =
    [
        new("Use default", null),
        new(Presentation.SettingsText.ResponseMode(ResponseOutputMode.Hybrid), ResponseOutputMode.Hybrid),
        new(Presentation.SettingsText.ResponseMode(ResponseOutputMode.VoiceOnly), ResponseOutputMode.VoiceOnly),
        new(Presentation.SettingsText.ResponseMode(ResponseOutputMode.VisualOnly), ResponseOutputMode.VisualOnly),
    ];

    public IReadOnlyList<ApplicationThemeMode> ThemeModes { get; } =
        Enum.GetValues<ApplicationThemeMode>();

    public AsyncCommand ToggleListeningCommand { get; }

    public AsyncCommand BeginPushToTalkCommand { get; }

    public AsyncCommand EndPushToTalkCommand { get; }

    public AsyncCommand WithdrawVoiceConsentCommand { get; }

    public AsyncCommand EnableVoiceConsentCommand { get; }

    public AsyncCommand RefreshMicrophonesCommand { get; }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand RunTypedCommand { get; }

    public AsyncCommand PreviewVoiceCommand { get; }

    public AsyncCommand StopSpeechCommand { get; }

    public AsyncCommand DownloadSpeechProviderCommand { get; }

    public AsyncCommand RemoveSpeechProviderCommand { get; }

    public AsyncCommand ToggleCallVisualOverrideCommand { get; }

    public AsyncCommand ToggleCallVoiceActivationCommand { get; }

    public AsyncCommand EnableManualCallCommand { get; }

    public AsyncCommand ClearManualCallCommand { get; }

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

    public string SettingsWindowTitle => $"{AssistantName} settings - {applicationInfo.Version}";

    public string SettingsSubtitle => $"{AssistantName} preferences on this device";

    public string AppearanceSettingsDescription =>
        $"Choose how {AssistantName} looks, including text size and animated status dots.";

    public string AppearanceThemeDescription =>
        $"System follows your Windows theme. Light or Dark sets the theme for all {AssistantName} windows.";

    public string PresenceTimeoutDescription =>
        "Hide the presence after this many seconds of inactivity when no prompts are waiting.";

    public string PresenceDisplayDescription =>
        IsPresenceDisplayEnabled
            ? "Show animated dots to indicate what the assistant is doing."
            : "Hide the animated dots. You can still use all other windows and controls.";

    public string ResponseTimeoutDescription =>
        "Hide an unpinned response window after this many seconds without interaction.";

    public string PresenceSizeDescription =>
        $"Overall size: {PresenceSizePixels} pixels.";

    public string PresenceDotSizeDescription =>
        $"Dot size: {PresenceDotSizePercent}% of normal.";

    public string PresenceDotDensityDescription =>
        $"Number of dots: {PresenceSettings.GetParticleCount(PresenceDotDensityPercent)} ({PresenceDotDensityPercent}% of normal).";

    public string PresenceMovementSpeedDescription =>
        $"Movement speed: {PresenceMovementSpeedPercent}% of normal.";

    public string PresenceSpeechScalingDescription =>
        IsPresenceSpeechScalingEnabled
            ? "The presence grows and shrinks with speech playback."
            : "The presence stays at its normal size during speech playback.";

    public string PresenceSpeechScaleAmountDescription =>
        $"How much the dots grow and shrink with speech: {PresenceSpeechScaleAmountPercent}%.";

    public string MainCaptureDescription =>
        $"{AssistantName} checks for devices without recording. Recording starts only after you enable listening.";

    public string SpeechAudioSettingsDescription =>
        $"Choose how {AssistantName} listens and speaks on this device.";

    public string ResponseSettingsDescription =>
        $"Choose when {AssistantName} uses visual text, speech, or both.";

    public string ReadinessSettingsDescription =>
        $"Check what {AssistantName} needs and complete setup.";

    public ApplicationThemeMode ThemeMode
    {
        get => themeMode;
        set => QueueAppearance(AppearanceOption.Theme, new AppearanceValue.Theme(value), nameof(ThemeMode));
    }

    public bool SetThemeMode(
        ApplicationThemeMode value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.Theme, new AppearanceValue.Theme(value), initiator, nameof(ThemeMode));

    public bool IsPresenceDisplayEnabled
    {
        get => isPresenceDisplayEnabled;
        set => QueueAppearance(AppearanceOption.PresenceDisplay, new AppearanceValue.Toggle(value), nameof(IsPresenceDisplayEnabled));
    }

    public bool SetPresenceDisplayEnabled(
        bool value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.PresenceDisplay, new AppearanceValue.Toggle(value), initiator, nameof(IsPresenceDisplayEnabled));

    public int PresenceTimeoutSeconds
    {
        get => presenceTimeoutSeconds;
        set => QueueAppearance(AppearanceOption.PresenceTimeout, new AppearanceValue.Number(value), nameof(PresenceTimeoutSeconds));
    }

    public bool SetPresenceTimeoutSeconds(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.PresenceTimeout, new AppearanceValue.Number(value), initiator, nameof(PresenceTimeoutSeconds));

    public int ResponseTimeoutSeconds
    {
        get => responseTimeoutSeconds;
        set => QueueAppearance(AppearanceOption.ResponseTimeout, new AppearanceValue.Number(value), nameof(ResponseTimeoutSeconds));
    }

    public bool SetResponseTimeoutSeconds(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.ResponseTimeout, new AppearanceValue.Number(value), initiator, nameof(ResponseTimeoutSeconds));

    public bool IsResponseAlwaysVisible
    {
        get => responseWindowSettings.AlwaysShow;
        set => QueuePreference(nameof(IsResponseAlwaysVisible), () => SetResponseAlwaysVisibleAsync(value));
    }

    public Task<bool> SetResponseAlwaysVisibleAsync(
        bool value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser,
        Action<string, string>? reportFailure = null) =>
        SetResponseWindowSettingsAsync(
            responseWindowSettings with { AlwaysShow = value },
            initiator, reportFailure);

    public bool IsResponseWindowTopmost
    {
        get => responseWindowSettings.Topmost;
        set => QueuePreference(nameof(IsResponseWindowTopmost), () => SetResponseWindowTopmostAsync(value));
    }

    public Task<bool> SetResponseWindowTopmostAsync(
        bool value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser,
        Action<string, string>? reportFailure = null) =>
        SetResponseWindowSettingsAsync(
            responseWindowSettings with { Topmost = value },
            initiator, reportFailure);

    public ResponseWindowPosition? ResponseWindowPosition =>
        responseWindowSettings.Position;

    public void QueueResponseWindowPosition(ResponseWindowPosition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        QueuePreference(nameof(ResponseWindowPosition), () => SetResponseWindowPositionAsync(value));
    }

    public Task<bool> SetResponseWindowPositionAsync(
        ResponseWindowPosition value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SetResponseWindowSettingsAsync(
            responseWindowSettings with { Position = value },
            initiator);
    }

    public int PresenceSizePixels
    {
        get => presenceSizePixels;
        set => QueueAppearance(AppearanceOption.PresenceSize, new AppearanceValue.Number(value), nameof(PresenceSizePixels));
    }

    public bool SetPresenceSizePixels(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.PresenceSize, new AppearanceValue.Number(value), initiator, nameof(PresenceSizePixels));

    public int PresenceDotSizePercent
    {
        get => presenceDotSizePercent;
        set => QueueAppearance(AppearanceOption.DotSize, new AppearanceValue.Number(value), nameof(PresenceDotSizePercent));
    }

    public bool SetPresenceDotSizePercent(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.DotSize, new AppearanceValue.Number(value), initiator, nameof(PresenceDotSizePercent));

    public int PresenceDotDensityPercent
    {
        get => presenceDotDensityPercent;
        set => QueueAppearance(AppearanceOption.DotDensity, new AppearanceValue.Number(value), nameof(PresenceDotDensityPercent));
    }

    public bool SetPresenceDotDensityPercent(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.DotDensity, new AppearanceValue.Number(value), initiator, nameof(PresenceDotDensityPercent));

    public int PresenceMovementSpeedPercent
    {
        get => presenceMovementSpeedPercent;
        set => QueueAppearance(AppearanceOption.MovementSpeed, new AppearanceValue.Number(value), nameof(PresenceMovementSpeedPercent));
    }

    public bool SetPresenceMovementSpeedPercent(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.MovementSpeed, new AppearanceValue.Number(value), initiator, nameof(PresenceMovementSpeedPercent));

    public bool IsPresenceSpeechScalingEnabled
    {
        get => isPresenceSpeechScalingEnabled;
        set => QueueAppearance(AppearanceOption.SpeechScaling, new AppearanceValue.Toggle(value), nameof(IsPresenceSpeechScalingEnabled));
    }

    public bool SetPresenceSpeechScalingEnabled(
        bool value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.SpeechScaling, new AppearanceValue.Toggle(value), initiator, nameof(IsPresenceSpeechScalingEnabled));

    public int PresenceSpeechScaleAmountPercent
    {
        get => presenceSpeechScaleAmountPercent;
        set => QueueAppearance(AppearanceOption.SpeechScaleAmount, new AppearanceValue.Number(value), nameof(PresenceSpeechScaleAmountPercent));
    }

    public bool SetPresenceSpeechScaleAmountPercent(
        int value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAppearance(AppearanceOption.SpeechScaleAmount, new AppearanceValue.Number(value), initiator, nameof(PresenceSpeechScaleAmountPercent));

    public PresencePosition? PresencePosition => presencePosition;

    public void QueuePresencePosition(PresencePosition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        QueuePreference(nameof(PresencePosition), () => SetPresencePositionAsync(value));
    }

    public Task<bool> SetPresencePositionAsync(
        PresencePosition value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SetPresencePositionCoreAsync(value, initiator);
    }

    private async Task<bool> SetPresencePositionCoreAsync(PresencePosition value, SecurityAuditInitiator initiator)
    {
        if (PresencePosition == value)
        {
            return true;
        }

        if (!suppressPresenceAppearancePreferenceSave
            && !await SavePreferenceAsync(
                () => appearancePreferences.SavePresencePosition(value),
                PresencePositionConfigurationAction,
                initiator,
                "presence position"))
        {
            OnPropertyChanged(nameof(PresencePosition));
            return false;
        }

        return SetProperty(
            ref presencePosition,
            value,
            nameof(PresencePosition));
    }

    public MicrophoneDevice? SelectedMicrophone
    {
        get => selectedMicrophone;
        set
        {
            if (committingMicrophonePreference) { return; }
            if (!suppressAudioDevicePreferenceSave && value is not null)
            {
                ApplyDirectMicrophonePreference(value);
                return;
            }
            if (!suppressAudioDevicePreferenceSave && !AdmitVoiceOptionMutation(MicrophoneConfigurationAction)) { return; }
            if (!suppressAudioDevicePreferenceSave && selectedMicrophone is not null)
            {
                HoldVoiceInput("Microphone changed · use Enable listening");
            }
            SetSelectedMicrophoneState(value);
        }
    }

    public SpeechProvider? SelectedSpeechProvider
    {
        get => selectedSpeechProvider;
        set
        {
            if (!suppressSpeechProviderPreferenceSave && !AdmitVoiceOptionMutation(SpeechProviderSelectionConfigurationAction)) { return; }
            if (SetProperty(ref selectedSpeechProvider, value))
            {
                NotifySpeechProviderStateChanged();
                UpdateSpeechProviderAvailability();
            }
        }
    }

    public SpeechVoice? SelectedVoice
    {
        get => selectedVoice;
        set
        {
            if (!suppressVoicePreferenceSave)
            {
                if (value is not null) { ApplySpeechChoice(SpeechOption.Voice, value.ConfigurationId); }
                else if (AdmitVoiceOptionMutation("configuration.voice-selection")) { ClearActiveSpeechVoice(); }
                return;
            }
            if (SetProperty(ref selectedVoice, value))
            {
                SetActiveSpeechVoice(value);

                PreviewVoiceCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsSpeechOutputAvailable));
                NotifyOutputPolicyChanged();
            }
        }
    }

    public AudioOutputDevice? SelectedOutputDevice
    {
        get => selectedOutputDevice;
        set
        {
            // Native mutation uses the presented choice + explicit Save command; device records are observations only.
            if (!synchronizingOutput && !ReferenceEquals(selectedOutputDevice, value)) { OnPropertyChanged(nameof(SelectedOutputDevice)); }
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
            Interlocked.Increment(ref responseReadOperationRevision);
            SetProperty(ref isSpeechProviderOperationActive, value);
            UpdateSpeechPreparation();
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
        internal set
        {
            if (!suppressResponseModeSave && !AdmitVoiceOptionMutation(ResponseOutputConfigurationAction)) { return; }
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The response output mode is invalid.");
            }

            if (SetProperty(ref defaultResponseMode, value))
            {
                OnPropertyChanged(nameof(DefaultResponseModeOption));
                NotifyOutputPolicyChanged();
            }
        }
    }

    public ResponseModeOverrideOption DefaultResponseModeOption
    {
        get => ResponseModeOptions.Single(option => option.Mode == DefaultResponseMode);
        internal set
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
        internal set => DefaultResponseMode = value;
    }

    public bool FallbackToVisualWhenOutputMuted
    {
        get => fallbackToVisualWhenOutputMuted;
        set
        {
            if (!suppressResponseModeSave && !AdmitVoiceOptionMutation(MutedOutputFallbackConfigurationAction)) { return; }
            if (value == fallbackToVisualWhenOutputMuted)
            {
                return;
            }

            if (!suppressResponseModeSave)
            {
                QueuePreference(nameof(FallbackToVisualWhenOutputMuted), () => SaveMutedOutputFallbackPreferenceAsync(value));
                return;
            }

            SetProperty(ref fallbackToVisualWhenOutputMuted, value);
            UpdateOutputDeviceAvailability();
            NotifyOutputPolicyChanged();
        }
    }

    public ResponseOutputMode? QueueResponseMode
    {
        get => queueResponseMode;
        set
        {
            if (!AdmitVoiceOptionMutation(ResponseOutputConfigurationAction)) { return; }
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
            if (!AdmitVoiceOptionMutation(ResponseOutputConfigurationAction)) { return; }
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
        InCallFeedbackRules.Resolve(activeTaskResponseOutput?.Configured
            ?? ResponseOutputModeResolver.Resolve(DefaultResponseMode, QueueResponseMode, TaskResponseMode),
            DesiredInCallFeedback, communicationPolicy.Current.EffectiveState);

    public bool IsSpeechOutputAvailable =>
        activeSpeechVoice is not null
        && (audioPlaybackSettings.VolumeState is null || audioPlaybackSettings.VolumeState.AllowsSpeech)
        && IsWindowsSpeechRateOutputEligible
        && (responseModeConfiguration is null || responseModeConfiguration.Get().Available)
        && (inCallFeedbackConfiguration is null || inCallFeedbackConfiguration.Get().Available)
        && (outputConfiguration is null || outputConfiguration.Get(CallPolicyRevision).Available)
        && EffectiveOutputDevice is not null
        && !EffectiveOutputDevice.IsMuted;

    private MicrophoneDevice? EffectiveMicrophone =>
        string.Equals(inputDevicePreferences.Source, "unavailable", StringComparison.Ordinal) ? null
        : SelectedMicrophone?.IsSystemDefault == true
            ? systemDefaultMicrophone
            : SelectedMicrophone is not null && Microphones.Contains(SelectedMicrophone)
                ? SelectedMicrophone : null;

    private AudioOutputDevice? EffectiveOutputDevice =>
        SelectedOutputDevice?.IsSystemDefault == true
            ? systemDefaultOutputDevice
            : SelectedOutputDevice is not null && OutputDevices.Contains(SelectedOutputDevice)
                ? SelectedOutputDevice : null;

    public bool IsVisualResponseVisible =>
        IsCallVisualOverrideActive
        || (CompletedTaskResponseVisual ?? (EffectiveResponseMode != ResponseOutputMode.VoiceOnly))
        || forceVisualResponse
        || IsSpeechOutputVisualFallbackRequired;

    private bool IsSpeechOutputVisualFallbackRequired =>
        !IsSpeechOutputAvailable;

    public bool IsSpeechResponseEnabled =>
        IsMandatorySpeechResponseEligible
        && EffectiveResponseMode != ResponseOutputMode.VisualOnly
        && !IsCallVisualOverrideActive;

    private bool IsMandatorySpeechResponseEligible =>
        !conversationSearchPresentation
        && IsCallMutationHostEligible && !IsPrivacyPresentationHeld && sessionController.IsCurrentSessionUnlocked()
        && privacyObservation.Current.SessionState == WindowsSessionState.Unlocked
        && (!voiceRecognition.IsListening || IsExactRetainedPlaybackWakeCapture)
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
                OnPropertyChanged(nameof(CanPushToTalk));
                ToggleListeningCommand.NotifyCanExecuteChanged();
                BeginPushToTalkCommand.NotifyCanExecuteChanged();
                NotifyOutputPolicyChanged();
            }
        }
    }

    public bool IsCallDetected =>
        communicationPolicy.Current.EffectiveState is CallState.Active or CallState.Suspected;

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
                OnPropertyChanged(nameof(CanPushToTalk));
                ToggleListeningCommand.NotifyCanExecuteChanged();
                BeginPushToTalkCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsCallVisualOverrideActive =>
        communicationPolicy.Current.SuppressSpeech;

    public bool IsVoiceActivationAvailable =>
        isAssistantNameAvailable && communicationPolicy.Current.AllowActivation;

    public string CallVisualOverrideButtonText => ShowVisualTextDuringCalls
        ? "Disable call speech suppression"
        : "Show visual text during calls";

    public string CallVisualOverrideStatus => ShowVisualTextDuringCalls
        ? $"On · {AssistantName} stays quiet during protected calls and shows the full response on screen"
        : "Off · other call, audio and privacy protections still apply";

    public string CallVoiceActivationButtonText => AllowVoiceActivationDuringCalls
        ? "Disable voice activation during calls"
        : "Keep voice activation during calls";

    public string CallVoiceActivationStatus => AllowVoiceActivationDuringCalls
        ? $"On · {AssistantName} can continue listening during detected calls"
        : "Off · automatic listening and question replies pause during detected calls; deliberate held push-to-talk remains separately gated";

    public string CallStateStatus => (IsManualCallActive ? "Manual call mode is active. " : string.Empty) + (AutomaticCallState switch
    {
        CallState.Active => "A call is active.",
        CallState.Suspected => "Call activity is suspected.",
        CallState.Clear => "No call is currently detected.",
        CallState.Unknown => "Call detection is enabled but its current state is unknown.",
        CallState.Unavailable => "Automatic call detection is unavailable.",
        _ => "Automatic call observation is invalid; protection remains active.",
    });

    public string ResponseOutputStatus
    {
        get
        {
            var scope = IsInCallFeedbackOverrideApplied
                ? "In-call feedback override"
                : TaskResponseMode is not null
                ? "Current task override"
                : QueueResponseMode is not null
                    ? "Current queue override"
                    : "Device default";
            var fallback = IsVisualResponseVisible
                           && !IsCallVisualOverrideActive
                           && EffectiveResponseMode == ResponseOutputMode.VoiceOnly
                ? " The full response is shown on screen because speech is unavailable or failed."
                : string.Empty;
            var callOverride = IsCallVisualOverrideActive
                ? $" {AssistantName} stays quiet during this call and shows the full response on screen. Listening has its own setting."
                : string.Empty;
            var modeLabel = ResponseModeOptions.Single(
                option => option.Mode == EffectiveResponseMode).Label;
            return $"{scope}: {modeLabel}.{callOverride}{fallback} {spokenSummaryRecovery}".TrimEnd();
        }
    }

    public AssistantState State
    {
        get => state;
        private set => SetProperty(ref state, value);
    }

    public string ResponseTitle
    {
        get => responseTitle;
        private set
        {
            RetireCurrentResponseSpeech();
            if (SetProperty(ref responseTitle, value)) { RetireSpeechCaptionSource(); RetireWebResultDetails(retireControl: false); }
        }
    }

    public string ResponseBody
    {
        get => responseBody;
        private set
        {
            RetireCurrentResponseSpeech();
            Interlocked.Increment(ref webResponseRevision);
            conversationSearchPresentation = false;
            sessionPresentationSources.Clear();
            if (SetProperty(ref responseBody, value)) { RetireSpeechCaptionSource(); RetireWebResultDetails(retireControl: false); }
        }
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
                Interlocked.Increment(ref attachmentCatalogueComposerRevision);
                Interlocked.Increment(ref evidenceTraceInputRevision);
                Interlocked.Increment(ref conversationDispatchInputRevision);
                RefreshArtifactCommandOptions();
                RunTypedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsArtifactCommandDropdownVisible => ArtifactCommandOptions.Count > 0;

    public void ApplyArtifactCommandOption(ArtifactCommandOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (!ArtifactCommandOptions.Contains(option))
        {
            return;
        }
        CommandText = option.Command + " ";
    }

    public void DismissArtifactCommandOptions()
    {
        ArtifactCommandOptions.Clear();
        OnPropertyChanged(nameof(IsArtifactCommandDropdownVisible));
    }

    private void RefreshArtifactCommandOptions()
    {
        ArtifactCommandOptions.Clear();
        foreach (var option in FilterArtifactCommandOptions(CommandText, artifactCatalogue.Artifacts))
        {
            ArtifactCommandOptions.Add(option);
        }
        OnPropertyChanged(nameof(IsArtifactCommandDropdownVisible));
    }

    internal static IReadOnlyList<ArtifactCommandOption> FilterArtifactCommandOptions(
        string input,
        IReadOnlyList<ArtifactDefinition> artifacts)
    {
        if (!input.StartsWith('/'))
        {
            return [];
        }
        var query = input[1..];
        ArtifactKind? kind = null;
        foreach (var candidate in Enum.GetValues<ArtifactKind>())
        {
            var prefix = candidate.ToString().ToLowerInvariant();
            if (query.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                query = query[(prefix.Length + 1)..];
                break;
            }
        }
        if (query.Any(char.IsWhiteSpace))
        {
            return [];
        }
        return artifacts
            .Where(artifact => (kind is null || artifact.Kind == kind)
                && artifact.CommandName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(artifact => artifact.CommandName, StringComparer.OrdinalIgnoreCase)
            .Select(ArtifactCommandOption.From)
            .ToArray();
    }

    private bool IsSetupStatusCommand() =>
        commandRouter.Match(CommandText, AssistantName).Command?.Action
            is BuiltInAction.ShowStatus or BuiltInAction.ShowCurrentTaskProgress;

    public bool IsListening
    {
        get => isListening;
        private set
        {
            if (SetProperty(ref isListening, value))
            {
                OnPropertyChanged(nameof(ListeningButtonText));
                OnPropertyChanged(nameof(ListeningStatus));
                OnPropertyChanged(nameof(CanPushToTalk));
                BeginPushToTalkCommand.NotifyCanExecuteChanged();
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
            // Logical completion cannot withdraw Stop while the provider still owns output.
            value |= textToSpeech.IsSpeaking;
            if (SetProperty(ref isSpeaking, value))
            {
                if (!value)
                {
                    SetSpeechPlaybackFrame(SpeechPlaybackFrame.Inactive);
                }
                StopSpeechCommand.NotifyCanExecuteChanged();
                PreviewVoiceCommand.NotifyCanExecuteChanged();
                NotifyResponseReadChanged();
                RemoveSpeechProviderCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanRemoveSpeechProvider));
                OnPropertyChanged(nameof(IsCancelTaskVisible));
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
                Interlocked.Increment(ref responseReadOperationRevision);
                ToggleListeningCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanPushToTalk));
                BeginPushToTalkCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                RunTypedCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanInstallLocalModel));
                OnPropertyChanged(nameof(CanInstallPowerShell));
                PreviewVoiceCommand.NotifyCanExecuteChanged();
                ApplyAssistantNameCommand.NotifyCanExecuteChanged();



            }
        }
    }

    public bool HasVoiceConsent => voiceConsent == true;

    public bool NeedsVoiceConsent => voiceConsent is null;

    public bool IsVoiceEnabled => Volatile.Read(ref voiceEnabled) != 0;

    public bool IsPushToTalkVisible => !disposed;

    public bool CanPushToTalk =>
        captureOpenCancellation is null
        && Volatile.Read(ref listeningStopRequests) == 0
        && !CanRetireWakeForManualInput && listeningStop.CanRecover
        && IsVoiceReadinessEligible(privacyObservation.Current, pushToTalk: true)
        && IsHostInputEligible
        && microphoneCatalogCurrent
        && !IsListening
        && (!IsBusy || IsSpeaking);

    public long MicrophoneTopologyRevision => Interlocked.Read(ref microphoneTopologyRevision);

    public string VoiceConsentDescription =>
        "Your listening permission is saved for your Windows account on this device. Voice commands are processed on this device. "
        + $"Wake-word listening is not available: {AssistantName} does not record or transcribe background audio. "
        + "Use push-to-talk in the response window or Hold to speak in the foreground Customizations editor when offered; Settings only manages listening permission and readiness. "
        + "Disable listening turns automatic listening off; deliberate held push-to-talk still requires consent and all other microphone checks. Remove listening permission also keeps it off after a restart. "
        + "Unlocking can restore push-to-talk readiness after safety checks, but never starts recording. "
        + "After a disconnect, sleep or loss of microphone access, enable listening again.";

    public string ListeningButtonText => IsVoiceEnabled ? "Disable listening" : "Enable listening";

    public string ListeningStatus => ActiveHandsFreeVoiceStatus ?? (IsListening
        ? SelectedMicrophone is { } activeMicrophone
            ? $"Recording on {activeMicrophone.Name}"
            : "Recording · checking microphone selection"
        : IsVoiceEnabled
            ? SelectedMicrophone is { } readyMicrophone
                ? $"Push-to-talk ready on {readyMicrophone.Name} · microphone off"
                : "Microphone closed · selected microphone is unavailable"
        : !HasVoiceConsent
            ? "Microphone closed · listening permission not granted"
        : IsMicrophoneAccessDenied
            ? "Microphone closed · Windows access is blocked"
            : listeningPauseSessionState is { } sessionState
                ? $"Microphone closed · Windows session is {sessionState}; use Enable listening"
                : listeningPauseReason
              ?? (IsVoiceActivationAvailable
                  ? "Microphone closed"
                  : "Microphone closed · voice activation paused during detected call"));

    public async Task InitializeAsync()
    {
        isInitializing = true;
        try
        {
            if (markdownDisplay is not null)
            {
                try { await markdownDisplay.ObserveAsync(CancellationToken.None); }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                    or InvalidOperationException or OperationCanceledException)
                { ApplicationLog.Error(logger, exception, "Reading Markdown display preference"); }
            }
            if (communicationActivity is not null)
            {
                try { communicationActivity.ObservePreferences(); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { ApplicationLog.Error(logger, exception, "Reading communication activity selection"); }
            }
            var isFirstLaunch = false;
            try
            {
                voiceConsent = voiceConsentPreferences.Load();
                isFirstLaunch = voiceConsent is null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                ApplicationLog.Error(logger, exception, "Loading ongoing voice consent");
                voiceConsent = false;
                HoldVoiceInput("Microphone closed · saved consent could not be read");
                await RefreshAsync();
                ShowFailure("Voice consent could not be read.", exception.Message);
                voiceStartupApplied = true;
                return;
            }
            OnPropertyChanged(nameof(HasVoiceConsent));
            OnPropertyChanged(nameof(NeedsVoiceConsent));
            await RefreshAsync();
            if (voiceStartupApplied)
            {
                return;
            }
            voiceStartupApplied = true;
            if (State != AssistantState.Failure
                && EffectiveMicrophone is not null
                && !IsVoiceEnabled && HasVoiceConsent)
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
                        $"{AssistantName} could not confirm that the Windows session is unlocked. Use Enable listening after returning to an unlocked interactive session.");
                    return;
                }

                await StartListeningAsync();
            }
            else if (!HasVoiceConsent)
            {
                if (isFirstLaunch)
                {
                    ShowFirstLaunchGreeting();
                }
                else
                {
                    ShowInformation("Voice consent is required.", VoiceConsentDescription);
                }
            }
        }
        finally
        {
            isInitializing = false;
            UpdateSpeechPreparation();
        }
    }

    private void ShowFirstLaunchGreeting()
    {
        var addressName = currentUserNameProvider.GetAddressName();
        var title = string.IsNullOrWhiteSpace(addressName)
            ? $"Hi, I'm {AssistantName}."
            : $"Hi {addressName}, I'm {AssistantName}.";
        var body = EffectiveMicrophone is null
            ? "To talk with me, connect a microphone and grant explicit voice consent in Settings > Speech & audio. "
                + "You can continue without voice and type commands instead."
            : IsMicrophoneAccessDenied
                ? "To talk with me, enable Windows microphone access and grant explicit voice consent in Settings > Speech & audio. "
                    + "The microphone stays closed until you use push-to-talk. You can also continue without voice."
                : MicrophoneAccessStatus.State == MicrophoneAccessState.Unknown
                    ? "To talk with me, confirm Windows microphone access and grant explicit voice consent in Settings > Speech & audio. "
                        + "The microphone stays closed until you use push-to-talk. You can also continue without voice."
                    : "To talk with me, grant explicit voice consent in Settings > Speech & audio. "
                        + "The microphone stays closed until you use push-to-talk. You can also continue without voice.";
        ShowInformation(title, body + Environment.NewLine + Environment.NewLine + LocalStorageDisclosure);
        SetResponseAction(new ResponseAction(
            ResponseActionKind.OpenVoiceSettings,
            "Review voice settings"));
    }

    public Task ExecuteResponseActionAsync(ResponseAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!ResponseActions.Any(candidate => ReferenceEquals(candidate, action)))
        {
            return Task.CompletedTask;
        }

        ClearResponseActions();
        switch (action.Kind)
        {
            case ResponseActionKind.OpenVoiceSettings:
                RequestVoiceRecovery();
                break;
            default:
                throw new InvalidOperationException($"Unknown response action: {action.Kind}.");
        }

        return Task.CompletedTask;
    }

    private void SetResponseAction(ResponseAction action)
    {
        ResponseActions = [action];
        OnPropertyChanged(nameof(ResponseActions));
        OnPropertyChanged(nameof(HasResponseActions));
        forceVisualResponse = true;
        NotifyOutputPolicyChanged();
    }

    private void ClearResponseActions()
    {
        if (ResponseActions.Count == 0)
        {
            return;
        }

        ResponseActions = [];
        OnPropertyChanged(nameof(ResponseActions));
        OnPropertyChanged(nameof(HasResponseActions));
        forceVisualResponse = ShouldForceVisualResponse(
            IsResponseInteractionPending, State);
        NotifyOutputPolicyChanged();
    }

    public async Task DetectMicrophonesAsync() => await RefreshAsync();

    public OptionalSpeechProviderOffer? GetOptionalSpeechProviderOffer()
    {
        try
        {
            var offerState = optionalSpeechOfferPreferences.Load();
            var missingSelected = savedSpeechProviderIdForOffer is { } savedId
                && !string.Equals(savedId, SpeechProviderIds.Windows, StringComparison.Ordinal)
                ? SpeechProviders.FirstOrDefault(provider =>
                    string.Equals(provider.Id, savedId, StringComparison.Ordinal))
                : null;
            if (savedSpeechProviderIdForOffer is { } previousId
                && !string.Equals(previousId, SpeechProviderIds.Windows, StringComparison.Ordinal)
                && (missingSelected is null || !missingSelected.IsBuiltIn && !missingSelected.IsInstalled)
                && !string.Equals(
                    offerState.MissingProviderNotified,
                    previousId,
                    StringComparison.Ordinal))
            {
                var providerName = missingSelected?.Name ?? "selected speech provider";
                return new OptionalSpeechProviderOffer(
                    $"{providerName} needs attention.",
                    $"Your previously selected {providerName} is no longer available or its assets are incomplete. Would you like to review Speech and audio settings? {AssistantName} will not download or replace it automatically.",
                    previousId,
                    IsRecovery: true);
            }

            if (missingSelected is { IsInstalled: true }
                && offerState.MissingProviderNotified is not null)
            {
                offerState = offerState with { MissingProviderNotified = null };
                optionalSpeechOfferPreferences.Save(offerState);
            }

            if (offerState.InitialOfferHandled)
            {
                return null;
            }

            var optional = SpeechProviders.FirstOrDefault(provider =>
                !provider.IsBuiltIn && !provider.IsInstalled);
            if (optional is not null)
            {
                return new OptionalSpeechProviderOffer(
                    $"Optional {optional.Name} speech is available.",
                    $"{optional.Name} offers another local voice option. Would you like to review its download in Speech and audio settings? This is optional and will not be added to the setup queue or downloaded unless you choose it.",
                    optional.Id,
                    IsRecovery: false);
            }

            if (SpeechProviders.Any(provider => !provider.IsBuiltIn))
            {
                optionalSpeechOfferPreferences.Save(offerState with { InitialOfferHandled = true });
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            ApplicationLog.Error(logger, exception, "Loading optional speech offer state");
            ShowFailure("Speech offer settings could not be loaded.", exception.Message);
            return null;
        }
    }

    public void AcknowledgeOptionalSpeechProviderOffer(OptionalSpeechProviderOffer offer, bool openSettings)
    {
        ArgumentNullException.ThrowIfNull(offer);
        try
        {
            var offerState = optionalSpeechOfferPreferences.Load();
            optionalSpeechOfferPreferences.Save(new OptionalSpeechOfferState(
                InitialOfferHandled: true,
                MissingProviderNotified: offer.IsRecovery ? offer.ProviderId : offerState.MissingProviderNotified));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            ApplicationLog.Error(logger, exception, "Saving optional speech offer response");
            ShowFailure("Speech offer response could not be saved.", exception.Message);
            return;
        }

        if (openSettings)
        {
            var provider = SpeechProviders.FirstOrDefault(item =>
                string.Equals(item.Id, offer.ProviderId, StringComparison.Ordinal));
            if (provider is not null)
            {
                SelectedSpeechProvider = provider;
            }
            ShowSettings();
        }
    }

    public async Task InstallLocalModelAsync()
    {
        if (!CanInstallLocalModel)
        {
            throw new InvalidOperationException("Local model setup cannot run while another setup check is active.");
        }

        if (IsResponseInteractionPending)
        {



        }

        LocalModelSetupStatus = "Preparing approved Ollama and model installation.";
        LocalModelSetupProgress = 0;
        IsLocalModelSetupProgressIndeterminate = true;
        using var cancellation = new CancellationTokenSource();
        var eligible = CaptureConfigurationControlEligibility(OriginalOrigin());
        localModelCancellation = cancellation;
        IsLocalModelSetupActive = true;
        var audit = CreateAudit(
            SecurityAuditCategory.ResourceWrite,
            LocalModelInstallAction,
            SecurityAuditInitiator.LocalUser,
            "local.inference");
        try
        {
            await WriteAuditAsync(audit);
            if (!eligible())
            {
                await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "setup-admission-changed");
                return;
            }
            var progress = new DispatcherProgress<LocalModelSetupProgress>(uiDispatcher, update =>
            {
                if (!string.Equals(
                    dependencyBootstrapper.Tasks.ActiveTask?.Id,
                    DependencySetupWorkflow.LocalModelTaskId,
                    StringComparison.Ordinal))
                {
                    return;
                }

                LocalModelSetupStatus = update.Detail;
                LocalModelSetupProgress = update.Percentage ?? 0;
                IsLocalModelSetupProgressIndeterminate = update.Percentage is null;
                dependencyBootstrapper.Tasks.Update(
                    DependencySetupWorkflow.LocalModelTaskId,
                    SetupTaskState.Running,
                    update.Detail,
                    update.Percentage);
            });
            var result = await dependencySetup.InstallLocalModelAsync(
                progress,
                cancellation.Token);
            ApplyDependencyStatuses(result.Statuses);
            LocalModelSetupStatus = result.Inference.Detail;
            LocalModelSetupProgress = 100;
            IsLocalModelSetupProgressIndeterminate = false;
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Succeeded);
            ShowInformation("Local model is ready.", result.Inference.Detail);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            LocalModelSetupStatus = "Local model setup was cancelled.";
            IsLocalModelSetupProgressIndeterminate = false;
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Cancelled, "user-cancelled");
            ShowInformation("Setup cancelled.", "Local model installation was stopped; refresh readiness to check partial progress.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or JsonException or Win32Exception)
        {
            LocalModelSetupStatus = $"Setup failed: {exception.Message}";
            IsLocalModelSetupProgressIndeterminate = false;
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "setup-failed");
            ApplicationLog.Error(logger, exception, "Installing local model dependencies");
            ShowFailure("Local model setup failed.", exception.Message);
        }
        finally
        {
            localModelCancellation = null;
            IsLocalModelSetupActive = false;
        }
    }

    public async Task InstallPowerShellAsync()
    {
        if (!CanInstallPowerShell)
        {
            throw new InvalidOperationException("PowerShell setup cannot run while another task is active.");
        }

        using var cancellation = new CancellationTokenSource();
        var eligible = CaptureConfigurationControlEligibility(OriginalOrigin());
        powerShellSetupCancellation = cancellation;
        IsPowerShellSetupActive = true;
        var audit = CreateAudit(
            SecurityAuditCategory.ResourceWrite,
            PowerShellInstallAction,
            SecurityAuditInitiator.LocalUser,
            dependencySetup.PowerShellTaskId);
        try
        {
            await WriteAuditAsync(audit);
            if (!eligible())
            {
                await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "setup-admission-changed");
                return;
            }
            var progress = new DispatcherProgress<string>(
                uiDispatcher,
                value => PowerShellSetupStatus = value);
            var status = await dependencySetup.InstallPowerShellAsync(
                progress,
                cancellation.Token);

            ApplyPowerShellStatus(status);
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Succeeded);
            ShowInformation("PowerShell 7 is ready.", status.Detail);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Cancelled, "user-cancelled");
            ShowInformation("PowerShell setup cancelled.", "Refresh readiness to check partial installation.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "setup-failed");
            ApplicationLog.Error(logger, exception, "Installing PowerShell 7");
            ShowFailure("PowerShell setup failed.", exception.Message);
        }
        finally
        {
            powerShellSetupCancellation = null;
            IsPowerShellSetupActive = false;
        }
    }

    public void ShowApplication()
    {
        if (!TryRecoverPrivatePresentation()) { return; }
        if (State == AssistantState.Hidden)
        {
            State = AssistantState.Information;
        }

        ShowPresentation();
        ResponseRestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    public void HideApplication() => new AsyncCommand(HideApplicationAsync, HandleCommandException).Execute(null);

    public Task HideApplicationAsync()
    {
        CancelStartupPresentationReview();
        Interlocked.Increment(ref approvalPreparationRevision);
        RetireWebResultDetails();


        ClearResponseActions();

        State = AssistantState.Hidden;
        HidePresentation();
        return Task.CompletedTask;
    }

    public void ShowPresentation()
    {
        if (CanRevealPrivatePresentation)
        {
            WindowActionRequested?.Invoke(this, WindowAction.Show);
        }
    }

    public void HidePresentation()
    {
        RetireCurrentResponseSpeech();
        RetireWebResultDetails();
        WindowActionRequested?.Invoke(this, WindowAction.Hide);
    }

    public void NotifyPresenceInteraction() =>
        WindowActionRequested?.Invoke(this, WindowAction.ShowPresence);

    public void ReportPresenceInputFailure(string detail) =>
        ShowFailure("Presence mouse routing is unavailable.",
            $"The presence has been hidden to avoid blocking other windows. Restart {AssistantName} to try again. {detail}");

    public void ReportHostInteractionFailure(string detail) =>
        ShowFailure("Native question unavailable.", detail);

    public void ShowSettings()
    {
        if (!TryRecoverPrivatePresentation()) { return; }
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowReadiness() => ReadinessRequested?.Invoke(this, EventArgs.Empty);

    public void ShowDocumentation() =>
        DocumentationRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? SessionsRequested;

    public void ShowSessions()
    {
        if (!TryRecoverPrivatePresentation()) { return; }
        SessionsRequested?.Invoke(this, EventArgs.Empty);
    }

    private Task SubmitModelQuestionAnswerAsync() => HandleUnmatchedRequestAsync();

    public async Task ExitAsync()
    {
        if (voluntaryShutdownRunning || hostExitRequested) { return; }
        voluntaryShutdownRunning = true;
        quarantinedExitRequest = IsPrivacyPresentationHeld
            && HostActivity.Current is { Request: { Origin: RequestOrigin.LocalUi, InvocationId: null } request }
                ? request : null;
        SetApplicationExitStatus("Exit requested - waiting for input, submitted-work and resource checks.");
        var closeDispatched = false;
        try
        {
            if (bundledActionShutdown is not null) { await bundledActionShutdown(); }
            if (ownedSkillAuthoringShutdown is not null && !await ownedSkillAuthoringShutdown()) { return; }
            var conversationCurrent = await PrepareConversationShutdown();
            if (conversationCurrent is null) { return; }
            using var submitted = await PrepareSubmittedWorkShutdown(Hosting.ApplicationLifecycleOperation.Exit, conversationCurrent);
            if (submitted is null) { return; }
            if (instructionEditorShutdown is not null && !await instructionEditorShutdown()) { return; }
            if (customizationShutdown is not null && !await customizationShutdown()) { return; }
            if (!RequireConversationShutdownCurrent(() => conversationCurrent() && submitted.IsCurrent)) { return; }
            if (!await JoinSubmittedWorkShutdown(submitted)) { return; }
            await CloseVoluntaryOperationsAsync();
            if (!RequireConversationShutdownCurrent(conversationCurrent)) { return; }
            ApplicationLog.Information(logger, "Kora exit was requested");
            hostExitRequested = true;
            lifecycleAdmissionClosed = true;
            await JoinVoluntaryPreparationAsync();
            RetireWebResultDetails();
            await StopWebPageRetrievalAsync();
            await StopWebSummary();
            ClearClipboardPreview();
            ClearFilePreview();
            HoldVoiceInput("Microphone closed · application exiting");
            textToSpeech.InvalidateOutput();
            try
            {
                await StopAudioAsync();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ApplicationLog.Error(logger, exception, "Stopping audio during application exit");
            }
            await JoinVolatileContextAsync();
            if (!RequireConversationShutdownCurrent(() => conversationCurrent() && submitted.IsCurrent)) { return; }
            closeDispatched = true;
            WindowActionRequested?.Invoke(this, WindowAction.Close);
        }
        finally
        {
            quarantinedExitRequest = null;
            if (!closeDispatched)
            {
                SetApplicationExitStatus("Exit not completed - input, submitted work or required review remains unavailable.");
                ApplicationLog.Information(logger, "Application exit did not complete; input or work admission remains unconfirmed");
            }
            if (!closeDispatched) { hostExitRequested = false; lifecycleAdmissionClosed = false; }
            voluntaryShutdownRunning = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            ShowInformation("Readiness check is busy.", "Wait for the current operation to finish before refreshing readiness.");
            return;
        }

        if (IsPowerShellSetupActive)
        {
            ShowInformation("PowerShell setup is running.", "Wait for setup to finish before refreshing readiness.");
            return;
        }
        if (IsLocalModelSetupActive)
        {
            ShowInformation("Local model setup is running.", "Wait for setup to finish before refreshing readiness.");
            return;
        }


        ApplicationLog.Debug(logger, "Refreshing devices, preferences, and dependency readiness");
        IsBusy = true;
        try
        {
            var statuses = await dependencyBootstrapper.ProbeAsync();
            ApplyDependencyStatuses(statuses);
            dependencyBootstrapper.Tasks.Remove("environment.check");
            OnPropertyChanged(nameof(SetupTasks));

            MicrophoneAccessStatus = microphoneAccessService.GetStatus();
            assistantNameConfiguration.Reload();
            SynchronizeAssistantNameConfiguration();
            if (!isAssistantNameAvailable)
            {
                throw new InvalidDataException(assistantNameConfiguration.Get().Recovery);
            }

            if (preapprovedUriList is not null)
            {
                PreapprovedUriPatterns = preapprovedUriList.Execute().Patterns;
                OnPropertyChanged(nameof(PreapprovedUriStatus));
            }

            var modelExecution = modelExecutionPreferences.Load();
            SetProperty(
                ref localModelsEnabled,
                modelExecution.LocalModelsEnabled,
                nameof(LocalModelsEnabled));
            SetProperty(
                ref hostedModelsEnabled,
                modelExecution.HostedModelsEnabled,
                nameof(HostedModelsEnabled));
            OnPropertyChanged(nameof(HostedModelExecutionDescription));

            responseWindowPreferencesUnconfirmed = true;
            var savedPresencePosition =
                appearancePreferences.LoadPresencePosition();
            var savedResponseWindowSettings = appearancePreferences.LoadResponseWindowSettings();
            appearanceConfiguration.Reload();
            SynchronizeAppearance();
            suppressPresenceAppearancePreferenceSave = true;
            suppressResponseWindowPreferenceSave = true;
            try
            {
                SetProperty(
                    ref presencePosition,
                    savedPresencePosition,
                    nameof(PresencePosition));
                ApplyResponseWindowSettings(savedResponseWindowSettings ?? ResponseWindowSettings.Default);
                responseWindowPreferencesUnconfirmed = false;
            }
            finally
            {
                suppressPresenceAppearancePreferenceSave = false;
                suppressResponseWindowPreferenceSave = false;
            }

            microphoneCatalogCurrent = false;
            var savedMicrophoneId = inputDevicePreferences.Load();
            suppressAudioDevicePreferenceSave = true;
            try
            {
                SelectedMicrophone = savedMicrophoneId is null
                    || string.Equals(savedMicrophoneId, SystemAudioDevices.Microphone.Id, StringComparison.Ordinal)
                    ? SystemAudioDevices.Microphone : new MicrophoneDevice(savedMicrophoneId, "Unavailable saved microphone");
            }
            finally { suppressAudioDevicePreferenceSave = false; }
            var microphones = voiceRecognition.GetMicrophones();
            Interlocked.Increment(ref microphoneTopologyRevision);
            OnPropertyChanged(nameof(MicrophoneTopologyRevision));
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
            var speechProviders = textToSpeech.GetProviders();
            SpeechProviders.Clear();
            foreach (var provider in speechProviders)
            {
                SpeechProviders.Add(provider);
            }

            var outputWork = Task.Run(async () =>
            {
                var snapshot = await outputCatalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                var savedId = audioDevicePreferences.LoadOutputDeviceId();
                outputConfiguration?.Observe(snapshot);
                return (Snapshot: snapshot, SavedId: savedId);
            });
            outputDiscoveryPersistence = outputWork;
            var outputObservation = await outputWork;
            if (disposed || lifecycleAdmissionClosed) { return; }
            var savedOutputDeviceId = outputObservation.SavedId;
            var outputDevices = outputObservation.Snapshot.Devices;
            OutputDevices.Clear();
            OutputDevices.Add(SystemAudioDevices.Output);
            foreach (var outputDevice in outputDevices)
            {
                OutputDevices.Add(outputDevice);
            }

            systemDefaultOutputDevice = outputObservation.Snapshot.Default;
            var savedOutputDevice = OutputDevices.FirstOrDefault(
                item => string.Equals(item.Id, savedOutputDeviceId, StringComparison.Ordinal));
            var savedOutputDeviceUnavailable =
                savedOutputDeviceId is not null && savedOutputDevice is null;
            suppressAudioDevicePreferenceSave = true;
            try
            {
                SelectedMicrophone = savedMicrophoneId is not null
                    ? savedMicrophone ?? new MicrophoneDevice(savedMicrophoneId, "Unavailable saved microphone")
                    : SystemAudioDevices.Microphone;
                SetOutputDeviceSnapshot(savedOutputDeviceId is not null
                    ? savedOutputDevice ?? (outputConfiguration is not null
                        ? new AudioOutputDevice(savedOutputDeviceId, "Unavailable saved output") : null)
                    : SystemAudioDevices.Output);
            }
            finally
            {
                suppressAudioDevicePreferenceSave = false;
            }

            catalogPrivacyRevision = privacyObservation.Current.TopologyRevision;
            Interlocked.Exchange(ref observedTopologyRevision, catalogPrivacyRevision);
            microphoneCatalogCurrent = true;
            UpdateMicrophoneAvailability(savedMicrophoneUnavailable);
            UpdateOutputDeviceAvailability(savedOutputDeviceUnavailable);
            ToggleListeningCommand.NotifyCanExecuteChanged();
            PreviewVoiceCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsSpeechOutputAvailable));
            NotifyOutputPolicyChanged();

            speechConfiguration.Reload();
            var savedSpeechProviderId = speechConfiguration.Get().Selection?.ProviderId;
            savedSpeechProviderIdForOffer = savedSpeechProviderId;
            responseModeConfiguration?.Observe();
            var savedResponseMode = responseModeConfiguration is null
                ? responseOutputPreferences.LoadDefaultMode() : responseModeConfiguration.Get().Desired;
            var savedMutedOutputFallback = responseOutputPreferences.LoadMutedOutputVisualFallback();
            var savedCallAwareSettings = callAwarePreferences.Load() ?? CallAwareSettings.Default;
            communicationPolicy.LoadSettings(savedCallAwareSettings);
            suppressResponseModeSave = true;
            try
            {
                DefaultResponseMode = savedResponseMode ?? ResponseOutputMode.Hybrid;
                FallbackToVisualWhenOutputMuted = savedMutedOutputFallback ?? true;
                ShowVisualTextDuringCalls = savedCallAwareSettings.ShowVisualTextDuringCalls;
                AllowVoiceActivationDuringCalls = savedCallAwareSettings.AllowVoiceActivationDuringCalls;
            }
            finally
            {
                suppressResponseModeSave = false;
            }

            CurrentCallState = communicationPolicy.Current.EffectiveState;
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
            suppressSpeechProviderPreferenceSave = true;
            try
            {
                SelectedSpeechProvider = preferredProvider;
                SynchronizeSpeechConfiguration();
            }
            finally
            {
                suppressSpeechProviderPreferenceSave = false;
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
                    _ => "With saved voice consent, safe startup enables push-to-talk. Ambient wake is unavailable; the microphone stays closed until explicit activation.",
                };
            if (speechConfiguration.Get().Recovery is { } speechRecovery)
            {
                if (Voices.Count > 0) { setupTitle = "Speech output is unavailable."; }
                setupBody += Environment.NewLine + speechRecovery;
            }
            PresentResponse(AssistantState.Information, setupTitle, setupBody, refreshOutput: false);
            OnPropertyChanged(nameof(MicrophoneTopologyRevision));
            ApplicationLog.EnvironmentRefreshCompleted(
                logger,
                microphones.Count,
                Voices.Count,
                outputDevices.Count);
        }
        catch (InvalidDataException exception)
        {
            RecordSetupCheckFailure(exception);
            ApplicationLog.Error(logger, exception, "Loading a saved preference");
            ShowFailure("A saved setting is invalid.", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordSetupCheckFailure(exception);
            ApplicationLog.Error(logger, exception, "Refreshing the environment because access was denied");
            ShowFailure("Storage or microphone access was denied.", exception.Message);
        }
        catch (IOException exception)
        {
            RecordSetupCheckFailure(exception);
            ApplicationLog.Error(logger, exception, "Refreshing the environment due to an I/O error");
            ShowFailure("Dependency probing failed.", exception.Message);
        }
        catch (AudioOutputDeviceUnavailableException exception)
        {
            RecordSetupCheckFailure(exception);
            ApplicationLog.Error(logger, exception, "Inspecting audio output");
            HandleAudioOutputFailure(exception, "Windows audio output is unavailable.");
        }
        catch (InvalidOperationException exception)
        {
            RecordSetupCheckFailure(exception);
            ApplicationLog.Error(logger, exception, "Refreshing speech services");
            SelectedVoice = null;
            ShowFailure("Speech services are unavailable.", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyDependencyStatuses(IEnumerable<DependencyStatus> statuses)
    {
        Dependencies.Clear();
        foreach (var status in statuses)
        {
            Dependencies.Add(status);
        }

        var localModel = Dependencies.FirstOrDefault(status =>
            string.Equals(status.Id, "local.inference", StringComparison.Ordinal));
        if (localModel is not null)
        {
            LocalModelSetupStatus = localModel.Detail;
        }

        var powerShell = Dependencies.FirstOrDefault(status =>
            string.Equals(status.Id, dependencySetup.PowerShellTaskId, StringComparison.Ordinal));
        if (powerShell is not null)
        {
            PowerShellSetupStatus = powerShell.Detail;
        }

        OnPropertyChanged(nameof(ShouldOfferLocalModelSetup));
        OnPropertyChanged(nameof(ShouldOfferPowerShellSetup));
    }

    private void ApplyPowerShellStatus(DependencyStatus status)
    {
        dependencyBootstrapper.RecordObservation(status);
        var previous = Dependencies.FirstOrDefault(item =>
            string.Equals(item.Id, status.Id, StringComparison.Ordinal));
        if (previous is null)
        {
            Dependencies.Add(status);
        }
        else
        {
            Dependencies[Dependencies.IndexOf(previous)] = status;
        }

        PowerShellSetupStatus = status.Detail;

        OnPropertyChanged(nameof(ShouldOfferLocalModelSetup));
        OnPropertyChanged(nameof(ShouldOfferPowerShellSetup));
    }

    private async Task ToggleListeningAsync()
    {
        if (IsVoiceEnabled)
        {
            await DisableListeningAsync();
            return;
        }

        var origin = OriginalOrigin();
        var callRevision = CallPolicyRevision;
        if (AdmitVoiceOptionMutation("configuration.listening-enabled"))
        {
            await StartListeningAsync(origin, callRevision, explicitListeningRecovery: true);
        }
    }

    private async Task StartListeningAsync(RequestOrigin? originalOrigin = null, long observedCallRevision = 0,
        bool explicitListeningRecovery = false)
    {
        IsBusy = true;
        try
        {
            var revision = Interlocked.Read(ref voiceRecoveryRevision);
            if (!RefreshVoiceReadiness())
            {
                HoldVoiceInput("Microphone closed · readiness/session gate failed");
                await StopListeningAsync();
                ShowFailure("Listening cannot be enabled.",
                    "Review voice consent, microphone permission, endpoint and unlocked Windows session, then explicitly enable listening.");
                return;
            }
            if (revision != Interlocked.Read(ref voiceRecoveryRevision))
            {
                ShowInformation("Readiness changed.", "Review the current blocker and enable listening again.");
                return;
            }
            if (originalOrigin is { } origin)
            {
                var denied = communicationPolicy.CheckMutation(origin, observedCallRevision, () => IsCallMutationHostEligible);
                if (denied is { } outcome)
                {
                    ReportCallMutation(outcome, "configuration.listening-enabled", origin);
                    return;
                }
            }
            if (!TryEnableVoiceReadiness(revision, explicitListeningRecovery: explicitListeningRecovery))
            {
                ShowInformation("Readiness changed.", "Review the current blocker and enable listening again.");
                return;
            }
            if (explicitListeningRecovery && originalOrigin == RequestOrigin.LocalUi)
            { Interlocked.Exchange(ref explicitVoiceActivationRevision, revision); }
            OnPropertyChanged(nameof(IsPrivacyPresentationHeld));
            NotifyVoiceEnablementChanged();
            SetListeningPauseReason(null);
            if (Volatile.Read(ref ordinaryResponse) is not null && !IsResponseInteractionPending)
            {
                Transcript = "Push-to-talk is ready. The microphone remains closed until you press and hold Push to talk. The current response remains unchanged.";
            }
            else
            {
                PresentResponse(
                    AssistantState.Information,
                    "Push-to-talk is ready.",
                    "The microphone remains closed until you press and hold Push to talk. Production wake detection is unavailable.");
            }
            IsBusy = false;
            await StartHandsFreeVoiceAsync(explicitListeningRecovery);
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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            HoldVoiceInput("Microphone closed · unexpected readiness failure");
            ApplicationLog.Error(logger, exception, "Starting voice activation unexpectedly");
            ShowFailure("Voice activation failed.", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopListeningAsync()
    {
        Interlocked.Exchange(ref acceptedTranscriptGeneration, -1);
        InvalidateOwnedVoiceCapture();
        await StopOwnedVoiceCaptureAsync();
        IsListening = false;
        ApplicationLog.Information(logger, "Voice activation stopped");
    }

    private async Task DownloadSpeechProviderAsync()
    {
        if (!AdmitVoiceOptionMutation(SpeechProviderSelectionConfigurationAction)) { return; }
        var provider = SelectedSpeechProvider!;
        var eligible = CaptureConfigurationControlEligibility(OriginalOrigin());
        var audit = CreateAudit(
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
            await WriteAuditAsync(audit);
            if (!eligible())
            {
                await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "provider-admission-changed");
                return;
            }
            await textToSpeech.InstallProviderAsync(
                provider.Id,
                progress);
            RefreshSpeechProviderCatalog(provider.Id);
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Succeeded);
            ShowInformation(
                $"{provider.Name} is ready.",
                "The local neural speech provider is available now. No restart is required.");
            await SpeakCurrentResponseAsync();
        }
        catch (HttpRequestException exception)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "download-failed");
            ApplicationLog.Error(logger, exception, "Downloading a speech provider");
            ShowFailure("The speech provider could not be downloaded.", exception.Message);
            await SpeakCurrentResponseAsync();
        }
        catch (InvalidDataException exception)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "validation-failed");
            ApplicationLog.Error(logger, exception, "Validating a downloaded speech provider");
            ShowFailure("The speech provider download was invalid.", exception.Message);
            await SpeakCurrentResponseAsync();
        }
        catch (UnauthorizedAccessException exception)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Installing a speech provider because access was denied");
            ShowFailure("The speech provider could not be installed.", exception.Message);
            await SpeakCurrentResponseAsync();
        }
        catch (IOException exception)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Installing a speech provider due to an I/O error");
            ShowFailure("The speech provider could not be installed.", exception.Message);
            await SpeakCurrentResponseAsync();
        }
        catch (InvalidOperationException exception)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "provider-unavailable");
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
        if (!AdmitVoiceOptionMutation(SpeechProviderSelectionConfigurationAction)) { return; }
        var provider = SelectedSpeechProvider!;
        var eligible = CaptureConfigurationControlEligibility(OriginalOrigin());
        var audit = CreateAudit(
            SecurityAuditCategory.ResourceWrite,
            SpeechProviderRemoveAction,
            SecurityAuditInitiator.LocalUser,
            provider.Id);
        IsSpeechProviderOperationActive = true;
        SpeechProviderOperationProgress = 0;
        SpeechProviderOperationStatus = $"Removing {provider.Name}.";
        try
        {
            await WriteAuditAsync(audit);
            if (!eligible())
            {
                await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "provider-admission-changed");
                return;
            }
            await textToSpeech.RemoveProviderAsync(provider.Id);
            RefreshSpeechProviderCatalog(provider.Id);
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Succeeded);
            ShowInformation(
                $"{provider.Name} was removed.",
                "Its downloaded model was deleted. The built-in Windows provider remains available.");
        }
        catch (UnauthorizedAccessException exception)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "access-denied");
            ApplicationLog.Error(logger, exception, "Removing a speech provider because access was denied");
            ShowFailure("The speech provider could not be removed.", exception.Message);
        }
        catch (IOException exception)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "io-error");
            ApplicationLog.Error(logger, exception, "Removing a speech provider due to an I/O error");
            ShowFailure("The speech provider could not be removed.", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "provider-busy");
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

    private void RecordSetupCheckFailure(Exception exception)
    {
        dependencyBootstrapper.Tasks.Reconcile(new DependencyStatus(
            "environment.check",
            "Check environment",
            DependencyReadiness.Failed,
            exception.Message));
        OnPropertyChanged(nameof(SetupTasks));
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
        try
        {
            SelectedSpeechProvider = SpeechProviders.FirstOrDefault(
                provider => string.Equals(
                    provider.Id,
                    providerId,
                    StringComparison.Ordinal));
            speechConfiguration.Reload();
            SynchronizeSpeechConfiguration();
        }
        finally
        {
            suppressSpeechProviderPreferenceSave = false;
        }
    }

    private async Task PreviewVoiceAsync()
    {
        if (!IsCallMutationHostEligible)
        {
            ApplicationLog.Information(logger, "Denied voice preview outside the eligible host generation");
            return;
        }
        if (textToSpeech.IsSpeaking)
        {
            PreserveUnconfirmedSpeechRelease();
            return;
        }
        if (communicationPolicy.Current.SuppressSpeech)
        {
            ShowInformation("Voice preview is suppressed.", "Protected call policy requires visual-only output.");
            return;
        }
        if (audioPlaybackSettings.VolumeState is { AllowsSpeech: false })
        {
            ShowInformation("Voice preview is unavailable.",
                $"{AssistantName} playback volume is zero or unavailable. Inspect volume status in Settings; changing it never replays stopped speech.");
            return;
        }
        if (!await RefreshOutputEndpointsAsync() || !IsCallMutationHostEligible) { return; }
        if (!IsSpeechOutputAvailable || SelectedVoice is not { } voice || SelectedOutputDevice is not { } outputDevice
            || EffectiveOutputDevice is not { IsMuted: false })
        {
            ApplicationLog.Information(logger, "Denied voice preview without an explicit voice and available output");
            ShowInformation("Voice preview is unavailable.",
                "Select an installed speech voice and an available audio output, then try again.");
            return;
        }
        var previewText = $"Hello, I'm {AssistantName}.";
        IsBusy = true;
        IsSpeaking = true;
        activeSpokenText = previewText;
        try
        {
            await communicationPolicy.StartSpeech(() => textToSpeech.SpeakAsync(
                previewText,
                voice,
                outputDevice), () => IsCallMutationHostEligible);
        }
        catch (OperationCanceledException)
        {
            ApplicationLog.Information(logger, "Voice preview was invalidated by a privacy transition");
        }
        catch (PlaybackVolumeUnavailableException exception)
        {
            ApplicationLog.Error(logger, exception, "Previewing with unavailable Kora playback volume");
            PreserveSpokenResponseFailure($"{AssistantName} playback volume is zero or unavailable. Full visual output is retained; refresh volume preferences.");
        }
        catch (WindowsSpeechRateUnavailableException exception)
        {
            ApplicationLog.Error(logger, exception, "Previewing with unavailable Windows speech rate");
            audioPlaybackSettings.HoldRateUnavailable();
            PreserveSpokenResponseFailure("Windows speech rate is unavailable. Full visual output is retained; explicitly refresh rate preferences.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ApplicationLog.Error(logger, exception, "Previewing the selected speech voice");
            ClearActiveSpeechVoice();
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
            ClearActiveSpeechVoice();
            ShowFailure("Text-to-speech is unavailable.", exception.Message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ApplicationLog.Error(logger, exception, "Previewing text-to-speech unexpectedly");
            ShowFailure("Voice preview failed.", exception.Message);
        }
        finally
        {
            activeSpokenText = null;
            IsSpeaking = textToSpeech.IsSpeaking;
            IsBusy = false;
            if (IsSpeaking) { PreserveUnconfirmedSpeechRelease(); }
        }
    }

    private void ConfirmSpeechOutputReleased()
    {
        IsSpeaking = textToSpeech.IsSpeaking;
        if (IsSpeaking)
        {
            throw new InvalidOperationException("Speech playback release remains unconfirmed. Use Stop speaking after output recovery; no replay is permitted.");
        }
        activeSpokenText = null;
    }

    private void PreserveUnconfirmedSpeechRelease()
    {
        IsSpeaking = true;
        ApplicationLog.Information(logger, "Speech playback release remains unconfirmed");
        PreserveSpokenResponseFailure("Speech playback release remains unconfirmed. Use Stop speaking after output recovery; the complete visual response is retained. No automatic retry.");
    }

    private async Task StopSpeakingAsync()
    {
        if (responseReadProposal is not null || responseReadPlanCancellation is not null)
        {
            await CloseResponseReadProposal();
            return;
        }
        RetireResponseReadPlayback();
        RetireSpeechCaption();
        try
        {
            await textToSpeech.StopAsync();
#pragma warning disable VSTHRD003 // Stop retires the owned read before replacing its source with recovery text.
            await responseReadQuiescence;
            await responseReadPlanQuiescence;
#pragma warning restore VSTHRD003
            ConfirmSpeechOutputReleased();
            activeCallSpeechLease = null;
            ShowInformation("Speech is stopped.", "No speech playback is active.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            IsSpeaking = textToSpeech.IsSpeaking;
            var reviewedCall = activeCallSpeechLease is not null;
            LogSpokenResponseFailure(exception, "Stopping speech output", reviewedCall);
            ShowFailure("Speech output could not be stopped.", reviewedCall
                ? "Reviewed call speech release remains unconfirmed. Use Stop speaking after output recovery; no replay is permitted."
                : exception.Message);
        }
    }

    private async Task StopAudioAsync()
    {
        RetireCurrentResponseSpeech();
        HoldVoiceInput("Microphone closed · host lifecycle stopped");
        textToSpeech.InvalidateOutput();
        var captureClosure = StopListeningAsync();
        Interlocked.Increment(ref stoppingAudioOperations);
        try
        {
            await Task.WhenAll(captureClosure, textToSpeech.StopAsync());
#pragma warning disable VSTHRD003 // Join the retired, independently owned one-shot before host resource handoff.
            await responseReadQuiescence;
            await responseReadPlanQuiescence;
#pragma warning restore VSTHRD003
#pragma warning disable VSTHRD003 // Await owned reply retirement asynchronously so no queued UI commit survives host handoff/close.
            if (questionVoiceReplies is not null) { await questionVoiceReplies.Quiescence; }
            await JoinCustomizationClarification();
            await JoinSessionRoutingVoice();
            if (composerDictation is not null) { await composerDictation.Quiescence; }
#pragma warning restore VSTHRD003
            activeSpokenText = null;
            IsSpeaking = false;

        }
        finally
        {
            Interlocked.Decrement(ref stoppingAudioOperations);
        }
    }

    private async Task ToggleCallVisualOverrideAsync()
    {
        await SetShowVisualTextDuringCallsAsync(!ShowVisualTextDuringCalls);
    }

    private async Task ToggleCallVoiceActivationAsync()
    {
        await SetAllowVoiceActivationDuringCallsAsync(!AllowVoiceActivationDuringCalls);
    }

    public Task SetShowVisualTextDuringCallsAsync(bool value) =>
        SetCallSettingsAsync(communicationPolicy.Current.Settings with { ShowVisualTextDuringCalls = value });

    public Task SetAllowVoiceActivationDuringCallsAsync(bool value) =>
        SetCallSettingsAsync(communicationPolicy.Current.Settings with { AllowVoiceActivationDuringCalls = value });

    private async Task SaveMutedOutputFallbackPreferenceAsync(bool value)
    {
        if (!AdmitVoiceOptionMutation(MutedOutputFallbackConfigurationAction)
            || value == fallbackToVisualWhenOutputMuted) { return; }
        if (!await SavePreferenceAsync(() => responseOutputPreferences.SaveMutedOutputVisualFallback(value),
            MutedOutputFallbackConfigurationAction, SecurityAuditInitiator.LocalUser, "muted-output visual fallback")) { return; }
        SetProperty(ref fallbackToVisualWhenOutputMuted, value, nameof(FallbackToVisualWhenOutputMuted));
        UpdateOutputDeviceAvailability();
        NotifyOutputPolicyChanged();
    }

    private async Task<bool> SetResponseWindowSettingsAsync(
        ResponseWindowSettings value,
        SecurityAuditInitiator initiator,
        Action<string, string>? reportFailure = null)
    {
        Action<string, string> notifyFailure = reportFailure ?? ShowFailure;
        try
        {
            ReadConfirmedResponseWindowSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException)
        {
            ApplicationLog.Error(logger, exception, "Confirming response-window preferences");
            notifyFailure("The response window settings could not be saved.", exception.Message);
            return false;
        }
        if (responseWindowSettings == value)
        {
            return true;
        }

        if (!suppressResponseWindowPreferenceSave
            && !await SavePreferenceAsync(() => appearancePreferences.SaveResponseWindowSettings(value),
                ResponseWindowConfigurationAction, initiator, "response window settings", reportFailure: reportFailure))
        {
            OnPropertyChanged(nameof(IsResponseAlwaysVisible));
            OnPropertyChanged(nameof(IsResponseWindowTopmost));
            OnPropertyChanged(nameof(ResponseWindowPosition));
            return false;
        }

        ApplyResponseWindowSettings(value);
        return true;
    }

    private void ApplyResponseWindowSettings(ResponseWindowSettings value)
    {
        responseWindowSettings = value;
        OnPropertyChanged(nameof(IsResponseAlwaysVisible));
        OnPropertyChanged(nameof(IsResponseWindowTopmost));
        OnPropertyChanged(nameof(ResponseWindowPosition));
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

    private void SetListeningPauseReason(string? value, WindowsSessionState? sessionState = null)
    {
        if (!string.Equals(listeningPauseReason, value, StringComparison.Ordinal)
            || listeningPauseSessionState != sessionState)
        {
            listeningPauseReason = value;
            listeningPauseSessionState = sessionState;
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
            not null when !Microphones.Contains(SelectedMicrophone) =>
                "The saved microphone is no longer available. Select another microphone.",
            not null => $"{SelectedMicrophone.Name} is selected for {AssistantName} capture.",
            null when selectedMicrophoneUnavailable =>
                "The saved microphone is no longer available. Select another microphone.",
            _ => "No Windows microphone is available. Connect or enable a microphone, then refresh.",
        };
    }

    private void UpdateOutputDeviceAvailability(bool selectedDeviceUnavailable = false)
    {
        if (outputConfiguration?.Get(CallPolicyRevision).Recovery is { } recovery)
        {
            OutputDeviceAvailabilityMessage = recovery;
            return;
        }
        const string mutedOutputStatus = "Visual text is forced. Acoustic audibility is not guaranteed.";
        OutputDeviceAvailabilityMessage = SelectedOutputDevice switch
        {
            { IsSystemDefault: true } when systemDefaultOutputDevice is { IsMuted: true } =>
                $"The Windows default audio output is muted or its volume is zero. {mutedOutputStatus}",
            { IsSystemDefault: true } when systemDefaultOutputDevice is not null =>
                $"System is selected and follows the Windows default audio output for {AssistantName} playback.",
            { IsSystemDefault: true } =>
                "System is selected, but Windows has no active default audio output. Visual text is forced.",
            { IsMuted: true } =>
                $"{SelectedOutputDevice.Name} is muted or its Windows volume is zero. {mutedOutputStatus}",
            not null => $"{SelectedOutputDevice.Name} is selected for {AssistantName} playback.",
            null when selectedDeviceUnavailable =>
                "The saved audio output device is no longer available. Select another device.",
            null when OutputDevices.Count == 0 =>
                "No Windows audio output device is available. Connect or enable speakers or headphones.",
            _ => "Select an audio output device to enable speech.",
        };
    }

    private async Task RunTypedCommandAsync()
    {
        if (retainedAnswerSpeech is not null && IsSpeechInterruptionCommand(CommandText))
        {
            await InterruptSessionSpeechAsync(CaptureSpeechInterruptionInput(), SpeechInterruptionChannel.Typed);
            return;
        }
        await HandleTranscriptAsync(
            CommandText,
            1,
            SecurityAuditInitiator.TypedCommand,
            foregroundTarget: Volatile.Read(ref foregroundConversationTarget));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "TranscriptRecognized requires a void event handler; failures are logged and presented by ShowFailure.")]
    private async void OnTranscriptRecognized(object? sender, VoiceTranscriptEventArgs eventArgs) =>
        await DispatchRecognizedVoiceTranscriptAsync(eventArgs);

    private async Task DispatchRecognizedVoiceTranscriptAsync(VoiceTranscriptEventArgs eventArgs, HostRequest? originalRequest = null)
    {
        var callCapture = Volatile.Read(ref pushToTalkCall);
        ActivityLink[] causes = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        using var activity = HostActivity.BeginRoot(originalRequest ?? HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request, causes);
        if (sessionRoutingVoiceReply is not null || !IsCurrentPushToTalkCall(callCapture)
            || eventArgs.Generation != Interlocked.Read(ref acceptedTranscriptGeneration)
            || eventArgs.Generation != voiceRecognition.Generation)
        {
            ApplicationLog.Debug(logger, "Discarded an unactivated or retired voice callback before UI dispatch");
            ApplicationLog.VoiceDispatchGate(logger, eventArgs.Generation, "RejectedBeforeQueue");
            activity.Complete(HostOperationOutcome.Completed);
            return;
        }
        try
        {
            if (eventArgs.ActivationEvidence is not null
                && (originalRequest is null || voiceRecognition is not HandsFreeVoiceRecognitionService))
            {
                throw new InvalidOperationException("An audio provider cannot supply host wake activation.");
            }
            var manualInput = CaptureManualCallInput(RequestOrigin.ActivatedVoice, CallPolicyRevision, eventArgs.Generation)
                with { PushToTalkCall = callCapture };
            if (AttachmentCataloguePreparationCommand.Parse(eventArgs.Transcript, AssistantName) is { } catalogue)
            {
                manualInput = manualInput with
                {
                    CataloguePreparation = catalogue,
                    CatalogueEligible = CaptureCatalogueVoiceEligibility(catalogue),
                };
            }
            var responseReadInput = CaptureResponseReadInput();
            var currentResponseInput = CaptureCurrentResponseInput(manualInput,
                Interlocked.Read(ref workspaceSurfaceInputRevision) + 1);
            var reviewEligibility = CaptureSkillReviewEligibility(manualInput);
            var questionTarget = CapturePendingQuestion(eventArgs.Transcript, beforeRouting: true);
            var newRequestEligibility = CaptureNewRequestOpeningEligibility(manualInput);
            var editorTarget = Volatile.Read(ref capturedCustomizationVoiceTarget);
            var foregroundTarget = Volatile.Read(ref foregroundConversationTarget);
            var speechInput = Volatile.Read(ref capturedSpeechInterruption);
            await uiDispatcher.InvokeAsync(() =>
            {
                if (eventArgs.ActivationEvidence is not null)
                {
                    if (originalRequest is not { } wakeRequest
                        || voiceRecognition is not HandsFreeVoiceRecognitionService wakeOwner)
                    {
                        throw new InvalidOperationException("The owning native wake producer is unavailable.");
                    }
                    manualInput = manualInput with
                    {
                        WakeActivationCurrent = wakeOwner.ConsumeWakeActivation(eventArgs, wakeRequest),
                        RoutingWakeContinuation = () => wakeOwner.CaptureRoutingContinuation(eventArgs, wakeRequest),
                    };
                }
                return HandleRecognizedVoiceTranscriptAsync(eventArgs, manualInput, responseReadInput, reviewEligibility,
                    newRequestEligibility, currentResponseInput, questionTarget, editorTarget, foregroundTarget, speechInput);
            });
            if (originalRequest is null && voiceRecognition is HandsFreeVoiceRecognitionService handsFree)
            {
                await handsFree.ResumeAfterActivatedInputAsync();
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ApplicationLog.Error(logger, exception, "Handling a recognized voice command");
            ShowFailure("The command could not be completed.", exception.Message);
            activity.Complete(HostOperationOutcome.Failed);
        }
    }

    private void OnRecognitionFailed(object? sender, VoiceRecognitionFailureEventArgs eventArgs)
    {
        if (sessionRoutingVoiceReply is not null) { return; }
        if (eventArgs.Reason == VoiceRecognitionFailureReason.PrivacyTransition
            && privacyObservation.Current.SessionState == WindowsSessionState.Locked)
        {
            ApplicationLog.Debug(logger, "Deferred locked-session recognition recovery to the privacy observer");
            return;
        }
        if (Interlocked.CompareExchange(ref acceptedTranscriptGeneration, -1, eventArgs.Generation) != eventArgs.Generation)
        {
            ApplicationLog.Debug(logger, "Rejected an unactivated or stale recognition failure");
            return;
        }
        HoldVoiceInput("Microphone closed · command capture failed; use Enable listening");
        uiDispatcher.Post(() =>
        {
            if (!IsHostInputEligible)
            {
                return;
            }

            ShowInformation("Command not recognized.", eventArgs.Message);
        });
    }

    private async Task HandleRecognizedVoiceTranscriptAsync(
        VoiceTranscriptEventArgs eventArgs, ManualCallInput manualInput, ResponseReadInput responseReadInput,
        Func<bool> reviewEligibility, Func<bool> newRequestEligibility, CurrentResponseInput currentResponseInput,
        PendingQuestionCapture questionTarget, Kora.Application.Definitions.CustomizationVoiceTarget? editorTarget,
        ForegroundConversationTarget? foregroundTarget, SpeechInterruptionInput? speechInput)
    {
        if (!IsCurrentPushToTalkCall(manualInput.PushToTalkCall)
            || eventArgs.Generation != voiceRecognition.Generation || lifecycleAdmissionClosed
            || !sessionController.IsCurrentSessionUnlocked()
            || manualInput.WakeActivationCurrent is { } wakeCurrent && !wakeCurrent())
        {
            ApplicationLog.Debug(logger, "Rejected an unactivated or stale voice transcript");
            ApplicationLog.VoiceDispatchGate(logger, eventArgs.Generation, "RejectedAfterQueue");
            ReportRetiredSkillReview(eventArgs.Transcript);
            if (SettingsSectionCommand.Parse(eventArgs.Transcript, AssistantName) is not null)
            { Transcript = "Settings navigation unavailable: the original voice generation or host was revoked."; }
            if (manualInput.CataloguePreparation is not null)
            { Transcript = "Attachment catalogue preparation unavailable: the original voice generation or host was revoked."; }
            if (EvidenceTraceCommand.Parse(eventArgs.Transcript, AssistantName) is not null)
            { Transcript = "Evidence trace unavailable: the original voice generation or host was revoked."; }
            if (questionTarget.Command is not null)
            { PendingQuestionNavigationStatus = "Pending question navigation unavailable: original voice or host admission was revoked."; }
            return;
        }
        if (Interlocked.CompareExchange(ref acceptedTranscriptGeneration, -1, eventArgs.Generation) != eventArgs.Generation)
        {
            ApplicationLog.VoiceDispatchGate(logger, eventArgs.Generation, "RejectedDuplicate");
            return;
        }
        Interlocked.CompareExchange(ref pushToTalkCall, null, manualInput.PushToTalkCall);
        ApplicationLog.VoiceDispatchGate(logger, eventArgs.Generation, "Admitted");
        IsListening = voiceRecognition.IsListening;
        if (IsSpeechInterruptionCommand(eventArgs.Transcript)
            && (speechInput?.Playback is not null || retainedAnswerSpeech is not null))
        {
            await InterruptSessionSpeechAsync(speechInput, SpeechInterruptionChannel.PushToTalk);
            return;
        }
        if (speechInput?.Playback is not null || retainedAnswerSpeech is not null)
        {
            ReportResponseRead("Only an explicit stop speaking command can interrupt the original session playback. No other command, speech or model work was dispatched.");
            return;
        }
        if (questionTarget.Command is not null)
        {
            await HandleTranscriptAsync(eventArgs.Transcript, eventArgs.Confidence,
                SecurityAuditInitiator.VoiceCommand, manualInput, questionTarget: questionTarget);
            return;
        }
        if (CurrentResponseCommand.Parse(eventArgs.Transcript, AssistantName) is not null)
        {
            await HandleTranscriptAsync(eventArgs.Transcript, eventArgs.Confidence,
                SecurityAuditInitiator.VoiceCommand, manualInput, currentResponseInput: currentResponseInput);
            return;
        }
        if (SettingsSectionCommand.Parse(eventArgs.Transcript, AssistantName) is not null
            || QuietHoursCommand.Parse(eventArgs.Transcript, AssistantName) is not null
            || LogonStartupCommand.Parse(eventArgs.Transcript, AssistantName) is not null
            || DetailPresentationCommand.Parse(eventArgs.Transcript, AssistantName) is not null
            || MarkdownDisplayCommand.Parse(eventArgs.Transcript, AssistantName) is not null
            || manualInput.CataloguePreparation is not null
            || EvidenceTraceCommand.Parse(eventArgs.Transcript, AssistantName) is not null)
        {
            await HandleTranscriptAsync(eventArgs.Transcript, eventArgs.Confidence,
                SecurityAuditInitiator.VoiceCommand, manualInput);
            return;
        }
        if (IsSpeaking && !IsSpeechInterruptionCommand(eventArgs.Transcript))
        {
            try
            {
                await textToSpeech.StopAsync();
                ConfirmSpeechOutputReleased();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                IsSpeaking = textToSpeech.IsSpeaking;
                LogSpokenResponseFailure(exception, "Stopping speech before voice command dispatch", activeCallSpeechLease is not null);
                ShowFailure("Speech output could not be stopped.", exception.Message);
                return;
            }
        }

        if (!IsCurrentPushToTalkCall(manualInput.PushToTalkCall)
            || eventArgs.Generation != voiceRecognition.Generation || !IsHostInputEligible
            || manualInput.WakeActivationCurrent is { } currentWake && !currentWake())
        {
            ApplicationLog.Debug(logger, "Discarded voice input retired while waiting for output shutdown");
            ReportRetiredSkillReview(eventArgs.Transcript);
            return;
        }
        await HandleTranscriptAsync(
            eventArgs.Transcript,
            eventArgs.Confidence,
            SecurityAuditInitiator.VoiceCommand,
            manualInput, responseReadInput, reviewEligibility, newRequestEligibility, currentResponseInput, questionTarget, editorTarget,
            foregroundTarget);
    }

    private async Task HandleTranscriptAsync(
        string spokenText,
        float confidence,
        SecurityAuditInitiator initiator,
        ManualCallInput? manualInput = null,
        ResponseReadInput? responseReadInput = null,
        Func<bool>? reviewEligibility = null,
        Func<bool>? newRequestEligibility = null,
        CurrentResponseInput? currentResponseInput = null,
        PendingQuestionCapture? questionTarget = null,
        Kora.Application.Definitions.CustomizationVoiceTarget? editorTarget = null,
        ForegroundConversationTarget? foregroundTarget = null)
    {
        CancelStartupPresentationReview();
        if (await TryAnswerDetailsOffer(spokenText, initiator)) { return; }
        if (await TryHandleResponseReadProposal(spokenText, initiator, responseReadInput)) { return; }
        if (responseReadPlanCancellation is not null
            && commandRouter.Match(spokenText, AssistantName).Command?.Action is not
                (BuiltInAction.CancelTask or BuiltInAction.StopSpeaking or BuiltInAction.ExitApplication))
        {
            ReportResponseRead("Local read planning is still running or joining. Cancel task before another request; the original answer is unchanged.");
            return;
        }
        if (QuietHoursCommand.Parse(spokenText, AssistantName) is { } quietHoursCommand)
        {
            CancelLogonStartupReview();
            if (!isAssistantNameAvailable || initiator == SecurityAuditInitiator.VoiceCommand
                && !HasCurrentNameActivation(spokenText))
            {
                CancelQuietHoursReview();
                Transcript = "Quiet hours require original typed input or the current assistant-name voice prefix.";
                return;
            }
            await ExecuteQuietHoursCommandAsync(quietHoursCommand, initiator, manualInput);
            return;
        }
        CancelQuietHoursReview();
        if (IsWakeCommand(spokenText) && string.Equals(AssistantName, "Stop", StringComparison.OrdinalIgnoreCase)
            && string.Equals(spokenText.Trim(), "listening", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteAsync(commandCatalog.GetCommands(AssistantName).Single(command => command.Action == BuiltInAction.DisableListening), initiator);
            return;
        }
        if (MarkdownDisplayCommand.Parse(spokenText, AssistantName) is { } markdownCommand)
        {
            if (!isAssistantNameAvailable || initiator == SecurityAuditInitiator.VoiceCommand
                && !HasCurrentNameActivation(spokenText))
            {
                CancelMarkdownDisplayReview();
                Transcript = "Markdown preferences require original typed input or the current assistant-name voice prefix.";
                return;
            }
            await ExecuteMarkdownDisplay(markdownCommand, initiator, manualInput);
            return;
        }
        if (DetailPresentationCommand.Parse(spokenText, AssistantName) is { } detailCommand)
        {
            if (!isAssistantNameAvailable || initiator == SecurityAuditInitiator.VoiceCommand
                && !HasCurrentNameActivation(spokenText))
            {
                Transcript = "Detail preferences require original typed input or the current assistant-name voice prefix.";
                return;
            }
            await ExecuteDetailPresentation(detailCommand, initiator, manualInput);
            return;
        }
        if (LogonStartupCommand.Parse(spokenText, AssistantName) is { } startupOperation)
        {
            if (!isAssistantNameAvailable || initiator == SecurityAuditInitiator.VoiceCommand
                && !HasCurrentNameActivation(spokenText))
            {
                CancelLogonStartupReview();
                Transcript = "Start at logon requires original typed input or the current assistant-name voice prefix.";
                return;
            }
            await ExecuteLogonStartup(startupOperation, initiator, manualInput);
            return;
        }
        CancelLogonStartupReview();
        if (CallSpeechCommand.Parse(spokenText, AssistantName) is { } callSpeechCommand)
        {
            if (callSpeechCommand.Operation is not { } operation || !isAssistantNameAvailable
                || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
                || (OriginalOrigin(initiator) == RequestOrigin.ActivatedVoice) != (initiator == SecurityAuditInitiator.VoiceCommand))
            {
                ReportCallSpeech("Call speech command was not accepted. Use exactly " + CallSpeechCommand.Syntax + ".");
                return;
            }
            await ExecuteCallSpeechAsync(operation, initiator);
            return;
        }
        if (SpokenApprovalReply.Parse(spokenText, AssistantName) is { } approvalReply)
        {
            await ExecuteSpokenToolApprovalAsync(approvalReply, spokenText, initiator);
            return;
        }
        if (WorkspaceSurfaceCommand.Parse(spokenText, AssistantName)?.Target is
                (WorkspaceSurfaceTarget.OpenNamedConversation or WorkspaceSurfaceTarget.ObservedResult)
            && (!CanEnterRetainedAnswerRead || editorTarget is not null))
        {
            ReportHostInteractionFailure("Conversation opening unavailable while a question, approval, review, editor or dictation owns input. Nothing was answered or submitted.");
            return;
        }
        var addressedWorkCommand = ParseAddressedSessionCommand(spokenText, initiator);
        if ((addressedWorkCommand?.Operation == SessionCommandOperation.Metrics
                || string.Equals(addressedWorkCommand?.Error, SessionCommand.MetricsSyntax, StringComparison.Ordinal))
            && (!CanAnswerRemainingWork || editorTarget is not null))
        {
            Transcript = "Session metrics are unavailable while a question, approval, editor or dictation owns input. Its decision is unchanged.";
            return;
        }
        if (addressedWorkCommand?.IsRemainingWork != true) { remainingWorkCommands?.Retire(); }
        if ((addressedWorkCommand?.IsRemainingWork == true || string.Equals(addressedWorkCommand?.Error, SessionCommand.RemainingWorkSyntax, StringComparison.Ordinal))
            && (!CanAnswerRemainingWork || editorTarget is not null))
        {
            Transcript = "Remaining-work answer unavailable while a question, approval, editor or dictation owns input. Its exact decision remains unchanged.";
            return;
        }
        var selectedAnswerCommand = ResponseReadCommand.Parse(spokenText, AssistantName) is { SelectedAnswer: true } selectedRead
            ? selectedRead : null;
        var displacedAnswerDecision = initiator == SecurityAuditInitiator.TypedCommand
            && Interlocked.Exchange(ref retainedAnswerDisplacedDecisionInput, 0) != 0;
        if (selectedAnswerCommand is { IsValid: true } && displacedAnswerDecision)
        {
            ReportResponseRead("Retained answer reading refused: the original input belonged to a prompt review retired by input editing. No decision was resumed and no playback or replay was queued.");
            return;
        }
        if (selectedAnswerCommand is not null && !CanEnterRetainedAnswerRead)
        {
            ReportResponseRead("Retained answer reading is unavailable while a question, approval, prompt review or dictation owns input. That input and its decision remain unchanged; no playback or replay was queued.");
            return;
        }
        if (selectedAnswerCommand is null) { RetireComposerDictation(); }
        if (ClipboardSnapshotStatusCommand.Parse(spokenText, AssistantName) is { } snapshotStatusCommand)
        {
            await ExecuteClipboardSnapshotStatusCommandAsync(snapshotStatusCommand, initiator,
                manualInput ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision));
            return;
        }
        if (SpeechComparisonCommand.Parse(spokenText, AssistantName) is { } comparisonDecision
            && !string.Equals(comparisonDecision.Operation, "compare speech input", StringComparison.Ordinal))
        {
            await ExecuteSpeechComparison(comparisonDecision, initiator);
            return;
        }
        Interlocked.Increment(ref workspaceSurfaceInputRevision);
        Interlocked.Increment(ref evidenceTraceInputRevision);
        var sessionCommand = addressedWorkCommand;
        var newRequestCommand = NewRequestOpenCommand.Parse(spokenText, AssistantName);
        var cataloguePreparation = manualInput?.CataloguePreparation
            ?? AttachmentCataloguePreparationCommand.Parse(spokenText, AssistantName);
        var evidenceTraceCommand = EvidenceTraceCommand.Parse(spokenText, AssistantName);
        var pendingQuestionCommand = questionTarget?.Command ?? PendingQuestionCommand.Parse(spokenText, AssistantName);
        var currentResponseCommand = CurrentResponseCommand.Parse(spokenText, AssistantName);
        if (currentResponseCommand is null && IsDetailsOfferPending
            && WorkspaceSurfaceCommand.Parse(spokenText, AssistantName) is { Target: WorkspaceSurfaceTarget.Details })
        { currentResponseCommand = new(CurrentResponseAction.Details); }
        if (cataloguePreparation is not null)
        {
            await PrepareAttachmentCatalogue(cataloguePreparation, initiator, manualInput);
            return;
        }
        if (evidenceTraceCommand is not null)
        {
            await PrepareEvidenceTraceAsync(evidenceTraceCommand, initiator, manualInput);
            return;
        }
        if (pendingQuestionCommand is not null)
        {
            await ExecutePendingQuestionCommand(pendingQuestionCommand, spokenText, initiator, manualInput, questionTarget);
            return;
        }
        if (currentResponseCommand is not null)
        {
            await ExecuteCurrentResponseCommandAsync(currentResponseCommand, spokenText, initiator, manualInput, currentResponseInput);
            return;
        }
        if (Kora.Application.Agents.AgentRunCommand.Parse(spokenText, AssistantName) is { } agentCommand)
        {
            await ExecuteAgentCommand(agentCommand, spokenText, manualInput
                ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision), reviewEligibility);
            return;
        }
        if (KnowledgeSourceCaptureLimitsCommand.Parse(spokenText, AssistantName) is { } captureLimits)
        {
            await ExecuteKnowledgeSourceCaptureLimits(captureLimits, spokenText, manualInput
                ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision), reviewEligibility);
            return;
        }
        if (Kora.Application.KnowledgeSources.KnowledgeSourceAnswerCommand.Parse(spokenText, AssistantName) is { } namedAnswer)
        {
            await ExecuteKnowledgeSourceAnswer(namedAnswer, spokenText, manualInput
                ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision), reviewEligibility);
            return;
        }
        if (Kora.Application.Skills.NamedSkillCommand.Parse(spokenText, AssistantName) is { } namedSkill)
        {
            await ExecuteNamedSkillInvocation(namedSkill, spokenText, manualInput
                ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision), reviewEligibility);
            return;
        }
        if (Kora.Application.Tools.CustomizationDiscoveryCommand.Parse(spokenText, AssistantName) is { } discoveryCommand)
        {
            await ExecuteCustomizationDiscovery(discoveryCommand, spokenText, manualInput
                ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision), reviewEligibility);
            return;
        }
        if (Kora.Application.Tools.SessionMetadataCommand.Parse(spokenText, AssistantName) is { } metadataCommand)
        {
            await ExecuteSessionMetadata(metadataCommand, spokenText, manualInput
                ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision), reviewEligibility);
            return;
        }
        if (Kora.Application.Skills.SharedSkillInvocationCommand.Parse(spokenText, AssistantName) is { } skillUse)
        {
            await ExecuteSharedSkillInvocation(skillUse, spokenText, manualInput
                ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision), reviewEligibility);
            return;
        }
        if (Kora.Application.Skills.OwnedSkillAuthoringCommand.Parse(spokenText, AssistantName) is { } authoringCommand)
        {
            await ExecuteOwnedSkillAuthoring(authoringCommand, spokenText, manualInput
                ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision), reviewEligibility);
            return;
        }
        if (Kora.Application.Skills.SkillReviewCommand.Parse(spokenText, AssistantName) is { } reviewCommand)
        {
            await ExecuteSkillReviewCommand(reviewCommand, spokenText, manualInput
                ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision), reviewEligibility);
            return;
        }
        if (SettingsSectionCommand.Parse(spokenText, AssistantName) is { } settingsSectionCommand)
        {
            await ExecuteSettingsSectionCommand(settingsSectionCommand, spokenText, initiator, manualInput);
            return;
        }
        if (WorkspaceSurfaceCommand.Parse(spokenText, AssistantName) is { } surfaceCommand)
        {
            await ExecuteWorkspaceSurfaceCommand(surfaceCommand, spokenText, initiator, manualInput);
            return;
        }
        if (!IsHostInputEligible)
        {
            ApplicationLog.Information(logger, "Rejected command input outside the active unlocked host");
            return;
        }
        if (newRequestCommand is not null)
        {
            await ExecuteNewRequestOpenCommand(newRequestCommand, initiator, newRequestEligibility);
            return;
        }
        if (IsListeningStopCommand(spokenText))
        {
            await RunTranscriptOperationAsync(
                OriginalOrigin(initiator),
                () => DisableListeningAsync(initiator));
            return;
        }
        if (ResponseWindowCommand.Parse(spokenText, AssistantName) is { } responseWindowCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no response-window control was dispatched.";
                return;
            }
            await ExecuteResponseWindowCommandAsync(responseWindowCommand, initiator);
            return;
        }
        if (selectedAnswerCommand is not null)
        {
            await ExecuteResponseReadCommandAsync(selectedAnswerCommand, spokenText, initiator, responseReadInput);
            return;
        }
        Interlocked.Increment(ref dispositionReviewEpoch);
        Interlocked.Increment(ref approvalPreparationRevision);
        var speechRevision = Interlocked.Read(ref manualCallSpeechRevision);
        if (ResponseReadCommand.Parse(spokenText, AssistantName) is { } responseReadCommand)
        {
            await ExecuteResponseReadCommandAsync(responseReadCommand, spokenText, initiator, responseReadInput);
            return;
        }
        RetirePromptPreparationForNewInput();
        if (PreparedLocalSearchCommand.Parse(spokenText, AssistantName) is { } preparedSearch)
        {
            HostRequestRunner.Run(OriginalOrigin(initiator), () => PrepareLocalSearch(preparedSearch, initiator));
            return;
        }
        if (Kora.Application.Definitions.CustomizationCommand.Parse(spokenText, AssistantName) is { } customization)
        {
            var reviewInput = manualInput ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision);
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                ReportHostInteractionFailure("Assistant prefix routing is unavailable; no customization control was dispatched.");
                return;
            }
            await RunTranscriptOperationAsync(OriginalOrigin(initiator),
                () => initiator == SecurityAuditInitiator.VoiceCommand && customization.Operation is "Refine"
                    ? ExecuteCustomizationVoice(editorTarget, customization.Text!)
                    : ExecuteCustomizationCommand(customization, reviewEligibility ?? CaptureSkillReviewEligibility(reviewInput)));
            return;
        }
        if (IsInstructionEditorCommand(spokenText))
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                ReportHostInteractionFailure("Assistant prefix routing is unavailable; no instruction control was dispatched.");
                return;
            }
            await RunTranscriptOperationAsync(OriginalOrigin(initiator),
                () => ExecuteInstructionEditorCommand(spokenText, manualInput, reviewEligibility));
            return;
        }
        if (CommunicationActivityCommand.Parse(spokenText, AssistantName) is { } activityCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no communications source change was dispatched.";
                return;
            }
            await ExecuteCommunicationActivityAsync(activityCommand, initiator);
            return;
        }
        if (InCallFeedbackCommand.Parse(spokenText, AssistantName) is { } callFeedbackCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no call feedback change was dispatched.";
                return;
            }
            await ExecuteInCallFeedbackCommandAsync(callFeedbackCommand, initiator);
            return;
        }
        if (ManualCallCommand.Parse(spokenText, AssistantName) is { } manualCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no call change was dispatched.";
                return;
            }
            await ExecuteManualCallCommandAsync(manualCommand, manualInput ?? CaptureManualCallInput(
                OriginalOrigin(initiator), CallPolicyRevision));
            return;
        }
        if (OutputDeviceCommand.Parse(spokenText, AssistantName) is { } outputCommand)
        {
            await ExecuteOutputDeviceCommandAsync(outputCommand, initiator);
            return;
        }
        if (AuditRetentionCommand.Parse(spokenText, AssistantName) is { } auditRetentionCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no audit-retention change was dispatched.";
                return;
            }
            await ExecuteAuditRetentionCommandAsync(auditRetentionCommand, initiator);
            return;
        }
        if (DiagnosticRetentionCommand.Parse(spokenText, AssistantName) is { } diagnosticRetentionCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no logging-retention control was dispatched.";
                return;
            }
            await ExecuteDiagnosticRetentionCommandAsync(diagnosticRetentionCommand, initiator);
            return;
        }
        if (OrdinaryTurnDeadlineCommand.Parse(spokenText, AssistantName) is { } ordinaryDeadlineCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no task deadline change was dispatched.";
                return;
            }
            await ExecuteOrdinaryDeadlineCommandAsync(ordinaryDeadlineCommand, initiator);
            return;
        }
        if (SessionQueueConfigurationCommand.Parse(spokenText, AssistantName) is not null)
        {
            Transcript = "Diagnostic queue settings retired. Existing records and their original deadlines are unchanged.";
            return;
        }
        if (WindowsSpeechRateCommand.Parse(spokenText, AssistantName) is { } rateCommand)
        {
            await ExecuteWindowsSpeechRateCommandAsync(rateCommand, initiator);
            return;
        }
        if (SpeechComparisonCommand.Parse(spokenText, AssistantName) is { } speechComparisonCommand)
        {
            await ExecuteSpeechComparison(speechComparisonCommand, initiator);
            return;
        }
        if (ForegroundPttCommand.Parse(spokenText, AssistantName) is { } foregroundPttCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no foreground shortcut setting was changed.";
                return;
            }
            await ExecuteForegroundPttCommandAsync(foregroundPttCommand, initiator);
            return;
        }
        if (VoiceCaptureTimingCommand.Parse(spokenText, AssistantName) is { } timingCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no capture timing change was dispatched.";
                return;
            }
            await ExecuteVoiceCaptureTimingAsync(timingCommand, initiator);
            return;
        }
        if (SpeechInputCommand.Parse(spokenText, AssistantName) is { } speechInputCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no speech-input selection was dispatched.";
                return;
            }
            await ExecuteSpeechInputCommandAsync(speechInputCommand, initiator);
            return;
        }
        if (PlaybackVolumeCommand.Parse(spokenText, AssistantName) is { } volumeCommand)
        {
            await ExecutePlaybackVolumeCommandAsync(volumeCommand, initiator);
            return;
        }
        if (SpeechTextCommand.Parse(spokenText, AssistantName) is { } speechTextCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no speech-caption control was dispatched.";
                return;
            }
            await ExecuteSpeechTextCommandAsync(speechTextCommand, initiator);
            return;
        }
        if (ConversationExecutionCommand.Parse(spokenText, AssistantName) is { } executionCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Assistant prefix routing is unavailable; no conversation execution setting was changed.";
                return;
            }
            await ExecuteConversationExecutionCommandAsync(executionCommand, initiator);
            return;
        }
        if (ResponseModeCommand.Parse(spokenText, AssistantName) is { } responseModeCommand)
        {
            await ExecuteResponseModeCommandAsync(responseModeCommand, initiator);
            return;
        }
        if (SessionProviderProfileCommand.Parse(spokenText, AssistantName) is { } sessionProviderProfileCommand)
        {
            await ExecuteSessionProviderProfileCommand(sessionProviderProfileCommand, initiator);
            return;
        }
        if (ProviderProfileCommand.Parse(spokenText, AssistantName) is { } providerProfileCommand)
        {
            await ExecuteProviderProfileCommandAsync(providerProfileCommand, initiator);
            return;
        }
        if (ProviderModeCommand.Parse(spokenText, AssistantName) is { } providerModeCommand)
        {
            await ExecuteProviderModeCommandAsync(providerModeCommand, initiator);
            return;
        }
        if (PreapprovedUriCommand.Parse(spokenText, AssistantName) is { } preapprovedUriCommand)
        {
            await ExecutePreapprovedUriCommandAsync(preapprovedUriCommand, initiator);
            return;
        }
        if (WebSummaryCommand.Parse(spokenText, AssistantName) is { } webSummaryCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                ReportHostInteractionFailure("Assistant prefix routing is unavailable; no web summary was dispatched.");
                return;
            }
            await ExecuteWebSummary(webSummaryCommand, initiator);
            return;
        }
        if (Kora.Application.Mcp.McpCommand.Parse(spokenText, AssistantName) is { } mcpCommand)
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                ReportHostInteractionFailure("Assistant prefix routing is unavailable; no MCP request was dispatched.");
                return;
            }
            await ExecuteMcpCommandAsync(mcpCommand, initiator);
            return;
        }
        if (WebPageCommand.Parse(spokenText, AssistantName) is { } webPageCommand)
        {
            await ExecuteWebPageCommandAsync(webPageCommand, initiator);
            return;
        }
        var origin = initiator == SecurityAuditInitiator.VoiceCommand
            ? Kora.Core.Hosting.RequestOrigin.ActivatedVoice : Kora.Core.Hosting.RequestOrigin.LocalUi;
        if (AssistantNameCommand.Parse(spokenText, AssistantName) is { } assistantCommand
            && (isAssistantNameAvailable || !commandRouter.IsActivationPrefixed(spokenText, AssistantName)))
        {
            await RunTranscriptOperationAsync(origin,
                () => ExecuteAssistantNameCommandAsync(assistantCommand, initiator));
            return;
        }
        if (!isAssistantNameAvailable
            && commandRouter.Match(spokenText, AssistantName).Command?.Action is not
                (BuiltInAction.CancelTask or BuiltInAction.StopSpeaking or BuiltInAction.CancelPowerAction
                or BuiltInAction.OpenSettings or BuiltInAction.OpenDocumentation or BuiltInAction.ExitApplication))
        {
            ShowFailure("Assistant prefix routing is unavailable.", assistantNameConfiguration.Get().Recovery!);
            return;
        }
        if (RegisteredSettingUndoCommand.Parse(spokenText, AssistantName) is { } undoCommand)
        {
            if (initiator == SecurityAuditInitiator.VoiceCommand
                && !HasCurrentNameActivation(spokenText))
            {
                ShowInformation("Settings undo requires original input.", "Use the current assistant-name prefix. Nothing was applied.");
                return;
            }
            await RunTranscriptOperationAsync(origin,
                () => ExecuteSettingsUndoAsync(undoCommand, initiator, CancellationToken.None));
            return;
        }
        if (RegisteredSettingSuggestionCommand.ParseResponseTimeout(spokenText, AssistantName) is { } timeoutSuggestionCommand)
        {
            await ExecuteResponseTimeoutSuggestionAsync(timeoutSuggestionCommand, initiator,
                HasCurrentNameActivation(spokenText));
            return;
        }
        if (RegisteredSettingSuggestionCommand.Parse(spokenText, AssistantName) is { } responseSuggestionCommand)
        {
            await ExecuteResponseModeSuggestionAsync(responseSuggestionCommand, initiator,
                HasCurrentNameActivation(spokenText));
            return;
        }
        if (PlaybackVolumeSuggestionCommand.Parse(spokenText, AssistantName) is { } volumeSuggestionCommand)
        {
            await ExecutePlaybackVolumeSuggestionAsync(volumeSuggestionCommand, initiator,
                HasCurrentNameActivation(spokenText));
            return;
        }
        if (WindowsSpeechRateSuggestionCommand.Parse(spokenText, AssistantName) is { } rateSuggestionCommand)
        {
            await ExecuteWindowsSpeechRateSuggestionAsync(rateSuggestionCommand, initiator,
                commandRouter.IsActivationPrefixed(spokenText, AssistantName));
            return;
        }
        if (SpeechSummarySuggestionCommand.Parse(spokenText, AssistantName) is { } speechSuggestionCommand)
        {
            await ExecuteSpeechSummarySuggestionAsync(speechSuggestionCommand, initiator,
                HasCurrentNameActivation(spokenText));
            return;
        }
        if (InstalledSpeechVoiceSuggestionCommand.Parse(spokenText, AssistantName) is { } installedVoiceSuggestionCommand)
        {
            await ExecuteInstalledSpeechVoiceSuggestionAsync(installedVoiceSuggestionCommand, initiator,
                HasCurrentNameActivation(spokenText));
            return;
        }
        if (SpeechCommand.Parse(spokenText, AssistantName) is { IsNaturalSummary: true } speechCommand)
        {
            if (initiator == SecurityAuditInitiator.VoiceCommand
                && !HasCurrentNameActivation(spokenText))
            {
                Transcript = "Use the current assistant-name prefix for spoken summary limit voice commands. No speech setting was changed.";
                return;
            }
            await RunTranscriptOperationAsync(origin,
                () => ExecuteSpeechCommandAsync(speechCommand, initiator));
            return;
        }
        if (Kora.Core.Interaction.LocalEventCommand.Parse(spokenText, AssistantName) is { } eventCommand)
        {
            await ExecuteLocalEventCommandAsync(eventCommand, initiator);
            return;
        }
        if (Kora.Core.Maintenance.MaintenanceUpdateCommand.Parse(spokenText, AssistantName) is { } updateCommand)
        {
            await ExecuteMaintenanceUpdateCommandAsync(updateCommand, initiator);
            return;
        }
        if (Kora.Core.Maintenance.MaintenanceCommandParser.Parse(spokenText, AssistantName) is { } maintenanceCommand)
        {
            await ExecuteMaintenanceCommandAsync(maintenanceCommand, initiator);
            return;
        }
        if (SessionDispositionReviewCommand.Parse(spokenText, AssistantName) is { } dispositionCommand)
        {
            await ExecuteSessionDispositionReviewAsync(dispositionCommand, initiator,
                HasCurrentNameActivation(spokenText));
            return;
        }
        if (SessionQueueCancellationReviewCommand.Parse(spokenText, AssistantName) is { } cancellationPreparation)
        {
            await ExecuteQueueCancellationPreparationAsync(cancellationPreparation, initiator,
                HasCurrentNameActivation(spokenText));
            return;
        }
        if (sessionCommand is not null)
        {
            if (initiator == SecurityAuditInitiator.VoiceCommand
                && (sessionCommand.IsRetentionCommand || sessionCommand.IsConversationSearch || sessionCommand.IsPendingClear
                    || sessionCommand.IsPermissionCommand
                    || sessionCommand.IsConversationDispatch
                    || sessionCommand.Operation is SessionCommandOperation.RequestReview or SessionCommandOperation.RequestConfirm
                        or SessionCommandOperation.RequestCancel or SessionCommandOperation.AttachmentInspect)
                && (!HasCurrentNameActivation(spokenText)
                    || sessionCommand.RequestForegroundTarget && !sessionCommand.RequestActivationPrefixed && !IsWakeCommand(spokenText)))
            {
                RetireSessionCommandReviews();
                ShowInformation("Session request denied.", "Use the current assistant-name prefix and exact session grammar. No pending question or model received this input.");
                return;
            }
            await ExecuteSessionCommandAsync(sessionCommand, initiator,
                reviewEligibility ?? CaptureSkillReviewEligibility(manualInput
                    ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision)),
                HasCurrentNameActivation(spokenText),
                manualInput?.AssistantNameRevision ?? assistantNameConfiguration.Get().Revision, foregroundTarget,
                sessionCommand.IsPermissionCommand
                    ? CapturePermissionReviewSource(manualInput
                        ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision))
                    : null);
            return;
        }
        if (MemoryCommand.Parse(spokenText, AssistantName) is { } memoryCommand)
        {
            await ExecuteMemoryCommandAsync(memoryCommand, initiator);
            return;
        }
        var admittedCommand = commandRouter.Match(spokenText, AssistantName).Command;
        if (admittedCommand?.Action == BuiltInAction.ShowVersion)
        {
            await durableVersionQuery.RunAsync(origin,
                async () =>
                {
                    if (!IsHostInputEligible)
                    {
                        throw new InvalidOperationException("Version-query admission changed before local dispatch.");
                    }
                    BeginTranscriptPresentation(spokenText, confidence, initiator);
                    await ExecuteAsync(admittedCommand, initiator);
                }, CancellationToken.None);
            if (ShouldSpeakResponse(BuiltInAction.ShowVersion))
            {
                await SpeakCurrentResponseAsync(expectedSpeechRevision: speechRevision);
            }
            return;
        }
        await RunTranscriptOperationAsync(origin,
            () => RouteTranscriptAsync(spokenText, confidence, initiator, speechRevision, editorTarget, manualInput));
    }

    private void BeginTranscriptPresentation(string spokenText, float confidence, SecurityAuditInitiator initiator)
    {

        Transcript = $"“{spokenText}” · {confidence:P0} confidence";
        State = AssistantState.Calculating;
        if (initiator == SecurityAuditInitiator.VoiceCommand)
        {
            WindowActionRequested?.Invoke(this, WindowAction.ShowPresence);
        }
    }

    private async Task RouteTranscriptAsync(
        string spokenText,
        float confidence,
        SecurityAuditInitiator initiator,
        long speechRevision, Kora.Application.Definitions.CustomizationVoiceTarget? editorTarget = null,
        ManualCallInput? originalInput = null)
    {
        if (await TryRouteStopAllWorkAsync(spokenText, confidence, initiator, originalInput)) { return; }
        if (InputDeviceCommand.Parse(spokenText, AssistantName) is { } inputCommand)
        {
            await ExecuteInputDeviceCommandAsync(inputCommand, initiator);
            return;
        }
        if (initiator == SecurityAuditInitiator.VoiceCommand
            && (editorTarget is not null || Kora.Application.Definitions.CustomizationCommand.ReferencesEditor(spokenText))
            && !commandRouter.Match(spokenText, AssistantName).IsMatch
            && AppearanceCommand.Parse(spokenText, AssistantName) is null
            && NaturalAppearanceCommand.Parse(spokenText, AssistantName, true) is null
            && SpeechCommand.Parse(spokenText, AssistantName) is null
            && ClipboardCommand.Parse(commandRouter.Match(spokenText, AssistantName).NormalizedTranscript) is null
            && LocalFileCommand.Parse(commandRouter.Match(spokenText, AssistantName).NormalizedTranscript) is null
            && Kora.Core.Tools.ReadOnlyCapabilityCatalog.MatchCommand(commandRouter.Match(spokenText, AssistantName).NormalizedTranscript) is null
            && !artifactCommandRouter.Match(spokenText, AssistantName).IsArtifactCommand)
        {
            await ExecuteCustomizationVoice(editorTarget, spokenText);
            return;
        }
        if ((AppearanceCommand.Parse(spokenText, AssistantName)
            ?? NaturalAppearanceCommand.Parse(spokenText, AssistantName, initiator == SecurityAuditInitiator.VoiceCommand && !IsWakeCommand(spokenText))) is { } appearanceCommand)
        {
            BeginTranscriptPresentation(spokenText, confidence, initiator);
            await ExecuteAppearanceCommandAsync(appearanceCommand, initiator);
            return;
        }

        if (SpeechCommand.Parse(spokenText, AssistantName) is { } speechCommand)
        {
            BeginTranscriptPresentation(spokenText, confidence, initiator);
            await ExecuteSpeechCommandAsync(speechCommand, initiator);
            return;
        }

        var match = commandRouter.Match(spokenText, AssistantName);
        if (LocalFileCommand.Parse(match.NormalizedTranscript) is { } fileCommand)
        {
            BeginTranscriptPresentation(spokenText, confidence, initiator);
            await ExecuteFileCommandAsync(fileCommand);
            return;
        }
        if (ClipboardCommand.Parse(match.NormalizedTranscript) is { } clipboardCommand)
        {
            BeginTranscriptPresentation(spokenText, confidence, initiator);
            await ExecuteClipboardCommandAsync(clipboardCommand);
            return;
        }
        if (Kora.Core.Tools.ReadOnlyCapabilityCatalog.MatchCommand(match.NormalizedTranscript) is not null)
        {
            BeginTranscriptPresentation(spokenText, confidence, initiator);
            TryPresentCapabilityCommand(match.NormalizedTranscript);
            return;
        }
        var artifactMatch = artifactCommandRouter.Match(spokenText, AssistantName);
        if (artifactMatch.IsArtifactCommand)
        {
            if (artifactMatch.Invocation is null)
            {
                ShowInformation("Artifact command not found.", artifactMatch.Error!);
                return;
            }
            if (artifactMatch.Invocation.Artifact.PromptTemplate is null)
            {
                await HandleUnmatchedRequestAsync();
                return;
            }
            BeginTranscriptPresentation(spokenText, confidence, initiator);
            var promptEligibility = CapturePromptSelection();
            if (!await IsArtifactSourceCurrent(artifactMatch.Invocation.Artifact, CancellationToken.None))
            {
                ShowInformation("Definition revision is stale or unavailable.", "Reload and review the exact definition on this machine before starting a new request.");
                return;
            }
            await PreparePrompt(artifactMatch.Invocation, spokenText, initiator, promptEligibility!);
            return;
        }
        if (!match.IsMatch || match.Command is null)
        {
            if (await TryRouteSessionRequestAsync(spokenText, initiator, originalInput)) { return; }
            await HandleUnmatchedRequestAsync();
            return;
        }

        BeginTranscriptPresentation(spokenText, confidence, initiator);
        var cancellingReadProposal = match.Command.Action == BuiltInAction.CancelTask
            && (responseReadProposal is not null || responseReadPlanCancellation is not null);
        await ExecuteAsync(match.Command, initiator, sourceCurrent: match.Command.Action == BuiltInAction.LockMachine
            && originalInput is not null ? CaptureLockSource(originalInput) : originalInput?.Eligible);
        if (!cancellingReadProposal && ShouldSpeakResponse(match.Command.Action))
        {
            await SpeakCurrentResponseAsync(expectedSpeechRevision: speechRevision);
        }
    }

    private Task HandleUnmatchedRequestAsync()
    {
        ShowFailure("Model request unavailable.", "Nothing was sent. Built-in commands and session conversations remain available.");
        return Task.CompletedTask;
    }

    public async Task CancelCurrentTaskAsync()
    {
        if (responseReadProposal is not null || responseReadPlanCancellation is not null)
        {
            await CloseResponseReadProposal();
            return;
        }
        RetireResponseReadPlayback();
        Interlocked.Increment(ref approvalPreparationRevision);
        RetireWebResultDetails();
        if (webPageCancellation is { } webCancellation) { await webCancellation.CancelAsync(); }
        if (webSummaryCancellation is { } summaryCancellation) { await summaryCancellation.CancelAsync(); }
        RetireSpeechCaption();
        textToSpeech.InvalidateOutput();
        ClearClipboardPreview();
        ClearFilePreview();
        var powerRetirement = CancelPendingPowerAuditAsync("task-cancelled");
        if (powerShellSetupCancellation is not null)
        {
            await powerShellSetupCancellation.CancelAsync();
            ShowInformation("Cancelling PowerShell setup.", "The approved installation is stopping.");
        }
        else if (localModelCancellation is not null)
        {
            await localModelCancellation.CancelAsync();
            ShowInformation("Cancelling local model setup.", "The running setup step is stopping.");
        }
        else if (IsSpeaking)
        {
            await StopSpeakingAsync();
        }
        else
        {
            ShowInformation("Cancelled.", $"No running {AssistantName} task or power proposal will continue. Setup items needing your action remain available.");
        }
        await powerRetirement;
    }

    private string? spokenSummaryRecovery;
    public Task<bool> ResponseOutputUpdate { get; private set; } = Task.FromResult(true);
    private long outputPresentationRevision = -1;

    private async Task<bool> RefreshResponseOutputAsync(long responseRevision, bool requestWindow)
    {
        var begin = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation);
        using var activity = begin();
        var refreshed = await RefreshOutputEndpointsAsync();
        if (refreshed && !disposed && responseRevision == Interlocked.Read(ref webResponseRevision)
            && requestWindow && IsHostInputEligible && IsVisualResponseVisible)
        {
            WindowActionRequested?.Invoke(this, WindowAction.Show);
        }
        activity.Complete(refreshed ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        return refreshed;
    }

    private async Task SpeakCurrentResponseAsync(long? expectedSpeechRevision = null,
        CancellationToken cancellationToken = default)
    {
        await TrySpeakCurrentResponseAsync(expectedSpeechRevision: expectedSpeechRevision, cancellationToken: cancellationToken);
    }

    private async Task<bool> TrySpeakCurrentResponseAsync(string? spokenText = null, long? expectedSpeechRevision = null,
        Func<bool>? additionalEligibility = null,
        Func<CancellationToken, Task>? validateReadSource = null, CallSpeechLease? callSpeechLease = null,
        Action<SpeechReadOutcome>? readOutcome = null, CancellationToken cancellationToken = default)
    {
        if (speechInterruptionActive) { return false; }
        if (textToSpeech.IsSpeaking)
        {
            PreserveUnconfirmedSpeechRelease();
            return false;
        }
        if (additionalEligibility is null && responseReadCancellation is not null)
        {
#pragma warning disable VSTHRD003 // A replacement response cannot start output until the retired one-shot has released it.
            await responseReadQuiescence;
#pragma warning restore VSTHRD003
        }
        var speechRevision = expectedSpeechRevision ?? Interlocked.Read(ref manualCallSpeechRevision);
        var responseRevision = Interlocked.Read(ref webResponseRevision);
        if (!IsHostInputEligible || additionalEligibility?.Invoke() == false) { return false; }
        var presentationRevision = outputPresentationRevision;
        var presentationOutput = ResponseOutputUpdate;
        var observedCurrentOutput = false;
        if (presentationRevision >= 0)
        {
#pragma warning disable VSTHRD003 // Join the response's owned output observation, rather than starting duplicate native discovery.
            var refreshed = await presentationOutput;
#pragma warning restore VSTHRD003
            if (outputPresentationRevision == presentationRevision && ReferenceEquals(ResponseOutputUpdate, presentationOutput))
            {
                outputPresentationRevision = -1;
            }
            if (!refreshed) { return false; }
            observedCurrentOutput = presentationRevision == responseRevision;
        }
        if (!observedCurrentOutput
            && !await RefreshOutputEndpointsAsync(preserveResponse: additionalEligibility is not null, token: cancellationToken)) { return false; }
        if (responseRevision != Interlocked.Read(ref webResponseRevision)) { return false; }
        bool Eligible() => IsResponseSpeechEligible(callSpeechLease) && !cancellationToken.IsCancellationRequested
            && speechRevision == Interlocked.Read(ref manualCallSpeechRevision)
            && responseRevision == Interlocked.Read(ref webResponseRevision)
            && (additionalEligibility is not null || responseReadCancellation is null)
            && additionalEligibility?.Invoke() != false;
        if (!Eligible())
        {
            if (speechRevision != Interlocked.Read(ref manualCallSpeechRevision))
            {
                PreserveSpokenResponseFailure("Speech from the earlier request was retired by manual call control; the complete result remains visual.");
            }
            return false;
        }

        if (validateReadSource is not null) { await validateReadSource(cancellationToken); }
        if (!Eligible()) { return false; }
        spokenText ??= $"{ResponseTitle}. {ResponseBody}";
        IsSpeaking = true;
        activeSpokenText = spokenText;
        ApplicationLog.Debug(logger, "Starting spoken response output");
        var completedSpeech = false;
        var startedSpeech = false;
        SpeechReadOutcome? readFailure = null;
        try
        {
            // Admission checks cancellation; generation invalidation and StopAsync own resource release.
            Task Start()
            {
                cancellationToken.ThrowIfCancellationRequested();
                startedSpeech = true;
                return StartCaptionedSpeechAsync(spokenText, Eligible, callSpeechLease?.Output,
                    additionalEligibility is null ? CancellationToken.None : cancellationToken);
            }
            var recovery = await speechConfiguration.StartSummarySpeechAsync(spokenText, communicationPolicy,
                Eligible, Start, callSpeechLease, cancellationToken);
            if (recovery is not null)
            {
                spokenSummaryRecovery = recovery;
                forceVisualResponse = true;
                NotifyOutputPolicyChanged();
                if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
            }
            completedSpeech = startedSpeech && Eligible() && !textToSpeech.IsSpeaking;
        }
        catch (OperationCanceledException)
        {
            readFailure = SpeechReadOutcome.Cancelled;
            ApplicationLog.Information(logger, "Spoken response was invalidated by a privacy transition");
            forceVisualResponse = true;
            NotifyOutputPolicyChanged();
            if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
        }
        catch (PlaybackVolumeUnavailableException exception)
        {
            readFailure = SpeechReadOutcome.Failed;
            LogSpokenResponseFailure(exception, "Playing a response with unavailable Kora playback volume", additionalEligibility is not null);
            PreserveSpokenResponseFailure($"{AssistantName} playback volume is zero or unavailable. Full visual output is retained; refresh volume preferences.");
        }
        catch (WindowsSpeechRateUnavailableException exception)
        {
            readFailure = SpeechReadOutcome.Failed;
            LogSpokenResponseFailure(exception, "Playing a response with unavailable Windows speech rate", additionalEligibility is not null);
            audioPlaybackSettings.HoldRateUnavailable();
            PreserveSpokenResponseFailure("Windows speech rate is unavailable. Full visual output is retained; explicitly refresh rate preferences.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            readFailure = SpeechReadOutcome.Failed;
            LogSpokenResponseFailure(exception, "Playing a response with the selected speech voice", additionalEligibility is not null);
            ClearActiveSpeechVoice();
            PreserveSpokenResponseFailure("The selected speech voice is unavailable. Refresh installed voices; the complete visual response is retained.");
        }
        catch (AudioOutputDeviceUnavailableException exception)
        {
            readFailure = SpeechReadOutcome.Failed;
            LogSpokenResponseFailure(exception, "Playing a response through audio output", additionalEligibility is not null);
            HandleAudioOutputFailure(exception, "The selected audio output is unavailable.", preserveResponseOnMute: true);
        }
        catch (InvalidOperationException exception)
        {
            readFailure = SpeechReadOutcome.Failed;
            LogSpokenResponseFailure(exception, "Playing a response with text-to-speech", additionalEligibility is not null);
            ClearActiveSpeechVoice();
            PreserveSpokenResponseFailure("Text-to-speech is unavailable. Refresh speech readiness; the complete visual response is retained.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            readFailure = SpeechReadOutcome.Failed;
            LogSpokenResponseFailure(exception, "Playing a spoken response unexpectedly", additionalEligibility is not null);
            PreserveSpokenResponseFailure("Speech playback failed. The complete visual response is retained; no automatic retry.");
        }
        finally
        {
            if (completedSpeech) { CompleteSpeechCaption(); }
            else { RetireSpeechCaption(); }
            activeSpokenText = null;
            IsSpeaking = textToSpeech.IsSpeaking;
            if (additionalEligibility is null && IsSpeaking) { PreserveUnconfirmedSpeechRelease(); }
        }
        if (additionalEligibility is not null && textToSpeech.IsSpeaking)
        {
            // Unconfirmed release retains active ownership and the native Stop recovery route.
            try { await textToSpeech.StopAsync(CancellationToken.None); }
            catch
            {
                callSpeechLease?.HoldUnconfirmedRelease();
                throw;
            }
            if (textToSpeech.IsSpeaking)
            {
                IsSpeaking = true;
                callSpeechLease?.HoldUnconfirmedRelease();
                throw new InvalidOperationException("Owned response playback release is unconfirmed.");
            }
            IsSpeaking = false;
        }
        readOutcome?.Invoke(completedSpeech ? SpeechReadOutcome.Played
            : readFailure ?? (startedSpeech ? SpeechReadOutcome.Cancelled : SpeechReadOutcome.Denied));
        return completedSpeech;
    }

    private void SetActiveSpeechVoice(SpeechVoice? voice)
    {
        activeSpeechVoice = voice;
        UpdateSpeechPreparation();
        OnPropertyChanged(nameof(IsSpeechOutputAvailable));
        NotifyOutputPolicyChanged();
    }

    private void ClearActiveSpeechVoice()
    {
        speechConfiguration.HoldUnavailable("Speech output is unavailable. Choose an installed voice or explicitly refresh/reset. Visual responses remain available.");
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
        string unavailableTitle,
        bool preserveResponseOnMute = false)
    {
        outputConfiguration?.HoldUnavailable("Native output open, mute or playback failure. Full visual output is retained; refresh before using the saved route.");
        if (SelectedOutputDevice?.IsSystemDefault == true)
        {
            systemDefaultOutputDevice = exception.Reason == AudioOutputFailureReason.Muted
                ? systemDefaultOutputDevice! with { IsMuted = true }
                : null;
            PreviewVoiceCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsSpeechOutputAvailable));
            NotifyOutputPolicyChanged();
            UpdateOutputDeviceAvailability();
            if (preserveResponseOnMute)
            {
                PreserveSpokenResponseFailure("Audio output is unavailable or muted. The complete visual response is retained; no endpoint is substituted.");
                return;
            }
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
            var mutedOutput = SelectedOutputDevice with { IsMuted = true };
            var outputIndex = OutputDevices.IndexOf(SelectedOutputDevice);
            suppressAudioDevicePreferenceSave = true;
            try
            {
                if (outputIndex >= 0)
                {
                    OutputDevices[outputIndex] = mutedOutput;
                }
                SetOutputDeviceSnapshot(mutedOutput);
            }
            finally
            {
                suppressAudioDevicePreferenceSave = false;
            }
            if (preserveResponseOnMute)
            {
                PreserveSpokenResponseFailure("Audio output is muted or volume is zero. The complete visual response is retained.");
                return;
            }
            ShowFailure("Audio output is muted.", exception.Message);
            return;
        }

        if (preserveResponseOnMute)
        {
            PreserveSpokenResponseFailure("The selected output could not be opened or playback failed. The saved pin is retained; no endpoint is substituted.");
            return;
        }
        SetOutputDeviceSnapshot(null);
        ShowFailure(unavailableTitle, exception.Message);
    }

    private void PreserveSpokenResponseFailure(string recovery)
    {
        spokenSummaryRecovery = recovery;
        forceVisualResponse = true;
        NotifyOutputPolicyChanged();
        if (CanRevealPrivatePresentation)
        {
            WindowActionRequested?.Invoke(this, WindowAction.Show);
        }
    }

    internal async Task ExecuteAsync(
        CommandDefinition command,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.System,
        Func<bool>? sourceCurrent = null)
    {
        if (!IsHostInputEligible)
        {
            ApplicationLog.Information(logger, "Denied command dispatch outside the eligible host generation");
            return;
        }
        if (command.Action == BuiltInAction.StopAllWork && initiator == SecurityAuditInitiator.ModelSuggestion)
        {
            ShowInformation("Model Stop All is unavailable.",
                "No model or reusable action grant can authorize global stopping. Use original typed input, activated voice or native All Work for a fresh exact review.");
            return;
        }
        if (initiator == SecurityAuditInitiator.ModelSuggestion)
        {
            ShowInformation("Model request unavailable.", "Nothing was sent. Use an original built-in command or session conversation.");
            return;
        }
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
                await HideApplicationAsync();
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
                    "Model, speech, audio, response, call, and readiness settings are available in the settings window.",
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
                ShowSettings();
                break;
            case BuiltInAction.ShowHelp:
                ShowInformation(
                    "Built-in commands are ready.",
                    $"{Commands.Count} deterministic commands are registered. Say “{AssistantName}, open documentation” and choose Commands for the full list. "
                    + "Use “list capabilities” for the admitted read-only registry, or “describe capability” followed by a canonical ID. "
                    + "Use “session help” for bounded exact-ID session observations and explicit lifecycle controls. "
                    + "Use “" + SessionDispositionReviewCommand.Syntax + "” for preview only in private native Sessions; confirmation is a separate native action. "
                    + SessionQueueCancellationReviewCommand.Syntax + ". " + SessionQueueCancellationReviewCommand.Boundary + " "
                    + "Use “list response window settings” for exact device-local visibility/topmost controls; no response is reopened. "
                    + $"Use “{AssistantName}, list assistant settings” for the display/PTT command-prefix name. "
                    + NaturalAppearanceCommand.Syntax + " "
                    + "Natural name commands: “what is your name”, “call yourself <name>”, “change your name to <name>”, or “reset your name”. "
                    + SettingsSectionCommand.Syntax + " "
                    + CurrentResponseCommand.Syntax + " "
                    + "Use “explain the clipboard” for exact native review and qualified local-only explanation; no hosted fallback. "
                    + "Use “show spoken summary limits” to inspect future ordinary-speech sentence/word budgets; “list speech settings” lists exact set/reset syntax. "
                    + "Use “open skill packages” or “open shared skill sources” for native review only; neither enables or runs skills. "
                    + PreparedLocalSearchCommand.Syntax + " "
                    + (!LocalModelsEnabled ? "Local model use is disabled; built-in commands remain available. " : string.Empty)
                    + "Model answers use session conversations. Unmatched requests without a session route are unavailable; nothing is sent.");
                break;
            case BuiltInAction.ShowVersion:
                ShowInformation(
                    $"{AssistantName} version",
                    $"{applicationInfo.Version} · local Windows speech · "
                    + (!LocalModelsEnabled
                        ? "local models disabled"
                        : Dependencies.Any(status =>
                        string.Equals(status.Id, "local.inference", StringComparison.Ordinal)
                        && status.Readiness == DependencyReadiness.Ready)
                        ? "local model ready"
                        : "local reasoning unavailable")
                    + Environment.NewLine + Environment.NewLine + DurableVersionQuery.StorageDisclosure);
                break;
            case BuiltInAction.ShowStatus:
                RefreshSubmittedWorkActivity();
                var outstanding = SetupTasks
                    .Where(task => task.State is SetupTaskState.Running
                        or SetupTaskState.NeedsAction or SetupTaskState.Failed)
                    .ToArray();
                ShowInformation(
                    outstanding.FirstOrDefault(task => task.State == SetupTaskState.Running) is { } active
                        ? $"Setting up {active.Name}."
                        : outstanding.Length > 0
                            ? "Setup needs attention."
                            : "Assistant status.",
                    (outstanding.Length > 0
                        ? string.Join(" ", outstanding.Select(task =>
                            $"{task.Name}: {task.State} — {task.Detail}"))
                        : IsVoiceEnabled ? ListeningStatus : "Enable listening or use the typed proof field.")
                    + Environment.NewLine + Environment.NewLine + SubmittedWorkActivityStatus);
                break;
            case BuiltInAction.ShowCurrentTaskProgress:
                var currentTask = dependencyBootstrapper.Tasks.ActiveTask;
                if (currentTask is not null)
                {
                    ShowInformation(
                        $"{currentTask.Name}: running.",
                        (currentTask.ProgressPercentage is { } percentage
                            ? $"Current stage: {currentTask.Detail} Completion: {percentage}%."
                            : $"Current stage: {currentTask.Detail} No completion percentage is available for this task.")
                        + Environment.NewLine + Environment.NewLine + SubmittedWorkActivityStatus);
                    break;
                }

                var nextTask = SetupTasks.FirstOrDefault(task =>
                    task.State is SetupTaskState.Queued or SetupTaskState.NeedsAction or SetupTaskState.Failed);
                ShowInformation(
                    "No setup task is running.",
                    (nextTask is null
                        ? "There is no active or pending setup task."
                        : $"{nextTask.Name}: {nextTask.State}. {nextTask.Detail}")
                    + Environment.NewLine + Environment.NewLine + SubmittedWorkActivityStatus);
                break;
            case BuiltInAction.CancelTask:
                await CancelCurrentTaskAsync();
                break;
            case BuiltInAction.StopAllWork:
                await StopAllWorkAsync(sourceCurrent);
                break;
            case BuiltInAction.StopSpeaking:
                await StopSpeakingAsync();
                break;
            case BuiltInAction.DisableListening:
                await DisableListeningAsync(initiator);
                break;
            case BuiltInAction.LockMachine:
                if (bundledAction is not null)
                {
                    await InvokeBundledAction(bundledAction, Kora.Core.Skills.FixedSessionAction.Lock, initiator, sourceCurrent);
                    break;
                }
                ReportHostInteractionFailure("Exact Lock admission is unavailable. No native fallback or named-action preference can authorize Lock.");
                break;
            case BuiltInAction.ProposeShutdown:
            case BuiltInAction.ProposeRestart:
                if (bundledAction is not null)
                {
                    await InvokeBundledAction(bundledAction, command.Action == BuiltInAction.ProposeShutdown
                        ? Kora.Core.Skills.FixedSessionAction.Shutdown : Kora.Core.Skills.FixedSessionAction.Restart,
                        initiator, sourceCurrent);
                    break;
                }
                var preparing = CreatePowerProposalAsync(command.Action, initiator);
                ApprovalPreparation = ApprovalPreparation.IsCompleted ? preparing : Task.WhenAll(ApprovalPreparation, preparing);
                if (!await preparing) { break; }
                PresentResponse(
                    AssistantState.Waiting,
                    command.Action == BuiltInAction.ProposeShutdown
                        ? "Shutdown request recognized."
                        : "Restart request recognized.",
                    "This bootstrap proves voice routing without executing disruptive power actions. No OS power request was sent.");
                break;
            case BuiltInAction.CancelPowerAction:
                if (bundledActionShutdown is not null)
                {
                    await bundledActionShutdown();
                    ShowInformation("Bundled operation retired.", bundledActionStatus?.Invoke()
                        ?? "No new native request can use the retired invocation. An already accepted Windows request cannot be undone.");
                    break;
                }
                var hadPendingAction = await CancelPendingPowerAuditAsync("user-cancelled");
                ShowInformation(
                    hadPendingAction ? "Power proposal cancelled." : $"No {AssistantName} power action is pending.",
                    "No operating-system action was sent.");
                break;
            case BuiltInAction.ShowPowerStatus:
                if (bundledActionStatus is not null)
                {
                    ShowInformation("Bundled operation status.", bundledActionStatus());
                    break;
                }
                ShowInformation(
                    pendingPowerAction switch
                    {
                        BuiltInAction.ProposeShutdown => "Shutdown proposal pending.",
                        BuiltInAction.ProposeRestart => "Computer restart proposal pending.",
                        _ => "No power action is pending.",
                    },
                    "Power execution is intentionally disabled in this bootstrap proof.");
                break;
            case BuiltInAction.ShowModelExecution:
                ShowInformation("Model execution settings", DescribeModelExecution());
                break;
            case BuiltInAction.EnableLocalModels:
                if (await TrySetModelExecutionSettingsAsync(true, hostedModelsEnabled, initiator))
                {
                    ShowSuccess("Local models enabled.", DescribeModelExecution());
                }
                break;
            case BuiltInAction.DisableLocalModels:
                if (await TrySetModelExecutionSettingsAsync(false, hostedModelsEnabled, initiator))
                {
                    ShowSuccess("Local models disabled.", DescribeModelExecution());
                }
                break;
            case BuiltInAction.EnableHostedModels:
                if (await TrySetModelExecutionSettingsAsync(localModelsEnabled, true, initiator))
                {
                    ShowSuccess("Hosted models enabled.", DescribeModelExecution());
                }
                break;
            case BuiltInAction.DisableHostedModels:
                if (await TrySetModelExecutionSettingsAsync(localModelsEnabled, false, initiator))
                {
                    ShowSuccess("Hosted models disabled.", DescribeModelExecution());
                }
                break;
            default:
                throw new InvalidOperationException($"Unsupported built-in action: {command.Action}.");
        }
    }

    private async Task RestartApplicationAsync(SecurityAuditInitiator initiator)
    {
        if (voluntaryShutdownRunning) { return; }
        voluntaryShutdownRunning = true;
        try
        {
            if (bundledActionShutdown is not null) { await bundledActionShutdown(); }
            if (ownedSkillAuthoringShutdown is not null && !await ownedSkillAuthoringShutdown()) { return; }
            var restartDispatched = false;
            var privacyRevision = privacyObservation.Current.TopologyRevision;
            bool Eligible() => IsCallMutationHostEligible
                && privacyObservation.Current.TopologyRevision == privacyRevision;
            var audit = await StartAuditAsync(
                SecurityAuditCategory.ApplicationExecution,
                ApplicationRestartAction,
                initiator,
                CurrentApplicationTarget);
            try
            {
                var conversationCurrent = await PrepareConversationShutdown();
                if (conversationCurrent is null)
                {
                    await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "conversation-shutdown-not-confirmed");
                    return;
                }
                using var submitted = await PrepareSubmittedWorkShutdown(Hosting.ApplicationLifecycleOperation.Restart, conversationCurrent);
                if (submitted is null)
                {
                    await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "submitted-work-review-cancelled");
                    return;
                }
                if (instructionEditorShutdown is not null && !await instructionEditorShutdown())
                {
                    await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "editor-shutdown-not-confirmed");
                    return;
                }
                if (customizationShutdown is not null && !await customizationShutdown())
                {
                    await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "customization-shutdown-not-confirmed");
                    return;
                }
                if (!RequireConversationShutdownCurrent(() => conversationCurrent() && submitted.IsCurrent)
                    || !Eligible())
                {
                    await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "submitted-work-review-changed");
                    return;
                }
                if (!await JoinSubmittedWorkShutdown(submitted))
                {
                    await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "submitted-work-release-unconfirmed");
                    return;
                }
                await CloseVoluntaryOperationsAsync();
                await StopAudioAsync();
#pragma warning disable VSTHRD003 // Join owned appearance writes before requesting process replacement.
                await AppearanceUpdate;
                await PreferenceUpdate;
                await preferencePersistence;
                await ApprovalPreparation;
                await consentPersistence;
                await AuditPersistence;
#pragma warning restore VSTHRD003
                await outputCatalog.WaitForQuiescenceAsync();
                if (!RequireConversationShutdownCurrent(() => conversationCurrent() && submitted.IsCurrent))
                {
                    await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "conversation-shutdown-changed");
                    return;
                }
                if (!Eligible())
                {
                    await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, "host-admission-changed");
                    ShowInformation("Host admission changed.", "Required ownership, privacy or model authority changed before restart; no restart was dispatched.");
                    return;
                }
                lifecycleAdmissionClosed = true;
                applicationProcessController.RestartCurrentApplication();
                restartDispatched = true;
                hostExitRequested = true;
                await CompleteAuditAsync(audit, SecurityAuditOutcome.Succeeded);
                WindowActionRequested?.Invoke(this, WindowAction.Close);
            }
            catch (InvalidOperationException exception)
            {
                if (!restartDispatched) { lifecycleAdmissionClosed = false; }
                await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed, "restart-failed");
                ApplicationLog.Error(logger, exception, "Restarting Kora");
                ShowFailure($"{AssistantName} could not restart.", exception.Message);
            }
        }
        finally { voluntaryShutdownRunning = false; }
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

    private async Task<bool> CreatePowerProposalAsync(
        BuiltInAction action, SecurityAuditInitiator initiator)
    {
        Interlocked.Increment(ref activeApprovalPreparations);
        try { return await CreatePowerProposalCoreAsync(action, initiator); }
        finally { Interlocked.Decrement(ref activeApprovalPreparations); }
    }

    private async Task<bool> CreatePowerProposalCoreAsync(
        BuiltInAction action,
        SecurityAuditInitiator initiator)
    {
        var preparation = Interlocked.Increment(ref approvalPreparationRevision);
        var eligible = CapturePreferenceEligibility(initiator);
        if (!eligible()) { return false; }
        await CancelPendingPowerAuditAsync("superseded");
        var requested = await StartAuditAsync(
            SecurityAuditCategory.SecurityApproval,
            action == BuiltInAction.ProposeShutdown
                ? ShutdownApprovalAction
                : RestartApprovalAction,
            initiator,
            CurrentMachineTarget,
            Guid.NewGuid());
        if (disposed) { return false; }
        if (!eligible() || preparation != Interlocked.Read(ref approvalPreparationRevision))
        {
            await CompleteAuditAsync(requested, SecurityAuditOutcome.Cancelled, "power-preparation-retired");
            return false;
        }
        pendingPowerAction = action;
        pendingPowerAudit = requested;
        return true;
    }

    private async Task<bool> CancelPendingPowerAuditAsync(string reasonCode)
    {
        if (pendingPowerAudit is null)
        {
            return false;
        }

        var audit = pendingPowerAudit;
        pendingPowerAudit = null;
        pendingPowerAction = null;
        await CompleteAuditAsync(audit, SecurityAuditOutcome.Cancelled, reasonCode);
        return true;
    }

    private async Task<SecurityAuditEvent> StartAuditAsync(
        SecurityAuditCategory category, string actionId, SecurityAuditInitiator initiator,
        string targetId, Guid? approvalId = null)
    {
        var audit = CreateAudit(category, actionId, initiator, targetId, approvalId);
        await WriteAuditAsync(audit);
        return audit;
    }

    private Task CompleteAuditAsync(SecurityAuditEvent audit, SecurityAuditOutcome outcome, string? reasonCode = null) =>
        WriteAuditAsync(audit.WithOutcome(outcome, reasonCode));

    private async Task WriteAuditAsync(SecurityAuditEvent audit, bool retiredResponseReadCancellation = false)
    {
        Interlocked.Increment(ref pendingAuditWrites);
        try
        {
            Task write;
            lock (auditPersistenceGate)
            {
                // Disposal cannot mint success, but an already-owned read must retain its cancelled terminal audit.
                var ownedReadCancellation = retiredResponseReadCancellation
                    && audit.Outcome == SecurityAuditOutcome.Cancelled
                    && responseReadAudit?.CorrelationId == audit.CorrelationId;
                ObjectDisposedException.ThrowIf(disposed && !ownedReadCancellation, this);
                write = securityAuditLog.WriteAsync(audit);
                auditPersistence = auditPersistence.IsCompleted ? write : Task.WhenAll(auditPersistence, write);
            }
            await write;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or AggregateException)
        {
            auditEvidenceUnavailable = true;
            throw;
        }
        finally { Interlocked.Decrement(ref pendingAuditWrites); }
    }

    private static SecurityAuditEvent CreateAudit(
        SecurityAuditCategory category, string actionId, SecurityAuditInitiator initiator,
        string targetId, Guid? approvalId = null) =>
        new(Guid.NewGuid(), category, actionId, SecurityAuditOutcome.Requested, initiator, targetId, approvalId);

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

    private void HandleCommandException(Exception exception)
    {
        try
        {
            ApplicationLog.Error(logger, exception, "Executing an asynchronous UI command");
        }
        finally
        {
            ShowFailure("The command failed.", exception.Message);
        }
    }

    private void PresentResponse(
        AssistantState responseState,
        string title,
        string body,
        bool requestWindow = true,
        bool refreshOutput = true,
        Action? sourceAssigned = null,
        FinalizedModelAnswer? modelAnswer = null)
    {
        RetireCurrentResponseSpeech();
        activeTaskResponseOutput = null;
        completedTaskResponseVisual = null;
        RetireWebResultDetails(retireControl: false);
        RetireSpeechCaptionSource();
        ClearResponseActions();
        State = responseState;
        spokenSummaryRecovery = null;
        ResponseTitle = title;
        if (modelAnswer is not null)
        {
            modelAnswer = modelAnswer with
            {
                OfferEpoch = detailPresentation is null ? 0 : detailPresentation.CaptureOfferEpoch(),
            };
            if (AnswerPresentation.From(modelAnswer.Body).HasDetails && modelAnswer.OfferEpoch is not null)
            { body += "\n\n" + AnswerPresentation.Offer; }
        }
        ResponseBody = body;
        forceVisualResponse = ShouldForceVisualResponse(
            IsResponseInteractionPending, responseState);
        sourceAssigned?.Invoke();
        BindCurrentResponseSpeech(!isInitializing && sourceAssigned is null
            && responseState is AssistantState.Information or AssistantState.Success
            && !IsResponseInteractionPending, modelAnswer);
        OnPropertyChanged(nameof(IsDetailsOfferPending));
        NotifyOutputPolicyChanged();
        if (!isInitializing && refreshOutput && SelectedOutputDevice is not null
            && responseState is not (AssistantState.Failure or AssistantState.Listening)
            && EffectiveResponseMode == ResponseOutputMode.VoiceOnly)
        {
            outputPresentationRevision = Interlocked.Read(ref webResponseRevision);
            ResponseOutputUpdate = RefreshResponseOutputAsync(outputPresentationRevision, requestWindow);
            return;
        }
        if (!isInitializing
            && !IsPrivacyPresentationHeld && sessionController.IsCurrentSessionUnlocked()
            && requestWindow
            && responseState != AssistantState.Listening
            && IsVisualResponseVisible)
        {
            WindowActionRequested?.Invoke(this, WindowAction.Show);
        }
    }

    private void NotifyOutputPolicyChanged()
    {
        NotifyResponseReadChanged();
        OnPropertyChanged(nameof(EffectiveResponseMode));
        OnPropertyChanged(nameof(IsVisualResponseVisible));
        OnPropertyChanged(nameof(IsSpeechResponseEnabled));
        OnPropertyChanged(nameof(ResponseOutputStatus));
        OnPropertyChanged(nameof(CanChangeAudioOutputDevice));
        OnPropertyChanged(nameof(AudioOutputConfigurationStatus));
        OnPropertyChanged(nameof(CanChangePlaybackVolume));
        OnPropertyChanged(nameof(CanChangePlaybackVolumeNative));
        OnPropertyChanged(nameof(CanInspectWindowsSpeechRate));
        OnPropertyChanged(nameof(CanChangeWindowsSpeechRate));
        OnPropertyChanged(nameof(CanInspectWindowsSpeechRateNative));
        OnPropertyChanged(nameof(CanChangeWindowsSpeechRateNative));
        OnPropertyChanged(nameof(CanChangeSpeechInputNative));
        OnPropertyChanged(nameof(CanChangeConversationalReplies));
        OnPropertyChanged(nameof(CanChangeForegroundPtt));
        OnPropertyChanged(nameof(CanControlLogonStartup));
        OnPropertyChanged(nameof(CanControlLogonStartupNative));
        OnPropertyChanged(nameof(CanControlStartupPresentation));
        OnPropertyChanged(nameof(CanControlDetailPresentation));
        OnPropertyChanged(nameof(CanControlDetailPresentationNative));
        OnPropertyChanged(nameof(WindowsSpeechRateStatus));
        OnPropertyChanged(nameof(PlaybackVolumeStatus));
        OnPropertyChanged(nameof(CanChangeDiagnosticRetention));
        OnPropertyChanged(nameof(CanChangeDiagnosticRetentionNative));
        OnPropertyChanged(nameof(DiagnosticRetentionStatus));
        OnPropertyChanged(nameof(CanChangeAuditRetention));
        OnPropertyChanged(nameof(CanChangeAuditRetentionNative));
        OnPropertyChanged(nameof(AuditRetentionStatus));
        RetireIneligibleAuditImpact();
        RetireIneligibleDiagnosticImpact();
        RetireIneligibleSessionRetentionPlan();
        OnPropertyChanged(nameof(CanChangeResponseMode));
        OnPropertyChanged(nameof(ResponseModeConfigurationStatus));
        OnPropertyChanged(nameof(CanChangeProviderMode));
        OnPropertyChanged(nameof(ProviderModeConfigurationStatus));
        OnPropertyChanged(nameof(CanChangeProviderProfile));
        OnPropertyChanged(nameof(CanSaveProviderService));
        OnPropertyChanged(nameof(CanSaveProviderProfile));
        OnPropertyChanged(nameof(ProviderProfileConfigurationStatus));
        NotifyPreapprovedUriControlChanged();
        OnPropertyChanged(nameof(IsInCallFeedbackOverrideApplied));
        OnPropertyChanged(nameof(InCallFeedbackStatus));
        OnPropertyChanged(nameof(CanInspectInCallFeedback));
        OnPropertyChanged(nameof(CanInspectInCallFeedbackNative));
        OnPropertyChanged(nameof(CanChangeInCallFeedbackNative));
        OnPropertyChanged(nameof(CanEnableCommunicationActivity));
        OnPropertyChanged(nameof(CanChangeCommunicationActivity));
        NotifyQuietHours();
    }

    private string[] GetRecognitionPhrases()
    {
        var commands = commandCatalog.GetActivationCommands(AssistantName)
            .SelectMany(command => command.AllPhrases)
            .Concat(ClipboardCommand.FixedPhrases)
            .Concat(ClipboardSnapshotStatusCommand.DiscoveryPhrases)
            .Concat(LocalFileCommand.FixedPhrases)
            .Concat(PreparedLocalSearchCommand.DiscoveryPhrases)
            .Concat(AttachmentCataloguePreparationCommand.DiscoveryPhrases)
            .Concat(SessionCommand.DiscoveryPhrases)
            .Concat(Kora.Application.Mcp.McpCommand.FixedPhrases)
            .Concat([Kora.Application.Tools.CustomizationDiscoveryCommand.Phrase])
            .Concat([Kora.Application.Tools.SessionMetadataCommand.Phrase])
            .Concat(WorkspaceSurfaceCommand.FixedPhrases)
            .Concat(EvidenceTraceCommand.DiscoveryPhrases)
            .Concat(["pending question help"])
            .Concat(NewRequestOpenCommand.FixedPhrases)
            .Concat(MemoryCommand.DiscoveryPhrases)
            .Concat(AssistantNameCommand.DiscoveryPhrases)
            .Concat(InputDeviceCommand.FixedPhrases)
            .Concat(OutputDeviceCommand.FixedPhrases)
            .Concat(PlaybackVolumeCommand.FixedPhrases)
            .Concat(WindowsSpeechRateCommand.FixedPhrases)
            .Concat(SpeechInputCommand.FixedPhrases)
            .Concat(QuietHoursCommand.FixedPhrases)
            .Concat(ForegroundPttCommand.FixedPhrases)
            .Concat(SpokenApprovalReply.FixedPhrases)
            .Concat(VoiceCaptureTimingCommand.FixedPhrases)
            .Concat(SpeechComparisonCommand.FixedPhrases)
            .Concat(DiagnosticRetentionCommand.FixedPhrases)
            .Concat(OrdinaryTurnDeadlineCommand.FixedPhrases)
            .Concat(AuditRetentionCommand.FixedPhrases)
            .Concat(ManualCallCommand.FixedPhrases)
            .Concat(ResponseModeCommand.FixedPhrases)
            .Concat(ConversationExecutionCommand.FixedPhrases)
            .Concat(ResponseWindowCommand.FixedPhrases)
            .Concat(ProviderModeCommand.FixedPhrases)
            .Concat(ProviderProfileCommand.FixedPhrases)
            .Concat(SessionProviderProfileCommand.FixedPhrases)
            .Concat(PreapprovedUriCommand.FixedPhrases)
            .Concat(WebPageCommand.FixedPhrases)
            .Concat(WebSummaryCommand.FixedPhrases)
            .Concat<string>(webSummaryReview is { } summaryReview
                ? [WebSummaryCommand.ConfirmPrefix + summaryReview.Id.ToString("D")] : [])
            .Concat(InCallFeedbackCommand.FixedPhrases)
            .Concat(CommunicationActivityCommand.FixedPhrases)
            .Concat(SpeechTextCommand.FixedPhrases)
            .Concat(ResponseReadCommand.FixedPhrases)
            .Concat(CallSpeechCommand.FixedPhrases)
            .Concat(CurrentResponseCommand.FixedPhrases)
            .Concat(SpeechCommand.FixedPhrases)
            .Concat(SpeechSummarySuggestionCommand.FixedPhrases)
            .Concat(PlaybackVolumeSuggestionCommand.FixedPhrases)
            .Concat(WindowsSpeechRateSuggestionCommand.FixedPhrases)
            .Concat(InstalledSpeechVoiceSuggestionCommand.FixedPhrases)
            .Concat(RegisteredSettingSuggestionCommand.FixedPhrases)
            .Concat(RegisteredSettingSuggestionCommand.ResponseTimeoutPhrases)
            .Concat(RegisteredSettingUndoCommand.FixedPhrases)
            .Concat(Kora.Application.Skills.OwnedSkillAuthoringCommand.FixedPhrases)
            .Concat(Kora.Application.Skills.SkillReviewCommand.FixedPhrases)
            .Concat(SettingsSectionCommand.FixedPhrases)
            .Concat(LogonStartupCommand.FixedPhrases)
            .Concat(DetailPresentationCommand.FixedPhrases)
            .Concat(MarkdownDisplayCommand.FixedPhrases)
            .Concat<string>(HasDetailsQuestion ? ["yes", "show it", "no", "not now"] : [])
            .Concat(Kora.Core.Maintenance.MaintenanceCommandParser.FixedPhrases)
            .Concat(Kora.Core.Maintenance.MaintenanceUpdateCommand.FixedPhrases)
            .Concat(Kora.Core.Interaction.LocalEventCommand.FixedPhrases)
            .Concat<string>(ClipboardPreview is { } snapshot
                ? ["reuse clipboard snapshot " + snapshot.SnapshotId.ToString("D")] : []);
        return AssistantNameRules.WithActivationNames(commands, AssistantName)
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
        commandCatalog.GetActivationCommands(normalizedName);
        var previousDefaultCommand = $"{AssistantName}, what can you do?";
        var replaceDefaultCommand = string.Equals(
            CommandText,
            previousDefaultCommand,
            StringComparison.Ordinal);
        if (!AssistantName.Equals(normalizedName, StringComparison.Ordinal))
        {
            RetireSessionCommandReviews();
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
            OnPropertyChanged(nameof(CallVisualOverrideStatus));
            OnPropertyChanged(nameof(ResponseOutputStatus));
            OnPropertyChanged(nameof(VoiceConsentDescription));
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
        if (!isAssistantNameAvailable)
        {
            AssistantNameSettingStatus = assistantNameConfiguration.Get().Recovery!;
            return;
        }
        try
        {
            var normalizedName = AssistantNameRules.Normalize(AssistantNameInput);
            AssistantNameSettingStatus = string.Equals(
                normalizedName,
                AssistantName,
                StringComparison.Ordinal)
                ? $"The current name is {AssistantName}."
                : $"Apply to use {normalizedName} on screen, in spoken responses and when addressing the assistant.";
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