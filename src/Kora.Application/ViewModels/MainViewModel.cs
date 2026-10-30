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
    private const string ModelActionApprovalPrefix = "model.action.";
    private const string ModelApprovalPreferenceAction = "configuration.model-approval";
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
    private readonly ILocalModelReasoner localModelReasoner;
    private readonly IModelApprovalPreferences modelApprovalPreferences;
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
    private long reusableGrantRevision;
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
    private CancellationTokenSource? activeReasoningCancellation;
    private Task? activeReasoningTask;
    private LocalModelArtifact? activeReasoningArtifact;
    private bool isLocalTaskCancellable;
    private bool isModelActionDispatchActive;
    private bool isModelApprovalPromptActive;
    private bool localModelsEnabled = ModelExecutionSettings.Default.LocalModelsEnabled;
    private bool hostedModelsEnabled = ModelExecutionSettings.Default.HostedModelsEnabled;
    private int stoppingAudioOperations;
    private BuiltInAction? pendingModelAction;
    private LocalModelArtifact? pendingModelActionArtifact;
    private SecurityAuditEvent? pendingModelActionAudit;
    private Func<HostActivity>? pendingModelQuestionContinuation;
    private GrantChange? pendingGrantChange;
    private SecurityAuditEvent? pendingGrantChangeAudit;
    private LocalModelQuestion? pendingModelQuestion;
    private string? pendingQuestionRequest;
    private LocalModelArtifact? pendingQuestionArtifact;
    private int pendingQuestionDepth;
    private bool isGrantEditorVisible;
    private CommandDefinition? selectedGrantAction;
    private GrantChangeOperation selectedGrantOperation;
    private ModelApprovalScope selectedGrantScope = ModelApprovalScope.Session;
    private ModelApprovalScope selectedGrantTargetScope = ModelApprovalScope.Always;
    private readonly HashSet<BuiltInAction> sessionAllowedModelActions = [];
    private readonly HashSet<BuiltInAction> alwaysAllowedModelActions = [];
    private bool requireAssistantNameForVoiceApproval = true;
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
        ILocalModelReasoner localModelReasoner,
        IModelApprovalPreferences modelApprovalPreferences,
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
        SpeechSetupComparison? speechSetupComparison = null,
        LogonStartupConfigurationService? logonStartupConfiguration = null)
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
        this.localModelReasoner = localModelReasoner;
        this.modelApprovalPreferences = modelApprovalPreferences;
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

        AsyncCommand CreateCommand(Func<Task> execute, Func<bool>? canExecute = null) =>
            new(() => Kora.Application.Hosting.HostRequestRunner.RunAsync(
                    HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi, execute),
                exception => Kora.Application.Hosting.HostRequestRunner.Run(
                    HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi,
                    () => HandleCommandException(exception)), canExecute);

        InitializeSpeechInputConfiguration(speechInputConfiguration, execute => CreateCommand(execute));
        InitializeSpeechComparison(speechSetupComparison, execute => CreateCommand(execute));
        InitializeConversationalReplies(conversationalReplies, execute => CreateCommand(execute));
        InitializeLogonStartup(logonStartupConfiguration, execute => CreateCommand(execute));
        ResetThemeCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.Theme));
        ResetReaderTextScaleCommand = CreateCommand(() => ResetAppearanceOptionAsync(AppearanceOption.ReaderTextScale));
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
        RefreshProviderProfileCommand = CreateCommand(() => RunNativeProviderProfileAsync(new(AppearanceCommandOperation.Get)));
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
        BeginPushToTalkCommand = CreateCommand(
            BeginPushToTalkAsync,
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
            () => !IsBusy && !IsLocalModelSetupActive && !IsPowerShellSetupActive
                && activeReasoningCancellation is null);
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
                      || IsModelActionReviewCommand(CommandText)
                      || MemoryCommand.Parse(CommandText, AssistantName) is not null
                      || IsInstructionEditorCommand(CommandText)
                      || Kora.Application.Definitions.CustomizationCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Tools.CustomizationDiscoveryCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Skills.OwnedSkillAuthoringCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Skills.SkillReviewCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Skills.SharedSkillInvocationCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Application.Skills.NamedSkillCommand.Parse(CommandText, AssistantName) is not null
                      || NewRequestOpenCommand.Parse(CommandText, AssistantName) is not null
                      || Kora.Core.Interaction.LocalEventCommand.Parse(CommandText, AssistantName) is not null
                      || ManualCallCommand.Parse(CommandText, AssistantName) is not null));
        PreviewVoiceCommand = CreateCommand(
            PreviewVoiceAsync,
            () => SelectedVoice is not null && SelectedOutputDevice is not null
                && EffectiveOutputDevice is { IsMuted: false }
                && !IsBusy && !IsListening && !voiceRecognition.IsListening && !speechInterruptionActive);
        StopSpeechCommand = CreateCommand(StopSpeakingAsync, () => IsSpeaking);
        speechReadOnce = new Kora.Tools.Speech.SpeechReadOnce(this);
        ReadCurrentResponseCommand = CreateCommand(
            () => ReadCurrentResponseAsync(CaptureResponseReadInput()), () => CanReadCurrentResponse);
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
        ApproveModelActionCommand = CreateCommand(
            () => ApproveModelActionAsync(ModelApprovalScope.Once),
            () => IsModelActionApprovalPending && !IsBusy && !IsLocalModelSetupActive
                && !IsPowerShellSetupActive);
        ApproveModelActionForSessionCommand = CreateCommand(
            () => ApproveModelActionAsync(ModelApprovalScope.Session),
            () => IsModelActionApprovalPending && !IsBusy && !IsLocalModelSetupActive
                && !IsPowerShellSetupActive && pendingModelActionArtifact is null);
        ApproveModelActionAlwaysCommand = CreateCommand(
            () => ApproveModelActionAsync(ModelApprovalScope.Always),
            () => IsModelActionApprovalPending && !IsBusy && !IsLocalModelSetupActive
                && !IsPowerShellSetupActive && pendingModelActionArtifact is null);
        RejectModelActionCommand = CreateCommand(
            RejectPendingModelActionAsync,
            () => IsModelActionApprovalPending);
        ApproveModelActionCommand.CanExecuteChanged += OnModelActionReviewAvailabilityChanged;
        ApproveModelActionForSessionCommand.CanExecuteChanged += OnModelActionReviewAvailabilityChanged;
        ApproveModelActionAlwaysCommand.CanExecuteChanged += OnModelActionReviewAvailabilityChanged;
        RejectModelActionCommand.CanExecuteChanged += OnModelActionReviewAvailabilityChanged;
        PrepareGrantChangeCommand = CreateCommand(
            async () =>
            {
                if (SelectedGrantAction is { } command)
                {
                    await PrepareGrantChangeAsync(new GrantChange(
                        SelectedGrantOperation,
                        command.Action,
                        SelectedGrantScope,
                        SelectedGrantOperation == GrantChangeOperation.Move
                            ? SelectedGrantTargetScope : null));
                }
                if (IsGrantChangePending)
                {
                    isModelApprovalPromptActive = true;
                    try
                    {
                        await SpeakModelApprovalPromptAsync();
                    }
                    catch (Exception exception) when (exception is InvalidOperationException
                        or UnauthorizedAccessException or IOException)
                    {
                        ApplicationLog.Error(logger, exception, "Speaking a grant-change confirmation prompt");
                        ShowFailure("The spoken grant question could not complete.",
                            (pendingGrantChange is { } pending
                                ? $"{DescribeGrantChange(pending)} The grant change is still awaiting your decision. "
                                : "The grant change is no longer pending. ")
                            + exception.Message);
                    }
                    finally
                    {
                        isModelApprovalPromptActive = false;
                    }
                }
            },
            () => IsGrantEditorVisible && SelectedGrantAction is not null);
        ConfirmGrantChangeCommand = CreateCommand(
            () => ConfirmGrantChangeAsync(SecurityAuditInitiator.LocalUser),
            () => IsGrantChangePending);
        RejectGrantChangeCommand = CreateCommand(
            RejectPendingGrantChangeAsync,
            () => IsGrantChangePending);

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

    public event EventHandler? SettingsRequested;
    public event EventHandler? ReadinessRequested;

    public event EventHandler? DocumentationRequested;

    public event EventHandler<string>? GrantDocumentRequested;
    public event EventHandler<string>? GrantDocumentChanged;

    public ObservableCollection<MicrophoneDevice> Microphones { get; } = [];

    public ObservableCollection<SpeechProvider> SpeechProviders { get; } = [];

    public ObservableCollection<SpeechVoice> Voices { get; } = [];

    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = [];

    public ObservableCollection<DependencyStatus> Dependencies { get; } = [];

    public ObservableCollection<ArtifactCommandOption> ArtifactCommandOptions { get; } = [];

    public IReadOnlyList<SetupTask> SetupTasks => dependencyBootstrapper.Tasks.Tasks;

    public bool IsModelActionApprovalPending => pendingModelAction is not null;
    public bool IsModelQuestionPending => pendingModelQuestion is not null;
    public bool IsResponseInteractionPending => IsApprovalPending || IsModelQuestionPending;
    public IReadOnlyList<ResponseAction> ResponseActions { get; private set; } = [];
    public bool HasResponseActions => ResponseActions.Count > 0;
    public IReadOnlyList<ModelQuestionChoice> ModelQuestionChoices { get; private set; } = [];
    public string CommandInputPlaceholder => IsModelQuestionPending
        ? "Type an option or your own answer"
        : "Type a command or / for artifacts";
    public string CommandSubmitLabel => IsModelQuestionPending ? "Answer" : "Run";
    public bool IsGrantChangePending => pendingGrantChange is not null;
    public bool IsApprovalPending => IsModelActionApprovalPending || IsGrantChangePending;
    public bool IsGrantEditorVisible
    {
        get => isGrantEditorVisible;
        private set
        {
            if (SetProperty(ref isGrantEditorVisible, value))
            {
                PrepareGrantChangeCommand.NotifyCanExecuteChanged();
                forceVisualResponse = ShouldForceVisualResponse(
                    value, IsResponseInteractionPending, State);
                NotifyOutputPolicyChanged();
                if (ShouldShowGrantEditor(value, isInitializing))
                {
                    WindowActionRequested?.Invoke(this, WindowAction.Show);
                }
            }
        }
    }
    internal static bool ShouldForceVisualResponse(
        bool editorVisible, bool interactionPending, AssistantState state) =>
        editorVisible || interactionPending || state == AssistantState.Failure;

    internal static bool ShouldShowGrantEditor(bool editorVisible, bool initializing) =>
        editorVisible && !initializing;

    public IReadOnlyList<CommandDefinition> GrantActions => commandCatalog.GetCommands(AssistantName)
        .Where(command => command.SupportsModelSuggestion).ToArray();
    public IReadOnlyList<GrantChangeOperation> GrantOperations { get; } =
        Enum.GetValues<GrantChangeOperation>();
    public IReadOnlyList<ModelApprovalScope> GrantScopes { get; } =
        [ModelApprovalScope.Session, ModelApprovalScope.Always];
    public CommandDefinition? SelectedGrantAction
    {
        get => selectedGrantAction;
        set
        {
            if (SetProperty(ref selectedGrantAction, value))
            {
                PrepareGrantChangeCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public GrantChangeOperation SelectedGrantOperation
    {
        get => selectedGrantOperation;
        set => SetProperty(ref selectedGrantOperation, value);
    }
    public ModelApprovalScope SelectedGrantScope
    {
        get => selectedGrantScope;
        set => SetProperty(ref selectedGrantScope, value);
    }
    public ModelApprovalScope SelectedGrantTargetScope
    {
        get => selectedGrantTargetScope;
        set => SetProperty(ref selectedGrantTargetScope, value);
    }

    public bool RequireAssistantNameForVoiceApproval
    {
        get => modelPreferenceWriteActive || modelPreferencesUnavailable || requireAssistantNameForVoiceApproval;
        set => QueuePreference(nameof(RequireAssistantNameForVoiceApproval), () => SetVoiceApprovalNameAsync(value));
    }

    private async Task SetVoiceApprovalNameAsync(bool value)
    {
        if (!AdmitModelPreferenceChange() || !AdmitVoiceOptionMutation(ModelApprovalPreferenceAction)
            || value == requireAssistantNameForVoiceApproval) { return; }
        if (await SaveModelApprovalPreferencesAsync(value, alwaysAllowedModelActions))
        {
            SetProperty(ref requireAssistantNameForVoiceApproval, value, nameof(RequireAssistantNameForVoiceApproval));
        }
    }

    public IReadOnlyList<CommandDefinition> AlwaysAllowedModelActions =>
        commandCatalog.GetCommands(AssistantName)
            .Where(command => alwaysAllowedModelActions.Contains(command.Action))
            .ToArray();

    public IReadOnlyList<CommandDefinition> SessionAllowedModelActions =>
        commandCatalog.GetCommands(AssistantName)
            .Where(command => sessionAllowedModelActions.Contains(command.Action))
            .ToArray();

    public Task RevokeModelActionApprovalAsync(BuiltInAction action, ModelApprovalScope scope)
    {
        if (!Enum.IsDefined(action) || !Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(action), "The model approval is invalid.");
        }
        return RevokeModelActionApprovalCoreAsync(action, scope);
    }

    private async Task RevokeModelActionApprovalCoreAsync(BuiltInAction action, ModelApprovalScope scope)
    {

        if (scope == ModelApprovalScope.Session)
        {
            if (sessionAllowedModelActions.Remove(action))
            {
                Interlocked.Increment(ref reusableGrantRevision);
                OnPropertyChanged(nameof(SessionAllowedModelActions));
                GrantDocumentChanged?.Invoke(this, GetGrantDocument());
                ShowInformation("Session approval revoked.", $"{action} now requires approval again.");
            }
            return;
        }

        if (!alwaysAllowedModelActions.Contains(action))
        {
            return;
        }

        var remaining = alwaysAllowedModelActions.Where(item => item != action).ToArray();
        if (await SaveModelApprovalPreferencesAsync(requireAssistantNameForVoiceApproval, remaining))
        {
            alwaysAllowedModelActions.Remove(action);
            Interlocked.Increment(ref reusableGrantRevision);
            OnPropertyChanged(nameof(AlwaysAllowedModelActions));
            GrantDocumentChanged?.Invoke(this, GetGrantDocument());
            ShowInformation("Always approval revoked.", $"{action} now requires approval again.");
        }
    }

    private Task<bool> SaveModelApprovalPreferencesAsync(
        bool requireName,
        IEnumerable<BuiltInAction> allowedActions,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser, Func<bool>? eligible = null)
    {
        if (!AdmitModelPreferenceChange()) { return Task.FromResult(false); }
        var snapshot = new ModelApprovalPreferences(requireName, allowedActions.Order().ToArray());
        return SavePreferenceAsync(() => modelApprovalPreferences.Save(snapshot),
            ModelApprovalPreferenceAction, initiator, "model approval settings", eligible);
    }

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

        var localModelsWereEnabled = localModelsEnabled;
        localModelsEnabled = enableLocalModels;
        hostedModelsEnabled = enableHostedModels;
        OnPropertyChanged(nameof(LocalModelsEnabled));
        OnPropertyChanged(nameof(HostedModelsEnabled));
        OnPropertyChanged(nameof(HostedModelExecutionDescription));

        if (localModelsWereEnabled && !enableLocalModels
            && activeReasoningCancellation is { IsCancellationRequested: false } cancellation)
        {
            await cancellation.CancelAsync();
        }

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

#pragma warning disable VSTHRD003 // Exposes the owned reasoning operation for asynchronous observation without submitting another request.
    internal Task? ActiveReasoningTask => activeReasoningTask;
#pragma warning restore VSTHRD003

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
                NotifyModelActionReviewAvailability();
                OnPropertyChanged(nameof(CanInstallLocalModel));
                OnPropertyChanged(nameof(CanInstallPowerShell));
                RefreshCommand.NotifyCanExecuteChanged();
                ApproveModelActionCommand.NotifyCanExecuteChanged();
                ApproveModelActionForSessionCommand.NotifyCanExecuteChanged();
                ApproveModelActionAlwaysCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanInstallLocalModel => !IsBusy && !IsLocalModelSetupActive
        && !IsPowerShellSetupActive && activeReasoningCancellation is null;

    public bool IsPowerShellSetupActive
    {
        get => isPowerShellSetupActive;
        private set
        {
            if (SetProperty(ref isPowerShellSetupActive, value))
            {
                NotifyModelActionReviewAvailability();
                OnPropertyChanged(nameof(CanInstallPowerShell));
                OnPropertyChanged(nameof(CanInstallLocalModel));
                RefreshCommand.NotifyCanExecuteChanged();
                ApproveModelActionCommand.NotifyCanExecuteChanged();
                ApproveModelActionForSessionCommand.NotifyCanExecuteChanged();
                ApproveModelActionAlwaysCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanInstallPowerShell => !IsBusy && !IsPowerShellSetupActive
        && !IsLocalModelSetupActive && activeReasoningCancellation is null;

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

    public AsyncCommand ApproveModelActionCommand { get; }

    public AsyncCommand ApproveModelActionForSessionCommand { get; }

    public AsyncCommand ApproveModelActionAlwaysCommand { get; }

    public AsyncCommand RejectModelActionCommand { get; }
    public AsyncCommand PrepareGrantChangeCommand { get; }
    public AsyncCommand ConfirmGrantChangeCommand { get; }
    public AsyncCommand RejectGrantChangeCommand { get; }

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
        InCallFeedbackRules.Resolve(ResponseOutputModeResolver.Resolve(DefaultResponseMode, QueueResponseMode, TaskResponseMode),
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
        || EffectiveResponseMode != ResponseOutputMode.VoiceOnly
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
        && !voiceRecognition.IsListening
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
        ? "On · Kora stays quiet during protected calls and shows the full response on screen"
        : "Off · other call, audio and privacy protections still apply";

    public string CallVoiceActivationButtonText => AllowVoiceActivationDuringCalls
        ? "Disable voice activation during calls"
        : "Keep voice activation during calls";

    public string CallVoiceActivationStatus => AllowVoiceActivationDuringCalls
        ? $"On · {AssistantName} can continue listening during detected calls"
        : "Off · listening closes and remains unavailable during detected calls";

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
                ? " Kora stays quiet during this call and shows the full response on screen. Listening has its own setting."
                : string.Empty;
            var modeLabel = ResponseModeOptions.Single(
                option => option.Mode == EffectiveResponseMode).Label;
            return $"{scope}: {modeLabel}.{callOverride}{fallback} {spokenSummaryRecovery}".TrimEnd();
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
                OnPropertyChanged(nameof(PresenceState));
            }
        }
    }

    public string StateLabel => State.GetLabel();

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
            if (SetProperty(ref isSpeaking, value))
            {
                if (!value)
                {
                    SetSpeechPlaybackFrame(SpeechPlaybackFrame.Inactive);
                }
                StopSpeechCommand.NotifyCanExecuteChanged();
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
                NotifyModelActionReviewAvailability();
                ToggleListeningCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanPushToTalk));
                BeginPushToTalkCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                RunTypedCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanInstallLocalModel));
                OnPropertyChanged(nameof(CanInstallPowerShell));
                PreviewVoiceCommand.NotifyCanExecuteChanged();
                ApplyAssistantNameCommand.NotifyCanExecuteChanged();
                ApproveModelActionCommand.NotifyCanExecuteChanged();
                ApproveModelActionForSessionCommand.NotifyCanExecuteChanged();
                ApproveModelActionAlwaysCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasVoiceConsent => voiceConsent == true;

    public bool NeedsVoiceConsent => voiceConsent is null;

    public bool IsVoiceEnabled => Volatile.Read(ref voiceEnabled) != 0;

    public bool IsPushToTalkVisible => !IsVoiceEnabled;

    public bool CanPushToTalk =>
        Volatile.Read(ref listeningStopRequests) == 0 && (listeningStop.CanRecover || CanRetireWakeForManualInput)
        && IsVoiceReadinessEligible(privacyObservation.Current)
        && IsHostInputEligible
        && microphoneCatalogCurrent
        && !IsListening
        && (!IsBusy || IsSpeaking);

    public long MicrophoneTopologyRevision => Interlocked.Read(ref microphoneTopologyRevision);

    public string VoiceConsentDescription =>
        "Your listening permission is saved for your Windows account on this device. Voice commands are processed on this device. "
        + "Wake-word listening is not available: Kora does not record or transcribe background audio. "
        + "Use push-to-talk in the response window or Hold to speak in the foreground Customizations editor when offered; Settings only manages listening permission and readiness. "
        + "Disable listening turns the microphone off until you enable it again. Remove listening permission also keeps it off after a restart. "
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
                        "Kora could not confirm that the Windows session is unlocked. Use Enable listening after returning to an unlocked interactive session.");
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
            IsGrantEditorVisible, IsResponseInteractionPending, State);
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
                    $"Your previously selected {providerName} is no longer available or its assets are incomplete. Would you like to review Speech and audio settings? Kora will not download or replace it automatically.",
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
            await ClearPendingModelActionAsync("superseded");
            await ClearPendingGrantChangeAsync("superseded");
            ClearPendingModelQuestion();
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

        if (IsResponseInteractionPending)
        {
            await ClearPendingModelActionAsync("superseded");
            await ClearPendingGrantChangeAsync("superseded");
            ClearPendingModelQuestion();
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

            ApplyDependencyStatus(status);
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
        if (!sessionController.IsCurrentSessionUnlocked() || lifecycleAdmissionClosed)
        {
            ApplicationLog.Information(logger, "Denied revealing the application outside an eligible Windows session");
            return;
        }
        Interlocked.Exchange(ref privacyPresentationHeld, 0);
        OnPropertyChanged(nameof(IsPrivacyPresentationHeld));
        if (State == AssistantState.Hidden)
        {
            State = AssistantState.Information;
        }

        ShowPresentation();
    }

    public void HideApplication() => new AsyncCommand(HideApplicationAsync, HandleCommandException).Execute(null);

    public async Task HideApplicationAsync()
    {
        Interlocked.Increment(ref approvalPreparationRevision);
        RetireWebResultDetails();
        var actionRetirement = ClearPendingModelActionAsync("dismissed");
        var grantRetirement = ClearPendingGrantChangeAsync("dismissed");
        ClearPendingModelQuestion();

        ClearResponseActions();
        IsGrantEditorVisible = false;
        State = AssistantState.Hidden;
        HidePresentation();
        await Task.WhenAll(actionRetirement, grantRetirement);
    }

    public void ShowPresentation()
    {
        if (!IsPrivacyPresentationHeld && sessionController.IsCurrentSessionUnlocked())
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
            $"The presence has been hidden to avoid blocking other windows. Restart Kora to try again. {detail}");

    public void ReportHostInteractionFailure(string detail) =>
        ShowFailure("Native question unavailable.", detail);

    public void ShowSettings()
    {
        if (!sessionController.IsCurrentSessionUnlocked() || lifecycleAdmissionClosed)
        {
            ApplicationLog.Information(logger, "Denied opening settings outside an eligible Windows session");
            return;
        }
        Interlocked.Exchange(ref privacyPresentationHeld, 0);
        OnPropertyChanged(nameof(IsPrivacyPresentationHeld));
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowReadiness() => ReadinessRequested?.Invoke(this, EventArgs.Empty);

    public void ShowDocumentation() =>
        DocumentationRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? SessionsRequested;

    public void ShowSessions() => SessionsRequested?.Invoke(this, EventArgs.Empty);

    public string GetGrantDocument()
    {
        var builder = new StringBuilder()
            .Append("# ").Append(AssistantName).AppendLine(" model-action grants")
            .AppendLine()
            .AppendLine("These grants apply only to the named built-in action. No file, script, or arbitrary API access is granted.")
            .AppendLine();
        AppendGrantSection(builder, "This session", sessionAllowedModelActions);
        AppendGrantSection(builder, "Always on this device", alwaysAllowedModelActions);
        builder.AppendLine("## Change a grant")
            .AppendLine()
            .AppendLine("Say \"manage grants\" to prepare an add, edit, or removal. Every proposed change must be confirmed in the response window.");
        return builder.ToString();
    }

    public Task PrepareGrantChangeAsync(
        GrantChange proposal,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!Enum.IsDefined(proposal.Action) || !Enum.IsDefined(proposal.Operation)
            || proposal.Scope is not (ModelApprovalScope.Once or ModelApprovalScope.Session or ModelApprovalScope.Always)
            || proposal.TargetScope is { } target
                && target is not (ModelApprovalScope.Session or ModelApprovalScope.Always))
        {
            throw new ArgumentException("The proposed grant change is invalid.", nameof(proposal));
        }
        var preparing = PrepareGrantChangeTrackedAsync(proposal, initiator);
        ApprovalPreparation = ApprovalPreparation.IsCompleted ? preparing : Task.WhenAll(ApprovalPreparation, preparing);
        return preparing;
    }

    private async Task PrepareGrantChangeTrackedAsync(GrantChange proposal, SecurityAuditInitiator initiator)
    {
        Interlocked.Increment(ref activeApprovalPreparations);
        try { await PrepareGrantChangeCoreAsync(proposal, initiator); }
        finally { Interlocked.Decrement(ref activeApprovalPreparations); }
    }

    private async Task PrepareGrantChangeCoreAsync(GrantChange proposal, SecurityAuditInitiator initiator)
    {
        var preparation = Interlocked.Increment(ref approvalPreparationRevision);
        var eligible = CapturePreferenceEligibility(initiator);
        if (!eligible())
        {
            ShowFailure("Grant change unavailable.", "Restore the owning unlocked host and required audit availability before a fresh preparation.");
            return;
        }

        if (IsApprovalPending)
        {
            await ClearPendingModelActionAsync("superseded");
            await ClearPendingGrantChangeAsync("superseded");
        }
        ClearPendingModelQuestion();

        var inSession = sessionAllowedModelActions.Contains(proposal.Action);
        var inAlways = alwaysAllowedModelActions.Contains(proposal.Action);
        var scope = proposal.Scope;
        if (scope == ModelApprovalScope.Once)
        {
            if (proposal.Operation == GrantChangeOperation.Add)
            {
                ShowInformation("Choose a grant scope.", "Select This session or Always before adding a grant.");
                return;
            }
            if (inSession == inAlways)
            {
                ShowInformation("Select the exact existing grant.",
                    inSession
                        ? $"{proposal.Action} has both a session and an always grant. Choose which one to change."
                        : $"{proposal.Action} has no existing grant to change.");
                IsGrantEditorVisible = true;
                return;
            }
            scope = inSession ? ModelApprovalScope.Session : ModelApprovalScope.Always;
        }

        if (IsGrantChangeInapplicable(proposal, scope, inSession, inAlways))
        {
            ShowInformation("Grant change cannot be prepared.",
                $"Check the existing {proposal.Action} grants and select an applicable operation and scope.");
            IsGrantEditorVisible = true;
            return;
        }

        var requested = await StartAuditAsync(
            SecurityAuditCategory.SecurityApproval,
            $"grant.{proposal.Operation.ToString().ToLowerInvariant()}.{proposal.Action.ToString().ToLowerInvariant()}",
            initiator,
            DeviceLocalPreferencesTarget,
            Guid.NewGuid());
        if (disposed) { return; }
        if (!eligible() || preparation != Interlocked.Read(ref approvalPreparationRevision))
        {
            await CompleteAuditAsync(requested, SecurityAuditOutcome.Cancelled, "grant-preparation-retired");
            return;
        }
        pendingGrantChange = proposal with { Scope = scope };
        pendingGrantChangeAudit = requested;
        IsGrantEditorVisible = false;
        OnPropertyChanged(nameof(IsGrantChangePending));
        OnPropertyChanged(nameof(IsApprovalPending));
        OnPropertyChanged(nameof(IsResponseInteractionPending));
        ConfirmGrantChangeCommand.NotifyCanExecuteChanged();
        RejectGrantChangeCommand.NotifyCanExecuteChanged();
        ShowInformation(
            "Confirm exact grant change?",
            DescribeGrantChange(pendingGrantChange)
                + $" This changes only permission for model suggestions; it will not execute {proposal.Action}. "
                + (RequireAssistantNameForVoiceApproval
                    ? $"Say “{AssistantName}, approve once” or “{AssistantName}, reject”, or use the buttons below."
                    : "Say “approve once” or “reject”, or use the buttons below."));
        WindowActionRequested?.Invoke(this, WindowAction.Show);
    }

    internal static bool IsGrantChangeInapplicable(
        GrantChange proposal, ModelApprovalScope scope, bool inSession, bool inAlways)
    {
        var exists = scope == ModelApprovalScope.Session ? inSession : inAlways;
        return proposal.Operation == GrantChangeOperation.Add && (exists || proposal.TargetScope is not null)
            || proposal.Operation == GrantChangeOperation.Remove && (!exists || proposal.TargetScope is not null)
            || proposal.Operation == GrantChangeOperation.Move
                && (!exists || proposal.TargetScope is null || proposal.TargetScope == scope
                    || (proposal.TargetScope == ModelApprovalScope.Session ? inSession : inAlways));
    }

    internal static string DescribeGrantChange(GrantChange change) =>
        change.Operation switch
        {
            GrantChangeOperation.Add => $"Add {change.Scope} grant for {change.Action}.",
            GrantChangeOperation.Remove => $"Remove {change.Scope} grant for {change.Action}.",
            GrantChangeOperation.Move =>
                $"Move {change.Action} grant from {change.Scope} to {change.TargetScope}.",
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

    public async Task ConfirmGrantChangeAsync(
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        if (!IsHostInputEligible)
        {
            ApplicationLog.Information(logger, "Denied grant confirmation outside the eligible host generation");
            return;
        }
        if (pendingGrantChange is not { } change)
        {
            throw new InvalidOperationException("No grant change is awaiting confirmation.");
        }

        var approval = pendingGrantChangeAudit;
        var preparation = Interlocked.Read(ref approvalPreparationRevision);
        try
        {
            await StopModelApprovalPromptAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            ApplicationLog.Error(logger, exception, "Stopping grant confirmation speech");
            ShowFailure("Could not stop the spoken grant question.",
                $"The grant change is still pending. {exception.Message}");
            return;
        }
        if (!ReferenceEquals(pendingGrantChangeAudit, approval))
        {
            return;
        }
        if (!IsHostInputEligible)
        {
            ApplicationLog.Information(logger, "Denied grant confirmation after a Windows privacy transition");
            return;
        }

        var inScope = change.Scope == ModelApprovalScope.Session
            ? sessionAllowedModelActions.Contains(change.Action)
            : alwaysAllowedModelActions.Contains(change.Action);
        var targetExists = change.TargetScope == ModelApprovalScope.Session
            ? sessionAllowedModelActions.Contains(change.Action)
            : alwaysAllowedModelActions.Contains(change.Action);
        if (change.Operation == GrantChangeOperation.Add && inScope
            || change.Operation != GrantChangeOperation.Add && !inScope
            || change.Operation == GrantChangeOperation.Move && targetExists)
        {
            await ClearPendingGrantChangeAsync("stale-grant");
            ShowFailure("The grant changed before confirmation.", "Review the current grants and prepare a new change.");
            return;
        }

        var expectedGrantRevision = Interlocked.Read(ref reusableGrantRevision);
        var nextAlways = alwaysAllowedModelActions.ToHashSet();
        if (change.Scope == ModelApprovalScope.Always
            || change.TargetScope == ModelApprovalScope.Always)
        {
            var next = nextAlways;
            if (change.Scope == ModelApprovalScope.Always)
            {
                if (change.Operation == GrantChangeOperation.Add)
                {
                    next.Add(change.Action);
                }
                else
                {
                    next.Remove(change.Action);
                }
            }
            else if (change.TargetScope == ModelApprovalScope.Always)
            {
                next.Add(change.Action);
            }
            if (!await SaveModelApprovalPreferencesAsync(requireAssistantNameForVoiceApproval, next, initiator,
                () => ReferenceEquals(pendingGrantChangeAudit, approval)
                    && Interlocked.Read(ref reusableGrantRevision) == expectedGrantRevision))
            {
                return;
            }
        }
        if (!ReferenceEquals(pendingGrantChangeAudit, approval) || !IsHostInputEligible) { return; }
        await ClearPendingGrantChangeAsync("approved-once");
        if (!IsHostInputEligible || preparation != Interlocked.Read(ref approvalPreparationRevision)) { return; }
        if (modelPreferenceWriteActive || Interlocked.Read(ref reusableGrantRevision) != expectedGrantRevision)
        {
            ShowFailure("The grant changed before confirmation.", "A concurrent revocation or grant change retired this continuation. Inspect current grants and receipts; no automatic retry.");
            return;
        }
        Interlocked.Increment(ref reusableGrantRevision);
        if (change.Scope == ModelApprovalScope.Always || change.TargetScope == ModelApprovalScope.Always)
        {
            alwaysAllowedModelActions.Clear();
            alwaysAllowedModelActions.UnionWith(nextAlways);
            OnPropertyChanged(nameof(AlwaysAllowedModelActions));
        }

        if (change.Scope == ModelApprovalScope.Session)
        {
            if (change.Operation == GrantChangeOperation.Add)
            {
                sessionAllowedModelActions.Add(change.Action);
            }
            else
            {
                sessionAllowedModelActions.Remove(change.Action);
            }
        }
        else if (change.TargetScope == ModelApprovalScope.Session)
        {
            sessionAllowedModelActions.Add(change.Action);
        }
        OnPropertyChanged(nameof(SessionAllowedModelActions));
        GrantDocumentChanged?.Invoke(this, GetGrantDocument());
        ShowSuccess("Grant changed.", DescribeGrantChange(change));
    }

    public async Task RejectPendingGrantChangeAsync()
    {
        if (!IsGrantChangePending)
        {
            return;
        }
        var approval = pendingGrantChangeAudit;
        try
        {
            await StopModelApprovalPromptAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            ApplicationLog.Error(logger, exception, "Stopping declined grant confirmation speech");
            ShowFailure("Could not stop the spoken grant question.",
                $"The grant change is still pending. {exception.Message}");
            return;
        }
        if (ReferenceEquals(pendingGrantChangeAudit, approval))
        {
            await ClearPendingGrantChangeAsync("user-declined");
            ShowInformation("Grant change declined.", "No grant was changed.");
        }
    }

    private Task ClearPendingGrantChangeAsync(string reason)
    {
        var audit = pendingGrantChangeAudit;
        pendingGrantChangeAudit = null;
        pendingGrantChange = null;
        var retirement = audit is null ? Task.CompletedTask : CompleteAuditAsync(audit,
            string.Equals(reason, "approved-once", StringComparison.Ordinal)
                ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Cancelled, reason);
        OnPropertyChanged(nameof(IsGrantChangePending));
        OnPropertyChanged(nameof(IsApprovalPending));
        OnPropertyChanged(nameof(IsResponseInteractionPending));
        ConfirmGrantChangeCommand.NotifyCanExecuteChanged();
        RejectGrantChangeCommand.NotifyCanExecuteChanged();
        NotifyOutputPolicyChanged();
        return retirement;
    }

    private void PresentModelQuestion(
        LocalModelQuestion question,
        string request,
        int depth,
        LocalModelArtifact? artifact)
    {
        pendingModelQuestion = question;
        pendingQuestionRequest = request;
        pendingQuestionArtifact = artifact;
        pendingQuestionDepth = depth;
        pendingModelQuestionContinuation = HostActivity.CaptureContinuation(
            HostActivityLayer.Application, HostOperation.Request, OriginalOrigin());
        ModelQuestionChoices = question.Options.Select((text, index) =>
            new ModelQuestionChoice(index + 1, text)).ToArray();
        OnPropertyChanged(nameof(ModelQuestionChoices));
        OnPropertyChanged(nameof(IsModelQuestionPending));
        OnPropertyChanged(nameof(IsResponseInteractionPending));
        OnPropertyChanged(nameof(CommandInputPlaceholder));
        OnPropertyChanged(nameof(CommandSubmitLabel));
        var options = string.Join("; ", ModelQuestionChoices.Select(choice => choice.DisplayText));
        ShowInformation(
            "Kora needs your direction.",
            $"{question.Prompt}\n{options}\nChoose an option or provide your own answer by voice or typing. This is not an action approval. Say “cancel question” to dismiss.");
        WindowActionRequested?.Invoke(this, WindowAction.Show);
    }

    private void ClearPendingModelQuestion()
    {
        pendingModelQuestion = null;
        pendingQuestionRequest = null;
        pendingQuestionArtifact = null;
        pendingModelQuestionContinuation = null;
        ModelQuestionChoices = [];
        OnPropertyChanged(nameof(ModelQuestionChoices));
        OnPropertyChanged(nameof(IsModelQuestionPending));
        OnPropertyChanged(nameof(IsResponseInteractionPending));
        OnPropertyChanged(nameof(CommandInputPlaceholder));
        OnPropertyChanged(nameof(CommandSubmitLabel));
        NotifyOutputPolicyChanged();
    }

    public async Task SelectModelQuestionChoiceAsync(ModelQuestionChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (pendingModelQuestion is not { } question
            || !ModelQuestionChoices.Any(item => ReferenceEquals(item, choice))
            || pendingQuestionRequest is not { } original)
        {
            return;
        }

        await SubmitModelQuestionAnswerAsync(
            question, original, choice.Text, selectedOption: true, SecurityAuditInitiator.TypedCommand);
    }

    private async Task SubmitModelQuestionAnswerAsync(
        LocalModelQuestion question,
        string original,
        string answer,
        bool selectedOption,
        SecurityAuditInitiator initiator)
    {
        var continuation = pendingModelQuestionContinuation;
        if (continuation is null)
        {
            ShowFailure("The clarification could not continue.", "The original host request is unavailable. Initiate a fresh request.");
            return;
        }
        var artifact = pendingQuestionArtifact;
        var followup = selectedOption
            ? JsonSerializer.Serialize(new
            {
                OriginalRequest = original,
                ClarificationQuestion = question.Prompt,
                SelectedOption = answer,
                Instruction = "Use the user's selected option to continue the original request. This selection is not permission to execute an action.",
            })
            : JsonSerializer.Serialize(new
            {
                OriginalRequest = original,
                ClarificationQuestion = question.Prompt,
                CustomAnswer = answer,
                Instruction = "Use the user's custom answer to continue the original request. This answer is not permission to execute an action.",
            });
        var depth = pendingQuestionDepth;
        if (!await TryStopQuestionPromptAsync())
        {
            return;
        }
        if (!ReferenceEquals(pendingModelQuestion, question))
        {
            return;
        }
        ClearPendingModelQuestion();
        if (followup.Length > 4096)
        {
            ShowFailure("The clarification could not continue.",
                "The combined request exceeds the local model limit. Please rephrase your request more briefly.");
            return;
        }

        using var routing = continuation();
        await HandleUnmatchedRequestAsync(
            followup,
            initiator,
            depth,
            artifact: artifact,
            activationAlreadyValidated: initiator == SecurityAuditInitiator.VoiceCommand);
        routing.Complete(HostOperationOutcome.Completed);
    }

    public async Task CancelModelQuestionAsync()
    {
        if (pendingModelQuestion is not { } question)
        {
            return;
        }
        if (!await TryStopQuestionPromptAsync())
        {
            return;
        }
        if (!ReferenceEquals(pendingModelQuestion, question))
        {
            return;
        }
        ClearPendingModelQuestion();
        ShowInformation("Question dismissed.", "No answer or action was selected.");
    }

    private async Task<bool> TryStopQuestionPromptAsync()
    {
        try
        {
            await StopModelApprovalPromptAsync();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            ApplicationLog.Error(logger, exception, "Stopping the spoken model question");
            ShowFailure("Could not stop the spoken question.",
                $"The question remains pending. {exception.Message}");
            return false;
        }
    }

    private void AppendGrantSection(
        StringBuilder builder,
        string heading,
        HashSet<BuiltInAction> actions)
    {
        builder.Append("## ").AppendLine(heading).AppendLine();
        foreach (var command in commandCatalog.GetCommands(AssistantName))
        {
            if (!actions.Contains(command.Action))
            {
                continue;
            }
            builder.Append("- **")
                .Append(command.Action)
                .Append("** — ")
                .AppendLine(command.Description);
        }

        if (actions.Count == 0)
        {
            builder.AppendLine("No grants.");
        }

        builder.AppendLine();
    }

    public async Task ExitAsync(bool fromModelActionDispatch = false)
    {
        if (voluntaryShutdownRunning || hostExitRequested) { return; }
        voluntaryShutdownRunning = true;
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
                await StopAudioAsync(fromModelActionDispatch);
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

        if (activeReasoningCancellation is not null)
        {
            ShowInformation("Local reasoning is running.", "Wait for the answer or cancel the task before refreshing readiness.");
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

            Interlocked.Increment(ref reusableGrantRevision);
            alwaysAllowedModelActions.Clear();
            var approvals = modelApprovalPreferences.Load();
            alwaysAllowedModelActions.UnionWith(approvals.AlwaysAllowedActions);
            SetProperty(
                ref requireAssistantNameForVoiceApproval,
                approvals.RequireAssistantNameForVoiceApproval,
                nameof(RequireAssistantNameForVoiceApproval));
            OnPropertyChanged(nameof(AlwaysAllowedModelActions));
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

    private void ApplyDependencyStatus(DependencyStatus status)
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

        if (string.Equals(status.Id, "local.inference", StringComparison.Ordinal))
        {
            LocalModelSetupStatus = status.Detail;
        }
        else
        {
            PowerShellSetupStatus = status.Detail;
        }

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
        voiceRecognition.InvalidateCapture();
        await voiceRecognition.StopAsync();
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
        if (communicationPolicy.Current.SuppressSpeech)
        {
            ShowInformation("Voice preview is suppressed.", "Protected call policy requires visual-only output.");
            return;
        }
        if (audioPlaybackSettings.VolumeState is { AllowsSpeech: false })
        {
            ShowInformation("Voice preview is unavailable.",
                "Kora playback volume is zero or unavailable. Inspect volume status in Settings; changing it never replays stopped speech.");
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
            PreserveSpokenResponseFailure("Kora playback volume is zero or unavailable. Full visual output is retained; refresh volume preferences.");
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
            IsSpeaking = false;
            IsBusy = false;
        }
    }

    private async Task StopSpeakingAsync()
    {
        RetireResponseReadPlayback();
        RetireSpeechCaption();
        try
        {
            await textToSpeech.StopAsync();
#pragma warning disable VSTHRD003 // Stop retires the owned read before replacing its source with recovery text.
            await responseReadQuiescence;
#pragma warning restore VSTHRD003
            if (activeCallSpeechLease is not null && textToSpeech.IsSpeaking)
            { throw new InvalidOperationException("Owned speech release remains unconfirmed."); }
            activeCallSpeechLease = null;
            activeSpokenText = null;
            IsSpeaking = false;
            ShowInformation("Speech is stopped.", "No speech playback is active.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var reviewedCall = activeCallSpeechLease is not null;
            LogSpokenResponseFailure(exception, "Stopping speech output", reviewedCall);
            ShowFailure("Speech output could not be stopped.", reviewedCall
                ? "Reviewed call speech release remains unconfirmed. Use Stop speaking after output recovery; no replay is permitted."
                : exception.Message);
        }
    }

    private async Task StopAudioAsync(bool skipCurrentModelDispatchWait = false)
    {
        RetireCurrentResponseSpeech();
        HoldVoiceInput("Microphone closed · host lifecycle stopped");
        textToSpeech.InvalidateOutput();
        var captureClosure = StopListeningAsync();
        Interlocked.Increment(ref stoppingAudioOperations);
        try
        {
            if (sessionAllowedModelActions.Count > 0)
            {
                sessionAllowedModelActions.Clear();
                Interlocked.Increment(ref reusableGrantRevision);
                OnPropertyChanged(nameof(SessionAllowedModelActions));
                GrantDocumentChanged?.Invoke(this, GetGrantDocument());
            }

            var actionRetirement = ClearPendingModelActionAsync("session-stopped");
            var grantRetirement = ClearPendingGrantChangeAsync("session-stopped");
            if (IsModelQuestionPending)
            {
                ClearPendingModelQuestion();
            }

            await Task.WhenAll(captureClosure, textToSpeech.StopAsync(), actionRetirement, grantRetirement);
#pragma warning disable VSTHRD003 // Join the retired, independently owned one-shot before host resource handoff.
            await responseReadQuiescence;
#pragma warning restore VSTHRD003
#pragma warning disable VSTHRD003 // Await owned reply retirement asynchronously so no queued UI commit survives host handoff/close.
            if (questionVoiceReplies is not null) { await questionVoiceReplies.Quiescence; }
            await JoinCustomizationClarification();
            if (composerDictation is not null) { await composerDictation.Quiescence; }
#pragma warning restore VSTHRD003
            activeSpokenText = null;
            IsSpeaking = false;
            if (activeReasoningCancellation is { } cancellation)
            {
                await cancellation.CancelAsync();
                if (activeReasoningTask is { } reasoningTask)
                {
                    await reasoningTask;
                }
            }
            else if ((isModelActionDispatchActive || isModelApprovalPromptActive)
                && !skipCurrentModelDispatchWait
                && activeReasoningTask is { } dispatchTask)
            {
                await dispatchTask;
            }

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
            await InterruptSessionSpeechAsync(CaptureSpeechInterruptionInput(), voice: false);
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
        ActivityLink[] causes = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        using var activity = HostActivity.BeginRoot(originalRequest ?? HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request, causes);
        if (eventArgs.Generation != Interlocked.Read(ref acceptedTranscriptGeneration)
            || eventArgs.Generation != voiceRecognition.Generation)
        {
            ApplicationLog.Debug(logger, "Discarded an unactivated or retired voice callback before UI dispatch");
            ApplicationLog.VoiceDispatchGate(logger, eventArgs.Generation, "RejectedBeforeQueue");
            activity.Complete(HostOperationOutcome.Completed);
            return;
        }
        try
        {
            var manualInput = CaptureManualCallInput(RequestOrigin.ActivatedVoice, CallPolicyRevision, eventArgs.Generation);
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
            await uiDispatcher.InvokeAsync(
                () => HandleRecognizedVoiceTranscriptAsync(eventArgs, manualInput, responseReadInput, reviewEligibility,
                    newRequestEligibility, currentResponseInput, questionTarget, editorTarget, foregroundTarget, speechInput));
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
            if (pendingModelQuestion is { } question)
            {
                ShowInformation("I didn't catch an option.",
                    $"{eventArgs.Message} {question.Prompt} Say “{AssistantName}, option one” or choose a button.");
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
        if (eventArgs.Generation != voiceRecognition.Generation || lifecycleAdmissionClosed
            || !sessionController.IsCurrentSessionUnlocked())
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
            if (IsModelActionReviewCommand(eventArgs.Transcript)
                || manualInput.ModelActionReview is not null
                    && ModelApprovalSpeech.TryMatch(eventArgs.Transcript, AssistantName, false, out _))
            { ReportModelActionReviewUnavailable(); }
            return;
        }
        if (Interlocked.CompareExchange(ref acceptedTranscriptGeneration, -1, eventArgs.Generation) != eventArgs.Generation)
        {
            ApplicationLog.VoiceDispatchGate(logger, eventArgs.Generation, "RejectedDuplicate");
            return;
        }
        ApplicationLog.VoiceDispatchGate(logger, eventArgs.Generation, "Admitted");
        IsListening = voiceRecognition.IsListening;
        if (IsSpeechInterruptionCommand(eventArgs.Transcript))
        {
            await InterruptSessionSpeechAsync(speechInput, voice: true);
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
            || LogonStartupCommand.Parse(eventArgs.Transcript, AssistantName) is not null
            || manualInput.CataloguePreparation is not null
            || EvidenceTraceCommand.Parse(eventArgs.Transcript, AssistantName) is not null)
        {
            await HandleTranscriptAsync(eventArgs.Transcript, eventArgs.Confidence,
                SecurityAuditInitiator.VoiceCommand, manualInput);
            return;
        }
        if (IsSpeaking)
        {
            await textToSpeech.StopAsync();
            activeSpokenText = null;
            IsSpeaking = false;
        }

        if (eventArgs.Generation != voiceRecognition.Generation || !IsHostInputEligible)
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
        if (LogonStartupCommand.Parse(spokenText, AssistantName) is { } startupOperation)
        {
            if (!isAssistantNameAvailable || initiator == SecurityAuditInitiator.VoiceCommand
                && !commandRouter.IsActivationPrefixed(spokenText, AssistantName))
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
        var reviewInput = manualInput ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision);
        if (TryActivateModelActionReview(spokenText, initiator, reviewInput)) { return; }
        if (ModelApprovalSpeech.TryMatch(spokenText, AssistantName, false, out _)
            && (reviewInput.ModelActionReview is not null || IsModelActionApprovalPending)
            && (!reviewInput.Eligible() || !IsModelActionReviewCurrent(reviewInput.ModelActionReview)))
        {
            ReportModelActionReviewUnavailable();
            return;
        }
        if (WorkspaceSurfaceCommand.Parse(spokenText, AssistantName)?.Target is
                (WorkspaceSurfaceTarget.OpenNamedConversation or WorkspaceSurfaceTarget.ObservedResult)
            && (!CanEnterRetainedAnswerRead || editorTarget is not null))
        {
            ReportHostInteractionFailure("Conversation opening unavailable while a question, approval, review, editor or dictation owns input. Nothing was answered or submitted.");
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
        var sessionCommand = SessionCommand.Parse(spokenText, AssistantName);
        var newRequestCommand = NewRequestOpenCommand.Parse(spokenText, AssistantName);
        var cataloguePreparation = manualInput?.CataloguePreparation
            ?? AttachmentCataloguePreparationCommand.Parse(spokenText, AssistantName);
        var evidenceTraceCommand = EvidenceTraceCommand.Parse(spokenText, AssistantName);
        var pendingQuestionCommand = questionTarget?.Command ?? PendingQuestionCommand.Parse(spokenText, AssistantName);
        var currentResponseCommand = CurrentResponseCommand.Parse(spokenText, AssistantName);
        if (currentResponseCommand is null && IsDetailsOfferPending
            && WorkspaceSurfaceCommand.Parse(spokenText, AssistantName)?.Target == WorkspaceSurfaceTarget.Details)
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
            await Kora.Application.Hosting.HostRequestRunner.RunAsync(
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
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                ReportHostInteractionFailure("Assistant prefix routing is unavailable; no customization control was dispatched.");
                return;
            }
            await Kora.Application.Hosting.HostRequestRunner.RunAsync(OriginalOrigin(initiator),
                () => initiator == SecurityAuditInitiator.VoiceCommand && customization.Operation is "Refine"
                    ? ExecuteCustomizationVoice(editorTarget, customization.Text!)
                    : ExecuteCustomizationCommand(customization));
            return;
        }
        if (IsInstructionEditorCommand(spokenText))
        {
            if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                ReportHostInteractionFailure("Assistant prefix routing is unavailable; no instruction control was dispatched.");
                return;
            }
            await Kora.Application.Hosting.HostRequestRunner.RunAsync(OriginalOrigin(initiator),
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
            await Kora.Application.Hosting.HostRequestRunner.RunAsync(origin,
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
                && !commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                ShowInformation("Settings undo requires original input.", "Use the current assistant-name prefix. Nothing was applied.");
                return;
            }
            await Kora.Application.Hosting.HostRequestRunner.RunAsync(origin,
                () => ExecuteSettingsUndoAsync(undoCommand, initiator, CancellationToken.None));
            return;
        }
        if (RegisteredSettingSuggestionCommand.Parse(spokenText, AssistantName) is { } responseSuggestionCommand)
        {
            await ExecuteResponseModeSuggestionAsync(responseSuggestionCommand, initiator,
                commandRouter.IsActivationPrefixed(spokenText, AssistantName));
            return;
        }
        if (PlaybackVolumeSuggestionCommand.Parse(spokenText, AssistantName) is { } volumeSuggestionCommand)
        {
            await ExecutePlaybackVolumeSuggestionAsync(volumeSuggestionCommand, initiator,
                commandRouter.IsActivationPrefixed(spokenText, AssistantName));
            return;
        }
        if (SpeechSummarySuggestionCommand.Parse(spokenText, AssistantName) is { } speechSuggestionCommand)
        {
            await ExecuteSpeechSummarySuggestionAsync(speechSuggestionCommand, initiator,
                commandRouter.IsActivationPrefixed(spokenText, AssistantName));
            return;
        }
        if (SpeechCommand.Parse(spokenText, AssistantName) is { IsNaturalSummary: true } speechCommand)
        {
            if (initiator == SecurityAuditInitiator.VoiceCommand
                && !commandRouter.IsActivationPrefixed(spokenText, AssistantName))
            {
                Transcript = "Use the current assistant-name prefix for spoken summary limit voice commands. No speech setting was changed.";
                return;
            }
            await Kora.Application.Hosting.HostRequestRunner.RunAsync(origin,
                () => ExecuteSpeechCommandAsync(speechCommand, initiator));
            return;
        }
        if (Kora.Core.Interaction.LocalEventCommand.Parse(spokenText, AssistantName) is { } eventCommand)
        {
            await ExecuteLocalEventCommandAsync(eventCommand, initiator);
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
                commandRouter.IsActivationPrefixed(spokenText, AssistantName));
            return;
        }
        if (SessionQueueCancellationReviewCommand.Parse(spokenText, AssistantName) is { } cancellationPreparation)
        {
            await ExecuteQueueCancellationPreparationAsync(cancellationPreparation, initiator,
                commandRouter.IsActivationPrefixed(spokenText, AssistantName));
            return;
        }
        if (sessionCommand is not null)
        {
            if (initiator == SecurityAuditInitiator.VoiceCommand
                && (sessionCommand.IsRetentionCommand || sessionCommand.IsConversationSearch
                    || sessionCommand.Operation is SessionCommandOperation.RequestReview or SessionCommandOperation.RequestConfirm
                        or SessionCommandOperation.RequestCancel or SessionCommandOperation.AttachmentInspect)
                && (!commandRouter.IsActivationPrefixed(spokenText, AssistantName)
                    || sessionCommand.RequestForegroundTarget && !sessionCommand.RequestActivationPrefixed))
            {
                RetireSessionCommandReviews();
                ShowInformation("Session request denied.", "Use the current assistant-name prefix and exact session grammar. No pending question or model received this input.");
                return;
            }
            await ExecuteSessionCommandAsync(sessionCommand, initiator,
                reviewEligibility ?? CaptureSkillReviewEligibility(manualInput
                    ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision)),
                commandRouter.IsActivationPrefixed(spokenText, AssistantName),
                manualInput?.AssistantNameRevision ?? assistantNameConfiguration.Get().Revision, foregroundTarget);
            return;
        }
        if (MemoryCommand.Parse(spokenText, AssistantName) is { } memoryCommand)
        {
            await ExecuteMemoryCommandAsync(memoryCommand, initiator);
            return;
        }
        var admittedCommand = commandRouter.Match(spokenText, AssistantName).Command;
        if (IsDurableVersionQueryEligible(true, pendingModelQuestion is not null, IsGrantChangePending, IsModelActionApprovalPending)
            && admittedCommand?.Action == BuiltInAction.ShowVersion)
        {
            await durableVersionQuery.RunAsync(origin,
                async () =>
                {
                    if (!IsDurableVersionQueryEligible(IsHostInputEligible, pendingModelQuestion is not null,
                        IsGrantChangePending, IsModelActionApprovalPending))
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
        await Kora.Application.Hosting.HostRequestRunner.RunAsync(origin,
            () => RouteTranscriptAsync(spokenText, confidence, initiator, speechRevision, editorTarget, manualInput));
    }

    internal static bool IsDurableVersionQueryEligible(
        bool hostEligible, bool questionPending, bool grantChangePending, bool actionApprovalPending) =>
        hostEligible && !questionPending && !grantChangePending && !actionApprovalPending;

    private void BeginTranscriptPresentation(string spokenText, float confidence, SecurityAuditInitiator initiator)
    {
        IsGrantEditorVisible = false;
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
        if (pendingModelQuestion is { } question)
        {
            var canAnswer = initiator != SecurityAuditInitiator.VoiceCommand
                || !RequireAssistantNameForVoiceApproval
                || commandRouter.IsActivationPrefixed(spokenText, AssistantName);
            if (canAnswer)
            {
                var index = LocalModelQuestionSpeech.Match(spokenText, question, AssistantName);
                if (index >= 0)
                {
                    Transcript = $"“{spokenText}” · {confidence:P0} confidence";
                    await SubmitModelQuestionAnswerAsync(
                        question, pendingQuestionRequest!, ModelQuestionChoices[index].Text,
                        selectedOption: true, initiator);
                    return;
                }
                if (commandRouter.Match(spokenText, AssistantName).Command?.Action == BuiltInAction.CancelTask
                    || LocalModelQuestionSpeech.IsCancellation(spokenText, AssistantName))
                {
                    await CancelModelQuestionAsync();
                    return;
                }
            }

            if (commandRouter.Match(spokenText, AssistantName).IsMatch
                || AppearanceCommand.Parse(spokenText, AssistantName) is not null
                || NaturalAppearanceCommand.Parse(spokenText, AssistantName, initiator == SecurityAuditInitiator.VoiceCommand) is not null
                || SpeechCommand.Parse(spokenText, AssistantName) is not null
                || ClipboardCommand.Parse(commandRouter.Match(spokenText, AssistantName).NormalizedTranscript) is not null)
            {
                await CancelModelQuestionAsync();
            }
            else if (canAnswer
                && LocalModelQuestionSpeech.GetFreeTextAnswer(spokenText, AssistantName) is { } answer)
            {
                Transcript = $"“{spokenText}” · {confidence:P0} confidence";
                await SubmitModelQuestionAnswerAsync(
                    question, pendingQuestionRequest!, answer, selectedOption: false, initiator);
                return;
            }
            else if (!canAnswer
                && LocalModelQuestionSpeech.GetFreeTextAnswer(spokenText, AssistantName) is not null)
            {
                ShowInformation("Say the assistant name to answer.",
                    $"{question.Prompt} Say “{AssistantName}, option one”, address me by name with your own answer, or type an answer in the response window.");
                return;
            }
            else
            {
                ShowInformation("Answer or cancel the question.",
                    $"{question.Prompt} Say “{AssistantName}, option one”, provide your own answer, choose a button, or say “{AssistantName}, cancel question”.");
                return;
            }
        }

        if (IsGrantChangePending)
        {
            if (ModelApprovalSpeech.TryMatch(spokenText, AssistantName, false, out var grantReply))
            {
                if (initiator == SecurityAuditInitiator.VoiceCommand
                    && RequireAssistantNameForVoiceApproval
                    && !commandRouter.IsActivationPrefixed(spokenText, AssistantName))
                {
                    ShowInformation("Say the assistant name to confirm.", DescribeGrantChange(pendingGrantChange!)
                        + $" Say “{AssistantName}, approve once” or “{AssistantName}, reject”.");
                    return;
                }
                if (grantReply == ModelApprovalReply.Reject)
                {
                    await RejectPendingGrantChangeAsync();
                }
                else if (grantReply == ModelApprovalReply.Once)
                {
                    await ConfirmGrantChangeAsync(initiator);
                }
                else
                {
                    ShowInformation("Confirm the exact change once.",
                        DescribeGrantChange(pendingGrantChange!)
                        + " Say “approve once” or “reject”; the proposed scope is already fixed.");
                }
                return;
            }
            await ClearPendingGrantChangeAsync("superseded");
        }

        if (IsModelActionApprovalPending)
        {
            if (ModelApprovalSpeech.TryMatch(
                    spokenText, AssistantName, false, out var approvalReply))
            {
                if (initiator == SecurityAuditInitiator.VoiceCommand
                    && RequireAssistantNameForVoiceApproval
                    && !commandRouter.IsActivationPrefixed(spokenText, AssistantName))
                {
                    var action = pendingModelAction!.Value;
                    var description = commandCatalog.GetCommands(AssistantName)
                        .Single(command => command.Action == action).Description;
                    ShowInformation(
                        "Say the assistant name to approve.",
                        $"{description} Say “{AssistantName}, approve once”, “{AssistantName}, approve for this session”, or “{AssistantName}, always allow this”.");
                    return;
                }

                Transcript = $"“{spokenText}” · {confidence:P0} confidence";
                if (approvalReply == ModelApprovalReply.Reject)
                {
                    await RejectPendingModelActionAsync();
                }
                else
                {
                    await ApproveModelActionAsync(ScopeForApprovalReply(approvalReply), initiator);
                }
                return;
            }

            await ClearPendingModelActionAsync("superseded");
        }

        BeginTranscriptPresentation(spokenText, confidence, initiator);

        if ((AppearanceCommand.Parse(spokenText, AssistantName)
            ?? NaturalAppearanceCommand.Parse(spokenText, AssistantName, initiator == SecurityAuditInitiator.VoiceCommand)) is { } appearanceCommand)
        {
            await ExecuteAppearanceCommandAsync(appearanceCommand, initiator);
            return;
        }

        if (SpeechCommand.Parse(spokenText, AssistantName) is { } speechCommand)
        {
            await ExecuteSpeechCommandAsync(speechCommand, initiator);
            return;
        }

        var match = commandRouter.Match(spokenText, AssistantName);
        if (LocalFileCommand.Parse(match.NormalizedTranscript) is { } fileCommand)
        {
            await ExecuteFileCommandAsync(fileCommand);
            return;
        }
        if (ClipboardCommand.Parse(match.NormalizedTranscript) is { } clipboardCommand)
        {
            await ExecuteClipboardCommandAsync(clipboardCommand);
            return;
        }
        if (TryPresentCapabilityCommand(match.NormalizedTranscript))
        {
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
            var promptEligibility = artifactMatch.Invocation.Artifact.PromptTemplate is null ? null : CapturePromptSelection();
            if (!await IsArtifactSourceCurrent(artifactMatch.Invocation.Artifact, CancellationToken.None))
            {
                ShowInformation("Definition revision is stale or unavailable.", "Reload and review the exact definition on this machine before starting a new request.");
                return;
            }
            if (artifactMatch.Invocation.Artifact.PromptTemplate is not null)
            {
                await PreparePrompt(artifactMatch.Invocation, spokenText, initiator, promptEligibility!);
                return;
            }
            await HandleUnmatchedRequestAsync(
                spokenText,
                initiator,
                artifactInvocation: artifactMatch.Invocation);
            return;
        }
        if (!match.IsMatch || match.Command is null)
        {
            if (await TryRouteSessionRequestAsync(spokenText, initiator, originalInput)) { return; }
            await HandleUnmatchedRequestAsync(spokenText, initiator);
            return;
        }

        await ExecuteAsync(match.Command, initiator, sourceCurrent: match.Command.Action == BuiltInAction.LockMachine
            && originalInput is not null ? CaptureLockSource(originalInput) : originalInput?.Eligible);
        if (ShouldSpeakResponse(match.Command.Action))
        {
            await SpeakCurrentResponseAsync(expectedSpeechRevision: speechRevision);
        }
    }

    internal static ModelApprovalScope ScopeForApprovalReply(ModelApprovalReply reply) => reply switch
    {
        ModelApprovalReply.Once => ModelApprovalScope.Once,
        ModelApprovalReply.Session => ModelApprovalScope.Session,
        ModelApprovalReply.Always => ModelApprovalScope.Always,
        _ => throw new InvalidOperationException("Unsupported approval reply."),
    };

    private Task HandleUnmatchedRequestAsync(
        string spokenText,
        SecurityAuditInitiator initiator,
        int questionDepth = 0,
        ArtifactInvocation? artifactInvocation = null,
        LocalModelArtifact? artifact = null,
        bool activationAlreadyValidated = false)
    {
        if (initiator == SecurityAuditInitiator.VoiceCommand
            && !activationAlreadyValidated
            && !commandRouter.IsActivationPrefixed(spokenText, AssistantName))
        {
            ShowInformation(
                "Activate me before asking a question.",
                $"Say “{AssistantName}” before a free-form request. Built-in voice commands remain available.");
            return Task.CompletedTask;
        }

        var request = artifactInvocation?.Request ?? spokenText.Trim();
        if (artifactInvocation is null && string.Equals(
            request.TrimEnd(',', ':', '-', '—').TrimEnd(),
            AssistantName,
            StringComparison.OrdinalIgnoreCase))
        {
            request = string.Empty;
        }
        else if (artifactInvocation is null
            && commandRouter.IsActivationPrefixed(request, AssistantName))
        {
            request = request[AssistantName.Length..].TrimStart(' ', ',', ':', '-', '—');
        }

        if (string.IsNullOrWhiteSpace(request))
        {
            if (artifactInvocation is null)
            {
                ShowInformation("No question was heard.", $"Say “{AssistantName}” followed by a request.");
                return Task.CompletedTask;
            }
            request = $"Run the selected {artifactInvocation.Artifact.Kind.ToString().ToLowerInvariant()}.";
        }

        if (artifact is null && artifactInvocation is not null)
        {
            artifact = new LocalModelArtifact(
                artifactInvocation.Artifact.Id,
                artifactInvocation.Artifact.Kind,
                artifactInvocation.Artifact.Name,
                artifactInvocation.Artifact.Source,
                artifactInvocation.Artifact.Version,
                artifactInvocation.Artifact.Digest,
                artifactInvocation.Artifact.Content)
            {
                IsLiteralPrompt = artifactInvocation.Artifact.Kind == ArtifactKind.Prompt
                    && artifactInvocation.Artifact.RequiresInstructionReview,
            };
        }

        if (request.Length > 4096)
        {
            ShowInformation("The request is too long.", "Model requests are limited to 4,096 characters.");
            return Task.CompletedTask;
        }

        if (IsLocalReasoningBusy(activeReasoningCancellation is not null,
            isModelActionDispatchActive, isModelApprovalPromptActive,
            IsBusy, IsLocalModelSetupActive, IsPowerShellSetupActive))
        {
            ShowInformation(
                "Local reasoning is busy.",
                "Wait for the current task or say “cancel task” before asking another question.");
            return Task.CompletedTask;
        }

        if (!LocalModelsEnabled)
        {
            ShowInformation(
                HostedModelsEnabled
                    ? "No enabled model is available."
                    : "Model use is turned off.",
                HostedModelsEnabled
                    ? "Local models are disabled, and no hosted model provider is configured in this build. Built-in commands remain available."
                    : $"Local and hosted models are disabled. Say “{AssistantName}, enable local models” or change the Models settings to allow free-form requests.");
            return SpeakCurrentResponseAsync();
        }

        if (Dependencies.FirstOrDefault(status =>
            string.Equals(status.Id, "local.inference", StringComparison.Ordinal)) is not
            { Readiness: DependencyReadiness.Ready })
        {
            ShowInformation(
                "That isn't a supported built-in command.",
                $"Say “{AssistantName}, what can you do?” to see deterministic commands. A verified local model is required for other requests; open setup to check readiness."
                + (HostedModelsEnabled
                    ? " Hosted models are allowed, but no hosted provider is configured in this build."
                    : " Hosted models are disabled, so no cloud fallback is used."));
            return SpeakCurrentResponseAsync();
        }

        var cancellation = new CancellationTokenSource();
        activeReasoningCancellation = cancellation;
        IsLocalTaskCancellable = true;
        dependencyBootstrapper.Tasks.Start("local.reasoning", "Local model response", "Generating an answer locally.");
        RefreshCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanInstallPowerShell));
        OnPropertyChanged(nameof(CanInstallLocalModel));
        ShowInformation(
            artifact is null ? "Thinking locally." : $"Running {artifact.Name} locally.",
            artifact is null
                ? "Only your current request text is sent to the selected local model."
                : "Your current request and the selected bundled artifact instructions are sent only to the selected local model. Artifact selection does not approve or execute an action.");
        var continuation = HostActivity.CaptureContinuation(
            HostActivityLayer.Application, HostOperation.Request, OriginalOrigin());
        activeReasoningTask = RunReasoningAsync(request, cancellation, questionDepth, artifact, continuation);
        _ = activeReasoningTask.ContinueWith(completed =>
        {
            if (completed.Exception is { } exception)
            {
                uiDispatcher.Post(() =>
                {
                    ApplicationLog.Error(logger, exception, "Completing local reasoning");
                    ShowFailure("Local reasoning failed.", exception.GetBaseException().Message);
                });
            }
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return Task.CompletedTask;
    }

    internal static bool IsLocalReasoningBusy(
        bool reasoning, bool actionDispatch, bool approvalPrompt,
        bool busy, bool modelSetup, bool powerShellSetup) =>
        reasoning || actionDispatch || approvalPrompt || busy || modelSetup || powerShellSetup;

    private async Task RunReasoningAsync(
        string request,
        CancellationTokenSource cancellation,
        int questionDepth,
        LocalModelArtifact? artifact, Func<HostActivity> continuation)
    {
        using var activity = continuation();
        var outcome = HostOperationOutcome.Completed;
        var speechRevision = Interlocked.Read(ref manualCallSpeechRevision);
        BuiltInAction? automaticAction = null;
        var announceApproval = false;
        var lockHostCurrent = CaptureModelActionReviewHost();
        var lockCallRevision = CallPolicyRevision;
        var lockPreparationRevision = Interlocked.Read(ref approvalPreparationRevision);
        var originalToken = cancellation.Token;
        bool LockSourceCurrent() => lockHostCurrent() && !originalToken.IsCancellationRequested
            && lockCallRevision == CallPolicyRevision
            && lockPreparationRevision == Interlocked.Read(ref approvalPreparationRevision)
            && (artifact is null || artifactCatalogue.IsCurrent(artifact.Id, artifact.Digest));
        activeReasoningArtifact = artifact;
        try
        {
            var context = new LocalModelContext(
                AssistantName,
                IsListening,
                pendingPowerAction?.ToString(),
                Dependencies.Select(status => new LocalModelDependency(
                    status.Name, status.Readiness)).ToArray(),
                SetupTasks.Where(task => !string.Equals(
                        task.Id, "local.reasoning", StringComparison.Ordinal))
                    .Select(task => new LocalModelTask(
                        task.Name, task.State, task.ProgressPercentage)).ToArray());
            if (artifact is not null && !await IsFileArtifactSourceCurrent(artifact, cancellation.Token))
            {
                await cancellation.CancelAsync();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            var decision = await localModelReasoner.ReasonAsync(request, context, artifact, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (artifact is not null && (!artifactCatalogue.IsCapturedRevisionCurrent(artifact.Id, artifact.Digest)
                || !await IsFileArtifactSourceCurrent(artifact, cancellation.Token)))
            {
                await cancellation.CancelAsync();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            if (new object?[] { decision.Answer, decision.Action, decision.GrantChange, decision.Question }
                    .Count(value => value is not null) != 1
                || decision.Answer is not null && string.IsNullOrWhiteSpace(decision.Answer)
                || decision.Action is { } unknown && !Enum.IsDefined(unknown)
                || decision.GrantChange is { } grant
                    && (!Enum.IsDefined(grant.Action) || !Enum.IsDefined(grant.Operation))
                || decision.Question is { } question && (
                    string.IsNullOrWhiteSpace(question.Prompt) || question.Prompt.Length > 500
                    || question.Options is null || question.Options.Count is < 2 or > 4
                    || question.Options.Any(option => string.IsNullOrWhiteSpace(option) || option.Length > 80)
                    || !LocalModelQuestionSpeech.AreOptionsUnambiguous(question.Options)))
            {
                throw new InvalidDataException("The local model returned an invalid answer or action.");
            }

            dependencyBootstrapper.Tasks.Update(
                "local.reasoning", SetupTaskState.Completed, "The local model returned a response.");
            if (artifact is not null && !artifactCatalogue.IsCurrent(artifact.Id, artifact.Digest)
                && decision.Answer is null)
            {
                ShowInformation("Artifact use disabled.",
                    "Captured reasoning finished, but no new question or action choice was admitted. Start a fresh request after exact use review; nothing was dispatched or retargeted.");
                return;
            }
            if (decision.Question is { } clarification)
            {
                if (questionDepth >= 3)
                {
                    ShowInformation("Too many clarification questions.",
                        "Please start a new request with the details you have already provided.");
                }
                else
                {
                    PresentModelQuestion(clarification, request, questionDepth + 1, artifact);
                    announceApproval = true;
                }
            }
            else if (decision.GrantChange is { } change)
            {
                if (artifact is not null)
                {
                    ShowInformation(
                        "Artifact cannot change reusable grants.",
                        "Artifact-based requests may propose a consequential action for fresh single-use approval, but cannot add or remove session or always grants.");
                }
                else
                {
                    await PrepareGrantChangeAsync(change, SecurityAuditInitiator.ModelSuggestion);
                    announceApproval = IsGrantChangePending;
                }
            }
            else if (decision.Action is { } action)
            {
                if (action != BuiltInAction.LockMachine && ModelActionRequiresApproval(action)
                    && (artifact is not null || !CanReuseLegacyModelGrant(action)))
                {
                    var callRevision = CallPolicyRevision;
                    var recoveryRevision = Interlocked.Read(ref voiceRecoveryRevision);
                    var preparationRevision = Interlocked.Read(ref approvalPreparationRevision);
                    var reviewRequest = HostActivity.RequireCurrent().Request;
                    var reviewHostEligible = CaptureModelActionReviewHost();
                    var proposedAudit = await StartAuditAsync(
                        SecurityAuditCategory.SecurityApproval,
                        $"{ModelActionApprovalPrefix}{action.ToString().ToLowerInvariant()}",
                        SecurityAuditInitiator.ModelSuggestion,
                        action switch
                        {
                            BuiltInAction.ProposeShutdown or BuiltInAction.ProposeRestart
                                => CurrentMachineTarget,
                            _ => CurrentApplicationTarget,
                        },
                        Guid.NewGuid());
                    if (cancellation.IsCancellationRequested || !IsCallMutationHostEligible
                        || CallPolicyRevision != callRevision || Interlocked.Read(ref voiceRecoveryRevision) != recoveryRevision
                        || preparationRevision != Interlocked.Read(ref approvalPreparationRevision)
                        || !reviewHostEligible())
                    {
                        await CompleteAuditAsync(proposedAudit, SecurityAuditOutcome.Denied, "approval-admission-changed");
                        activity.Complete(HostOperationOutcome.Cancelled);
                        return;
                    }
                    pendingModelAction = action;
                    pendingModelActionArtifact = artifact;
                    pendingModelActionAudit = proposedAudit;
                    pendingModelActionReview = new(reviewRequest, checked(++modelActionReviewEpoch), proposedAudit,
                        reviewHostEligible);
                    ModelActionReviewStatus = "An action needs review. Open it explicitly, then confirm or reject separately.";
                    NotifyModelActionReviewChanged();
                    OnPropertyChanged(nameof(IsModelActionApprovalPending));
                    OnPropertyChanged(nameof(IsApprovalPending));
                    OnPropertyChanged(nameof(IsResponseInteractionPending));
                    ApproveModelActionCommand.NotifyCanExecuteChanged();
                    ApproveModelActionForSessionCommand.NotifyCanExecuteChanged();
                    ApproveModelActionAlwaysCommand.NotifyCanExecuteChanged();
                    RejectModelActionCommand.NotifyCanExecuteChanged();
                    PresentPendingModelActionReview();
                    WindowActionRequested?.Invoke(this, WindowAction.Show);
                    announceApproval = true;
                }
                else
                {
                    automaticAction = action;
                }
            }
            else
            {
                var presentation = AnswerPresentation.From(decision.Answer!);
                PresentResponse(AssistantState.Information, "Local model response",
                    $"Generated locally; verify important details.\n\n{presentation.Brief}"
                        + (presentation.HasDetails ? "\n\n" + AnswerPresentation.Offer : string.Empty),
                    modelAnswer: new(decision.Answer!));
                await SpeakCurrentResponseAsync(cancellationToken: cancellation.Token, expectedSpeechRevision: speechRevision);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = HostOperationOutcome.Cancelled;
            dependencyBootstrapper.Tasks.Update(
                "local.reasoning", SetupTaskState.Cancelled, "The local request was cancelled.");
            ShowInformation("Local request cancelled.", "No local model answer will continue.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
            or JsonException or InvalidDataException or InvalidOperationException)
        {
            outcome = HostOperationOutcome.Failed;
            ApplicationLog.Error(logger, exception, "Calling the verified local model");
            dependencyBootstrapper.Tasks.Update(
                "local.reasoning", SetupTaskState.Failed, exception.Message);
            var status = new DependencyStatus(
                "local.inference",
                "Local model inference (Ollama)",
                DependencyReadiness.Failed,
                $"Local inference failed: {exception.Message}. Refresh readiness to retry.");
            ApplyDependencyStatus(status);
            dependencyBootstrapper.Tasks.Reconcile(status);
            ShowFailure("Local reasoning failed.", exception.Message);
        }
        finally
        {
            if (ReferenceEquals(activeReasoningArtifact, artifact))
            {
                activeReasoningArtifact = null;
            }
            IsLocalTaskCancellable = false;
            if (ReferenceEquals(activeReasoningCancellation, cancellation))
            {
                activeReasoningCancellation = null;
                RefreshCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanInstallPowerShell));
                OnPropertyChanged(nameof(CanInstallLocalModel));
            }

            cancellation.Dispose();
        }

        if (announceApproval && IsResponseInteractionPending)
        {
            isModelApprovalPromptActive = true;
            try
            {
                await SpeakModelApprovalPromptAsync(speechRevision);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or UnauthorizedAccessException or IOException)
            {
                outcome = HostOperationOutcome.Failed;
                ApplicationLog.Error(logger, exception, "Speaking a model action approval prompt");
                var description = pendingModelQuestion is { } question
                    ? question.Prompt
                    : pendingGrantChange is { } grantChange
                    ? DescribeGrantChange(grantChange)
                    : pendingModelAction is { } pendingAction
                    ? commandCatalog.GetCommands(AssistantName)
                        .Single(command => command.Action == pendingAction).Description
                    : "No model action remains pending.";
                ShowFailure(
                    "The spoken approval prompt could not complete.",
                    $"{description} The action is still awaiting your decision. {exception.Message}");
            }
            finally
            {
                isModelApprovalPromptActive = false;
            }
        }

        if (automaticAction is { } safeAction)
        {
            var admittedCallRevision = CallPolicyRevision;
            if (safeAction == BuiltInAction.LockMachine && !LockSourceCurrent())
            {
                ReportHostInteractionFailure("The original Lock source or host authority changed. No Lock request was dispatched.");
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            if (safeAction != BuiltInAction.LockMachine
                && ModelActionRequiresApproval(safeAction) && !CanReuseLegacyModelGrant(safeAction))
            {
                ShowInformation("Saved permission is not eligible.",
                    "Call policy changed before dispatch. Initiate a fresh request; no action was replayed.");
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            isModelActionDispatchActive = true;
            try
            {
                var command = commandCatalog.GetCommands(AssistantName).Single(item => item.Action == safeAction);
                await ExecuteAsync(command, SecurityAuditInitiator.ModelSuggestion, admittedCallRevision,
                    sourceCurrent: safeAction == BuiltInAction.LockMachine ? LockSourceCurrent : null);
                if (ShouldSpeakResponse(safeAction))
                {
                    await SpeakCurrentResponseAsync(expectedSpeechRevision: speechRevision);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or HttpRequestException or JsonException or InvalidDataException)
            {
                outcome = HostOperationOutcome.Failed;
                ApplicationLog.Error(logger, exception, "Executing a model-suggested built-in action");
                ShowFailure("The model-suggested action failed.", exception.Message);
            }
            finally
            {
                isModelActionDispatchActive = false;
            }
        }
        activity.Complete(outcome);
    }

    private async Task SpeakModelApprovalPromptAsync(long? expectedSpeechRevision = null)
    {
        var wasListening = IsListening;
        if (wasListening)
        {
            await StopListeningAsync();
        }
        if (!IsSpeechResponseEnabled)
        {
            return;
        }

        try
        {
            if (pendingModelQuestion is { } question)
            {
                var options = string.Join(". ", ModelQuestionChoices.Select(choice => choice.DisplayText));
                await SpeakCurrentResponseAsync(
                    $"{question.Prompt} {options}. Choose an option by number, provide your own answer, or say cancel question. "
                    + (RequireAssistantNameForVoiceApproval
                        ? "Begin your answer by addressing me by name."
                        : "You may answer without addressing me by name."),
                    expectedSpeechRevision: expectedSpeechRevision);
            }
            else if (pendingGrantChange is { } change)
            {
                var spoken = DescribeGrantChange(change)
                    + " This only changes permission and does not execute the action. "
                    + (RequireAssistantNameForVoiceApproval
                        ? "Begin your answer by addressing me by name."
                        : "You may answer without addressing me by name.");
                await SpeakCurrentResponseAsync(spoken, expectedSpeechRevision: expectedSpeechRevision);
            }
            else if (pendingModelAction is { } action)
            {
                var description = commandCatalog.GetCommands(AssistantName)
                    .Single(command => command.Action == action).Description;
                var spoken = $"{description} You can grant permission once, for this session, or always; or decline. "
                    + (RequireAssistantNameForVoiceApproval
                        ? "Begin your answer by addressing me by name."
                        : "You may answer without addressing me by name.");
                await SpeakCurrentResponseAsync(spoken, expectedSpeechRevision: expectedSpeechRevision);
                if (State == AssistantState.Failure && IsModelActionApprovalPending)
                {
                    ShowFailure(
                        "The spoken approval prompt failed.",
                        $"{description} The action is still awaiting your decision. {ResponseBody}");
                }
            }
        }
        finally
        {
            // A question never opens capture; each voice reply needs a new explicit activation.
            NotifyVoiceEnablementChanged();
        }
    }

    internal static bool ModelActionRequiresApproval(BuiltInAction action) => action switch
    {
        BuiltInAction.HideApplication or BuiltInAction.ExitApplication
            or BuiltInAction.RestartApplication or BuiltInAction.CancelTask or BuiltInAction.StopAllWork
            or BuiltInAction.LockMachine or BuiltInAction.ProposeShutdown
            or BuiltInAction.ProposeRestart or BuiltInAction.EnableLocalModels
            or BuiltInAction.DisableLocalModels or BuiltInAction.EnableHostedModels
            or BuiltInAction.DisableHostedModels => true,
        BuiltInAction.ShowApplication or BuiltInAction.OpenSettings
            or BuiltInAction.OpenDocumentation or BuiltInAction.OpenSetup
            or BuiltInAction.ShowHelp or BuiltInAction.ShowVersion
            or BuiltInAction.ShowStatus or BuiltInAction.ShowCurrentTaskProgress
            or BuiltInAction.StopSpeaking or BuiltInAction.DisableListening or BuiltInAction.CancelPowerAction
            or BuiltInAction.ShowPowerStatus or BuiltInAction.ListGrants
            or BuiltInAction.ManageGrants or BuiltInAction.ShowModelExecution => false,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported model action."),
    };

    public async Task CancelCurrentTaskAsync()
    {
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
        else if (activeReasoningCancellation is not null)
        {
            var running = activeReasoningTask;
            await activeReasoningCancellation.CancelAsync();
            if (IsSpeaking)
            {
                try
                {
                    await textToSpeech.StopAsync();
                    activeSpokenText = null;
                    IsSpeaking = false;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    ApplicationLog.Error(logger, exception, "Stopping local response speech during cancellation");
                    ShowFailure("Speech output could not be stopped.", exception.Message);
                    return;
                }
            }

            if (running is not null)
            {
                await running;
            }

            var cancelled = SetupTasks.Any(task =>
                string.Equals(task.Id, "local.reasoning", StringComparison.Ordinal)
                && task.State == SetupTaskState.Cancelled);
            ShowInformation(
                cancelled ? "Local request cancelled." : "Local answer already completed.",
                cancelled
                    ? "The local model request was stopped."
                    : "No further model work is running. Speech playback was stopped.");
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

    private async Task ApproveModelActionAsync(
        ModelApprovalScope scope,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        if (!IsHostInputEligible)
        {
            ApplicationLog.Information(logger, "Denied model-action approval outside the eligible host generation");
            return;
        }
        if (pendingModelActionArtifact is { } pendingArtifact)
        {
            var originalReview = pendingModelActionReview;
            var current = artifactCatalogue.IsCurrent(pendingArtifact.Id, pendingArtifact.Digest)
                && await IsFileArtifactSourceCurrent(pendingArtifact, CancellationToken.None);
            if (!ReferenceEquals(pendingModelActionReview, originalReview))
            {
                ReportModelActionReviewUnavailable();
                return;
            }
            if (!current || scope != ModelApprovalScope.Once)
            {
                await ClearPendingModelActionAsync("artifact-revision-ineligible");
                ShowInformation("Fresh single-use approval is required.",
                    "Artifact-based action proposals cannot create reusable grants, and changed artifact revisions require a new request.");
                return;
            }
        }
        var action = ValidateModelActionApproval(
            scope, pendingModelAction, IsBusy, IsLocalModelSetupActive, IsPowerShellSetupActive);

        var approval = pendingModelActionAudit;
        var reviewTarget = pendingModelActionReview;
        var callRevision = CallPolicyRevision;
        var preparation = Interlocked.Read(ref approvalPreparationRevision);
        try
        {
            var command = commandCatalog.GetCommands(AssistantName).Single(item => item.Action == action);
            await StopModelApprovalPromptAsync();
            if (!ReferenceEquals(pendingModelActionAudit, approval))
            {
                ReportModelActionReviewUnavailable();
                return;
            }
            if (!IsHostInputEligible)
            {
                ApplicationLog.Information(logger, "Denied model-action approval after a Windows privacy transition");
                ReportModelActionReviewUnavailable();
                return;
            }
            if (!IsModelActionReviewCurrent(reviewTarget))
            {
                ReportModelActionReviewUnavailable();
                return;
            }
            if (initiator == SecurityAuditInitiator.VoiceCommand
                && !communicationPolicy.Current.Authorization(IsHostInputEligible, true)
                    .AllowsApprovalChannel(RequestOrigin.ActivatedVoice))
            {
                ShowInformation("Spoken approvals disabled during call; UI available.",
                    "The pending operation remains unapproved. No grant was changed and rejected speech will not be replayed.");
                return;
            }

            var expectedGrantRevision = Interlocked.Read(ref reusableGrantRevision);
            if (scope == ModelApprovalScope.Always
                && !await SaveModelApprovalPreferencesAsync(
                    requireAssistantNameForVoiceApproval,
                    alwaysAllowedModelActions.Append(action).Distinct(),
                    initiator, () => ReferenceEquals(pendingModelActionAudit, approval) && IsModelActionReviewCurrent(reviewTarget)
                        && Interlocked.Read(ref reusableGrantRevision) == expectedGrantRevision))
            {
                return;
            }
            if (!ReferenceEquals(pendingModelActionAudit, approval) || !IsModelActionReviewCurrent(reviewTarget))
            {
                ReportModelActionReviewUnavailable();
                return;
            }
            var approvedArtifact = pendingModelActionArtifact;
            await ClearPendingModelActionAsync($"approved-{scope.ToString().ToLowerInvariant()}");
            if (approvedArtifact is not null && !await IsFileArtifactSourceCurrent(approvedArtifact, CancellationToken.None))
            {
                ShowInformation("Definition changed before execution.",
                    "The action was not dispatched. Reload and review the exact definition, then start a fresh request.");
                return;
            }
            if (disposed || !IsHostInputEligible
                || !reviewTarget.Eligible()
                || preparation != Interlocked.Read(ref approvalPreparationRevision)) { return; }
            if (modelPreferenceWriteActive || Interlocked.Read(ref reusableGrantRevision) != expectedGrantRevision)
            {
                ShowFailure("The grant changed before confirmation.", "A concurrent revocation retired this approval continuation. No action was dispatched or grant published.");
                return;
            }
            Interlocked.Increment(ref reusableGrantRevision);

            if (scope == ModelApprovalScope.Session)
            {
                sessionAllowedModelActions.Add(action);
                OnPropertyChanged(nameof(SessionAllowedModelActions));
            }
            else if (scope == ModelApprovalScope.Always)
            {
                alwaysAllowedModelActions.Add(action);
                OnPropertyChanged(nameof(AlwaysAllowedModelActions));
            }

            if (scope != ModelApprovalScope.Once)
            {
                GrantDocumentChanged?.Invoke(this, GetGrantDocument());
            }
            await ExecuteAsync(command, SecurityAuditInitiator.ModelSuggestion, callRevision,
                sourceCurrent: approvedArtifact is null ? null
                    : () => artifactCatalogue.IsCurrent(approvedArtifact.Id, approvedArtifact.Digest));
            if (ShouldSpeakResponse(action))
            {
                await SpeakCurrentResponseAsync();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or JsonException or InvalidDataException)
        {
            ApplicationLog.Error(logger, exception, "Executing an approved model-suggested action");
            ShowFailure("The approved action failed.", exception.Message);
        }
    }

    internal static BuiltInAction ValidateModelActionApproval(
        ModelApprovalScope scope, BuiltInAction? pendingAction,
        bool busy, bool modelSetup, bool powerShellSetup)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if (pendingAction is not { } action)
        {
            throw new InvalidOperationException("No model-suggested action is awaiting approval.");
        }

        if (busy || modelSetup || powerShellSetup)
        {
            throw new InvalidOperationException("A model-suggested action cannot run while setup is active.");
        }

        return action;
    }

    public void RejectPendingModelAction() => new AsyncCommand(
        async () => await RejectPendingModelActionAsync(), HandleCommandException).Execute(null);

    private async Task RejectPendingModelActionCoreAsync()
    {
        if (pendingModelAction is null)
        {
            return;
        }

        await ClearPendingModelActionAsync("user-declined");
        ShowInformation("Model action declined.", "No action was performed.");
    }

    public async Task<bool> RejectPendingModelActionAsync()
    {
        if (!IsModelActionApprovalPending)
        {
            return true;
        }

        var approval = pendingModelActionAudit;
        var reviewTarget = pendingModelActionReview;
        try
        {
            await StopModelApprovalPromptAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ApplicationLog.Error(logger, exception, "Stopping the model approval prompt");
            ShowFailure("Approval prompt could not be stopped.", exception.Message);
            return false;
        }

        if (ReferenceEquals(pendingModelActionAudit, approval) && IsModelActionReviewCurrent(reviewTarget))
        {
            await RejectPendingModelActionCoreAsync();
        }
        else
        {
            ReportModelActionReviewUnavailable();
            return false;
        }

        return true;
    }

    private async Task StopModelApprovalPromptAsync()
    {
        if (!isModelApprovalPromptActive)
        {
            return;
        }

        await textToSpeech.StopAsync();
        activeSpokenText = null;
        IsSpeaking = false;
        if (activeReasoningTask is { } promptTask)
        {
            await promptTask;
        }
    }

    private Task ClearPendingModelActionAsync(string reason)
    {
        var audit = pendingModelActionAudit;
        pendingModelActionAudit = null;
        pendingModelAction = null;
        pendingModelActionArtifact = null;
        pendingModelActionReview = null;
        ModelActionReviewStatus = "The original action review is retired.";
        NotifyModelActionReviewChanged();
        var retirement = audit is null ? Task.CompletedTask : CompleteAuditAsync(audit,
            reason.StartsWith("approved-", StringComparison.Ordinal)
                ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Cancelled, reason);
        OnPropertyChanged(nameof(IsModelActionApprovalPending));
        OnPropertyChanged(nameof(IsApprovalPending));
        OnPropertyChanged(nameof(IsResponseInteractionPending));
        ApproveModelActionCommand.NotifyCanExecuteChanged();
        ApproveModelActionForSessionCommand.NotifyCanExecuteChanged();
        ApproveModelActionAlwaysCommand.NotifyCanExecuteChanged();
        RejectModelActionCommand.NotifyCanExecuteChanged();
        NotifyOutputPolicyChanged();
        return retirement;
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

    private async Task SpeakCurrentResponseAsync(string? spokenText = null, long? expectedSpeechRevision = null,
        CancellationToken cancellationToken = default)
    {
        await TrySpeakCurrentResponseAsync(spokenText, expectedSpeechRevision, cancellationToken: cancellationToken);
    }

    private async Task<bool> TrySpeakCurrentResponseAsync(string? spokenText = null, long? expectedSpeechRevision = null,
        Func<bool>? additionalEligibility = null, bool boundedExactReadback = false,
        Func<CancellationToken, Task>? validateReadSource = null, CallSpeechLease? callSpeechLease = null,
        CancellationToken cancellationToken = default)
    {
        if (speechInterruptionActive) { return false; }
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
        var exactReadback = !boundedExactReadback && (spokenText is not null || IsResponseInteractionPending);
        spokenText ??= $"{ResponseTitle}. {ResponseBody}";
        IsSpeaking = true;
        activeSpokenText = spokenText;
        ApplicationLog.Debug(logger, "Starting spoken response output");
        var completedSpeech = false;
        var startedSpeech = false;
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
            if (exactReadback)
            {
                await communicationPolicy.StartSpeech(Start, Eligible);
            }
            else
            {
                var recovery = await speechConfiguration.StartSummarySpeechAsync(spokenText, communicationPolicy,
                    Eligible, Start, callSpeechLease, cancellationToken);
                if (recovery is not null)
                {
                    spokenSummaryRecovery = recovery;
                    forceVisualResponse = true;
                    NotifyOutputPolicyChanged();
                    if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
                }
            }
            completedSpeech = startedSpeech && Eligible() && !textToSpeech.IsSpeaking;
        }
        catch (OperationCanceledException)
        {
            ApplicationLog.Information(logger, "Spoken response was invalidated by a privacy transition");
            forceVisualResponse = true;
            NotifyOutputPolicyChanged();
            if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
        }
        catch (PlaybackVolumeUnavailableException exception)
        {
            LogSpokenResponseFailure(exception, "Playing a response with unavailable Kora playback volume", additionalEligibility is not null);
            PreserveSpokenResponseFailure("Kora playback volume is zero or unavailable. Full visual output is retained; refresh volume preferences.");
        }
        catch (WindowsSpeechRateUnavailableException exception)
        {
            LogSpokenResponseFailure(exception, "Playing a response with unavailable Windows speech rate", additionalEligibility is not null);
            audioPlaybackSettings.HoldRateUnavailable();
            PreserveSpokenResponseFailure("Windows speech rate is unavailable. Full visual output is retained; explicitly refresh rate preferences.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            LogSpokenResponseFailure(exception, "Playing a response with the selected speech voice", additionalEligibility is not null);
            ClearActiveSpeechVoice();
            PreserveSpokenResponseFailure("The selected speech voice is unavailable. Refresh installed voices; the complete visual response is retained.");
        }
        catch (AudioOutputDeviceUnavailableException exception)
        {
            LogSpokenResponseFailure(exception, "Playing a response through audio output", additionalEligibility is not null);
            HandleAudioOutputFailure(exception, "The selected audio output is unavailable.", preserveResponseOnMute: true);
        }
        catch (InvalidOperationException exception)
        {
            LogSpokenResponseFailure(exception, "Playing a response with text-to-speech", additionalEligibility is not null);
            ClearActiveSpeechVoice();
            PreserveSpokenResponseFailure("Text-to-speech is unavailable. Refresh speech readiness; the complete visual response is retained.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogSpokenResponseFailure(exception, "Playing a spoken response unexpectedly", additionalEligibility is not null);
            PreserveSpokenResponseFailure("Speech playback failed. The complete visual response is retained; no automatic retry.");
        }
        finally
        {
            if (completedSpeech) { CompleteSpeechCaption(); }
            else { RetireSpeechCaption(); }
            activeSpokenText = null;
            if (additionalEligibility is null) { IsSpeaking = false; }
            else { IsSpeaking = textToSpeech.IsSpeaking; }
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
            if (callSpeechLease is not null && textToSpeech.IsSpeaking)
            {
                IsSpeaking = true;
                callSpeechLease.HoldUnconfirmedRelease();
                throw new InvalidOperationException("Owned response playback release is unconfirmed.");
            }
            IsSpeaking = false;
        }
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
            or BuiltInAction.StopAllWork
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
        long? observedCallRevision = null,
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
        if (initiator == SecurityAuditInitiator.ModelSuggestion && ModelActionRequiresApproval(command.Action)
            && !IsModelCallDispatchEligible(initiator, observedCallRevision))
        {
            ShowInformation("Call policy changed.", "The model authorization is no longer applicable. Initiate a fresh request; no action was dispatched.");
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
                await ExitAsync(initiator == SecurityAuditInitiator.ModelSuggestion
                    && isModelActionDispatchActive);
                break;
            case BuiltInAction.RestartApplication:
                await RestartApplicationAsync(initiator, observedCallRevision);
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
                    + (LocalModelsEnabled && Dependencies.Any(status =>
                        string.Equals(status.Id, "local.inference", StringComparison.Ordinal)
                        && status.Readiness == DependencyReadiness.Ready)
                        ? "You can also ask other questions; answers use the verified local model."
                        : LocalModelsEnabled
                            ? "Other questions require a verified local model; open setup to check readiness."
                            : "Local model use is disabled; built-in commands remain available."));
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
                var outstanding = SetupTasks
                    .Where(task => task.State is SetupTaskState.Running
                        or SetupTaskState.NeedsAction or SetupTaskState.Failed)
                    .ToArray();
                ShowInformation(
                    outstanding.FirstOrDefault(task => task.State == SetupTaskState.Running) is { } active
                        ? $"Setting up {active.Name}."
                        : outstanding.Length > 0
                            ? "Setup needs attention."
                            : IsVoiceEnabled ? "Waiting for your command." : "Voice is not active.",
                    outstanding.Length > 0
                        ? string.Join(" ", outstanding.Select(task =>
                            $"{task.Name}: {task.State} — {task.Detail}"))
                        : IsVoiceEnabled ? ListeningStatus : "Enable listening or use the typed proof field.");
                break;
            case BuiltInAction.ShowCurrentTaskProgress:
                var currentTask = dependencyBootstrapper.Tasks.ActiveTask;
                if (currentTask is not null)
                {
                    ShowInformation(
                        $"{currentTask.Name}: running.",
                        currentTask.ProgressPercentage is { } percentage
                            ? $"Current stage: {currentTask.Detail} Completion: {percentage}%."
                            : $"Current stage: {currentTask.Detail} No completion percentage is available for this task.");
                    break;
                }

                var nextTask = SetupTasks.FirstOrDefault(task =>
                    task.State is SetupTaskState.Queued or SetupTaskState.NeedsAction or SetupTaskState.Failed);
                ShowInformation(
                    "No task is running.",
                    nextTask is null
                        ? "There is no active or pending setup task."
                        : $"{nextTask.Name}: {nextTask.State}. {nextTask.Detail}");
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
                if (initiator == SecurityAuditInitiator.ModelSuggestion)
                {
                    ShowInformation("Model listening control is unavailable.",
                        "Use the exact typed command or native Disable listening.");
                    break;
                }
                await DisableListeningAsync(initiator);
                break;
            case BuiltInAction.LockMachine:
                if (bundledAction is not null)
                {
                    await InvokeBundledAction(bundledAction, Kora.Core.Skills.FixedSessionAction.Lock, initiator, observedCallRevision, sourceCurrent);
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
                        initiator, observedCallRevision, sourceCurrent);
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
            case BuiltInAction.ListGrants:
                var markdown = GetGrantDocument();
                GrantDocumentRequested?.Invoke(this, markdown);
                ShowInformation(
                    "Model-action grants",
                    "The current session and always grants are shown in the grant document window.");
                break;
            case BuiltInAction.ManageGrants:
                ShowInformation(
                    "Manage grants",
                    "Select an action, Add, Remove, or Move, and the grant scope. For Move, select the destination after 'to'. Prepare the exact change before confirming it.");
                IsGrantEditorVisible = true;
                WindowActionRequested?.Invoke(this, WindowAction.Show);
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

    private async Task RestartApplicationAsync(SecurityAuditInitiator initiator, long? observedCallRevision)
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
                    || !Eligible() || !IsModelCallDispatchEligible(initiator, observedCallRevision))
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
                await StopAudioAsync(initiator == SecurityAuditInitiator.ModelSuggestion
                    && isModelActionDispatchActive);
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
                if (!Eligible() || !IsModelCallDispatchEligible(initiator, observedCallRevision))
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
        RetireWebResultDetails(retireControl: false);
        RetireSpeechCaptionSource();
        ClearResponseActions();
        State = responseState;
        spokenSummaryRecovery = null;
        ResponseTitle = title;
        ResponseBody = body;
        forceVisualResponse = ShouldForceVisualResponse(
            IsGrantEditorVisible, IsResponseInteractionPending, responseState);
        sourceAssigned?.Invoke();
        BindCurrentResponseSpeech(!isInitializing && sourceAssigned is null
            && responseState is AssistantState.Information or AssistantState.Success
            && !IsResponseInteractionPending && !IsGrantEditorVisible, modelAnswer);
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
        OnPropertyChanged(nameof(CanInspectWindowsSpeechRate));
        OnPropertyChanged(nameof(CanChangeWindowsSpeechRate));
        OnPropertyChanged(nameof(CanInspectWindowsSpeechRateNative));
        OnPropertyChanged(nameof(CanChangeWindowsSpeechRateNative));
        OnPropertyChanged(nameof(CanChangeSpeechInputNative));
        OnPropertyChanged(nameof(CanChangeConversationalReplies));
        OnPropertyChanged(nameof(CanControlLogonStartup));
        OnPropertyChanged(nameof(CanControlLogonStartupNative));
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
    }

    private string[] GetRecognitionPhrases()
    {
        var commands = commandCatalog.GetCommands(AssistantName)
            .SelectMany(command => command.AllPhrases)
            .Concat([ReviewPendingActionPhrase])
            .Concat(ClipboardCommand.FixedPhrases)
            .Concat(ClipboardSnapshotStatusCommand.DiscoveryPhrases)
            .Concat(LocalFileCommand.FixedPhrases)
            .Concat(PreparedLocalSearchCommand.DiscoveryPhrases)
            .Concat(AttachmentCataloguePreparationCommand.DiscoveryPhrases)
            .Concat(SessionCommand.DiscoveryPhrases)
            .Concat(Kora.Application.Mcp.McpCommand.FixedPhrases)
            .Concat([Kora.Application.Tools.CustomizationDiscoveryCommand.Phrase])
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
            .Concat(SpeechComparisonCommand.FixedPhrases)
            .Concat(DiagnosticRetentionCommand.FixedPhrases)
            .Concat(AuditRetentionCommand.FixedPhrases)
            .Concat(ManualCallCommand.FixedPhrases)
            .Concat(ResponseModeCommand.FixedPhrases)
            .Concat(ResponseWindowCommand.FixedPhrases)
            .Concat(ProviderModeCommand.FixedPhrases)
            .Concat(ProviderProfileCommand.FixedPhrases)
            .Concat(SessionProviderProfileCommand.FixedPhrases)
            .Concat(PreapprovedUriCommand.FixedPhrases)
            .Concat(WebPageCommand.FixedPhrases)
            .Concat(WebSummaryCommand.FixedPhrases)
            .Concat(webSummaryReview is { } summaryReview
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
            .Concat(RegisteredSettingSuggestionCommand.FixedPhrases)
            .Concat(RegisteredSettingUndoCommand.FixedPhrases)
            .Concat(Kora.Application.Skills.OwnedSkillAuthoringCommand.FixedPhrases)
            .Concat(Kora.Application.Skills.SkillReviewCommand.FixedPhrases)
            .Concat(SettingsSectionCommand.FixedPhrases)
            .Concat(LogonStartupCommand.FixedPhrases)
            .Concat(Kora.Core.Maintenance.MaintenanceCommandParser.FixedPhrases)
            .Concat(Kora.Core.Interaction.LocalEventCommand.FixedPhrases)
            .Concat(ClipboardPreview is { } snapshot
                ? ["reuse clipboard snapshot " + snapshot.SnapshotId.ToString("D")] : []);
        return commands
            .SelectMany(phrase => new[] { phrase, $"{AssistantName} {phrase}" })
            .Concat(ModelApprovalSpeech.GetPhrases(AssistantName))
            .Concat(pendingModelQuestion is { } question
                ? LocalModelQuestionSpeech.GetPhrases(question, AssistantName)
                : [])
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