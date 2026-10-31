using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

using Kora.Application.Documentation;
using Kora.Application.ViewModels;
using Kora.Application;
using Kora.Application.Maintenance;
using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Configuration;
using Kora.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App : Avalonia.Application
{
    private SystemTrayController? systemTray;
    private SettingsWindowController? settingsWindow;
    private MicrophoneRecoveryWindowController? microphoneRecovery;
    private DocumentationWindowController? documentationWindow;
    private DetailWindowController? detailWindow;
    private ResponseWindowController? responseWindow;
    private SpeechCaptionWindowController? speechCaptionWindow;
    private GrantListWindowController? grantListWindow;
    private QuestionWindowController? questionWindow;
    private EvidenceWindowController? evidenceWindow;
    private SkillPackagesWindowController? skillPackagesWindow;
    private OwnedInstructionEditorController? instructionEditor;
    private CustomizationWindowController? customizations;
    private PromptParameterController? promptParameters;
    private SessionsWindowController? sessionsWindow;
    private NewRequestWindowController? newRequestWindow;
    private AllWorkWindowController? allWorkWindow;
    private WorkspaceSurfaceCommandController? workspaceSurfaceCommands;
    private ClipboardPreviewWindowController? clipboardWindow;
    private CopilotTransmissionWindowController? copilotTransmissionWindow;
    private CopilotAccountWindowController? copilotAccountWindow;
    private LocalFilePreviewWindowController? fileWindow;
    private SessionAttachmentWindowController? attachmentWindow;
    private AttachmentCatalogueWindowController? attachmentCatalogue;
    private MaintenanceWindowController? maintenanceWindow;
    private MainViewModel? viewModel;
    private ILogger<App>? logger;
    private PresenceWindowController? presenceWindows;
    private readonly DispatcherTimer presenceRendererTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Func<HostActivity>? rendererSwitchContext;

    internal static ServiceProvider Services { get; set; } = null!;
    internal DetailWindowController? Details => detailWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
        => HostRequestRunner.Run(RequestOrigin.HostSystem, InitializeFramework,
            HostActivityLayer.Desktop, HostOperation.Startup);

    private void InitializeFramework()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Services.GetRequiredService<AppearanceConfigurationService>().ConfigureNativePresenceStartupDefault(
                PresenceRendererSelection.UseNative(Environment.GetEnvironmentVariable(PresenceRendererSelection.EnvironmentVariable)));
            viewModel = Services.GetRequiredService<MainViewModel>();
            Services.GetRequiredService<DesktopSessionWorkspaceAccess>().Bind(viewModel);
            viewModel.BindWebSummaryWorkflow(Services.GetRequiredService<Kora.Application.Network.WebSummaryWorkflow>());
            viewModel.BindExternalBrowserWorkflow(Services.GetRequiredService<Kora.Application.Network.ExternalBrowser.ExternalBrowserWorkflow>());
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.PrivacyClosureRequested += OnPrivacyClosureRequested;
            ApplyThemeMode(viewModel.ThemeMode);
            logger = Services.GetRequiredService<ILogger<App>>();
            BindHandoffPresentation(viewModel);
            DesktopLog.Information(logger, "Initializing the Kora desktop application");
            var hostViewModel = viewModel;
            presenceWindows = new PresenceWindowController((native, startup) =>
            {
                var replacement = new MainWindow(hostViewModel,
                    Services.GetRequiredService<ILogger<MainWindow>>(), native, startup,
                    Services.GetRequiredService<ILogger<Kora.Application.Visuals.PresenceRenderWorker>>());
                desktop.MainWindow = replacement;
                return replacement;
            }, () => fileWindow?.IsOwnerInUse != true && questionWindow?.IsOwnerInUse != true);
            presenceWindows.Start(viewModel.IsNativePresenceRenderer);
            presenceRendererTimer.Tick += OnPresenceRendererTick;
            viewModel.ReportPresenceRendererStatus(viewModel.IsNativePresenceRenderer
                ? "Experimental animation mode selected; starting animation." : "Standard animation mode selected; starting animation.");
            microphoneRecovery = new MicrophoneRecoveryWindowController(viewModel,
                Services.GetRequiredService<ILogger<MicrophoneRecoveryWindow>>());
            var captionView = new SpeechCaptionWindow(viewModel);
            speechCaptionWindow = new SpeechCaptionWindowController(viewModel, captionView,
                new AvaloniaCaptionDisplaySource(captionView.Screens));
            settingsWindow = new SettingsWindowController(
                viewModel,
                Services.GetRequiredService<ILogger<SettingsWindowController>>(),
                microphoneRecovery.Open, speechCaptionWindow);
            MarkdownDocumentRenderer.Renderer = new NativeDetailRenderer(
                Services.GetRequiredService<ILogger<NativeDetailRenderer>>());
            detailWindow = new DetailWindowController(
                Services.GetRequiredService<IUserDocumentationProvider>(), viewModel,
                Services.GetRequiredService<ILogger<DetailWindowController>>(),
                Services.GetRequiredService<ILogger<NativeDetailRenderer>>(),
                Services.GetRequiredService<ILogger<StaticHtmlDetailRenderer>>());
            detailWindow.BindHistoryAccess(() => Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>().CanInspect);
            documentationWindow = new DocumentationWindowController(
                Services.GetRequiredService<IUserDocumentationProvider>(),
                viewModel,
                Services.GetRequiredService<ILogger<DocumentationWindowController>>(),
                detailWindow);
            viewModel.BindReviewedVoiceCommand(
                Services.GetRequiredService<ILogger<Kora.Application.Voice.ReviewedVoiceCommandCapture>>(),
                Services.GetRequiredService<TimeProvider>());
            responseWindow = new ResponseWindowController(
                viewModel,
                Services.GetRequiredService<ILogger<ResponseWindowController>>(), detailWindow);
            grantListWindow = new GrantListWindowController(viewModel);
            BindExactGrants(viewModel);
            Services.GetRequiredService<Kora.Windows.Storage.WindowsSqliteHostInteractionStore>()
                .BindAuthorizationPolicyGate(viewModel.AuthorizationPolicyGate);
            var nativeQuestions = new NativeQuestionHost(Services.GetRequiredService<Kora.Windows.Storage.WindowsSqliteHostInteractionStore>(),
                Services.GetRequiredService<TimeProvider>(), Services.GetRequiredService<ILogger<NativeQuestionViewModel>>(),
                questions => viewModel.BindQuestionVoiceReplies(questions, Services.GetRequiredService<TimeProvider>(),
                    Services.GetRequiredService<ILogger<Kora.Application.Interaction.HostQuestionVoiceReplyService>>()));
            nativeQuestions.BindWorkspace(Services.GetRequiredService<Kora.Application.Hosting.SessionWorkspaceService>());
            nativeQuestions.BindPrivateRevision(() => Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>().ControlRevision);
            questionWindow = new QuestionWindowController(viewModel,
                nativeQuestions,
                Services.GetRequiredService<Kora.Application.Hosting.DurableVersionQuery>(),
                Services.GetRequiredService<IApplicationInfo>(),
                new NativeDetailRenderer(Services.GetRequiredService<ILogger<NativeDetailRenderer>>()),
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsCapabilityAdmissionOpen
                    && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
                Services.GetRequiredService<ILogger<QuestionWindowController>>(),
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision);
            skillPackagesWindow = new SkillPackagesWindowController(viewModel,
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                    && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
                Services.GetRequiredService<ILogger<SkillPackagesWindowController>>(),
                new NativeDetailRenderer(Services.GetRequiredService<ILogger<NativeDetailRenderer>>()),
                Services.GetRequiredService<Kora.Application.Skills.SharedSkillDiscoveryService>(),
                Services.GetRequiredService<ILogger<SharedSkillSourcesWindowController>>());
            BindBundledRuntimeSetup();
            viewModel.BindSkillReviewNavigation(skillPackagesWindow,
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision);
            BindSharedSkillInvocation(viewModel, skillPackagesWindow);
            BindAgentRuns(viewModel);
            BindCustomizationDiscovery(viewModel);
            instructionEditor = new(viewModel,
                Services.GetRequiredService<Kora.Application.Definitions.OwnedInstructionEditorActions>(),
                Services.GetRequiredService<Kora.Application.Definitions.InstructionSaveWorkflow>(),
                Services.GetRequiredService<ArtifactCatalogueReloadService>(),
                Services.GetRequiredService<Kora.Core.Dependencies.IApplicationDataPaths>(),
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                    && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision,
                Services.GetRequiredService<ILogger<OwnedInstructionEditorController>>(),
                Services.GetRequiredKeyedService<Kora.Application.Definitions.OwnedInstructionEditorActions>(Kora.Core.Artifacts.ArtifactKind.Prompt),
                Services.GetRequiredKeyedService<Kora.Application.Definitions.InstructionSaveWorkflow>(Kora.Core.Artifacts.ArtifactKind.Prompt),
                Services.GetRequiredService<Kora.Application.Definitions.ProfileInstructionForkWorkflow>(),
                refinement: Services.GetRequiredService<Kora.Application.Definitions.OwnedInstructionRefinementService>(),
                saveQuestions: questionWindow,
                profileOriginals: Services.GetRequiredService<Kora.Application.Definitions.ProfileInstructionEditorWorkflow>(),
                dictation: viewModel.BindComposerDictation(Services.GetRequiredService<ILogger<Kora.Application.Voice.ComposerDictationService>>()));
            instructionEditor.Bind();
            customizations = new(viewModel,
                Services.GetRequiredService<Kora.Application.Definitions.CustomizationManagementService>(),
                Services.GetRequiredService<Kora.Application.Definitions.CustomizationAuthoringService>(),
                Services.GetRequiredService<Kora.Core.Artifacts.ArtifactCatalogue>(),
                Services.GetRequiredService<ArtifactCatalogueReloadService>(),
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                    && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision,
                Services.GetRequiredService<ILogger<CustomizationWindowController>>(), agentAuthoring!.Add);
            customizations.Bind();
            BindInstructionUse(viewModel);
            promptParameters = new(viewModel, Services.GetRequiredService<Kora.Core.Artifacts.ArtifactCatalogue>(),
                Services.GetRequiredService<Kora.Application.Definitions.PromptRequestConsumer>(),
                Services.GetRequiredService<Kora.Application.Definitions.PromptConversationSubmission>(),
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                    && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision,
                Services.GetRequiredService<ILogger<PromptParameterController>>(),
                viewModel.BindPromptInputVoice(Services.GetRequiredService<ILogger<Kora.Application.Definitions.PromptInputVoiceController>>()));
            promptParameters.Bind();
            viewModel.PrivacyClosureRequested += (_, _) =>
                Services.GetRequiredService<Kora.Application.Definitions.OwnedInstructionEditorActions>().SuspendReviewedUse();
            viewModel.PrivacyClosureRequested += (_, _) =>
                Services.GetRequiredKeyedService<Kora.Application.Definitions.OwnedInstructionEditorActions>(Kora.Core.Artifacts.ArtifactKind.Prompt)
                    .SuspendReviewedUse();
            viewModel.BindOwnedInstructionFreshnessWithCancellation((candidate, token) =>
            {
                var ownership = Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision;
                var eligible = viewModel.CaptureInstructionEditorEligibility();
                var actions = candidate.Kind == Kora.Core.Artifacts.ArtifactKind.Prompt
                    ? Services.GetRequiredKeyedService<Kora.Application.Definitions.OwnedInstructionEditorActions>(Kora.Core.Artifacts.ArtifactKind.Prompt)
                    : Services.GetRequiredService<Kora.Application.Definitions.OwnedInstructionEditorActions>();
                return actions
                    .IsCurrent(candidate, () => eligible()
                        && Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision == ownership, token);
            });
            viewModel.BindFileArtifactFreshness((candidate, token) =>
            {
                var ownership = Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision;
                var eligible = viewModel.CaptureInstructionEditorEligibility();
                return Services.GetRequiredService<ArtifactCatalogueReloadService>().IsCurrentAsync(candidate,
                    () => eligible() && Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision == ownership, token);
            });
            systemTray = new SystemTrayController(
                viewModel,
                Services.GetRequiredService<ILogger<SystemTrayController>>(),
                () => evidenceWindow?.Open(),
                () => sessionsWindow?.Open(),
                microphoneRecovery.Open,
                () => newRequestWindow?.Open(),
                () => customizations.Open());
            settingsWindow.EditOwnedInstructions = () => instructionEditor.Open();
            settingsWindow.InspectSkillPackages = () => skillPackagesWindow.Open();
            settingsWindow.InspectExactGrants = () => exactGrants?.Open();
            settingsWindow.ReviewLocalVersion = () => questionWindow.ReviewVersionAsync(GetPresenceOwner());
            settingsWindow.ReturnToVersionQuestion = () => questionWindow.ReturnToCurrentQuestionAsync(GetPresenceOwner());
            evidenceWindow = new EvidenceWindowController(viewModel,
                Services.GetRequiredService<Kora.Application.Diagnostics.DurableEvidenceQuery>(),
                Services.GetRequiredService<Kora.Core.Diagnostics.IEvidenceQueryAccess>(),
                Services.GetRequiredService<ILogger<EvidenceViewModel>>(),
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(),
                CreateEvidenceAnswers(), Services.GetRequiredService<TimeProvider>());
            evidenceWindow.Bind();
            BindSettingInspection(settingsWindow);
            fileWindow = new LocalFilePreviewWindowController(viewModel, GetPresenceOwner);
            var sessionFiles = Services.GetRequiredService<SessionFileAttachmentService>();
            Services.GetRequiredService<SessionWorkspaceService>().BindAttachmentLifecycle(sessionFiles);
            viewModel.BindSessionAttachments(sessionFiles);
            attachmentWindow = new SessionAttachmentWindowController(viewModel.ReportHostInteractionFailure, sessionFiles,
                Services.GetRequiredService<SessionWorkspaceService>(),
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(), fileWindow, viewModel);
            viewModel.BindSessionAttachmentCommands(new(Services.GetRequiredService<SessionWorkspaceService>(), sessionFiles,
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(), attachmentWindow));
            attachmentCatalogue = new(viewModel, Services.GetRequiredService<SessionWorkspaceService>(), sessionFiles,
                attachmentWindow, Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(),
                Services.GetRequiredService<ILogger<AttachmentCatalogueViewModel>>(),
                Services.GetRequiredService<SessionAttachmentCatalogueService>());
            sessionsWindow = new SessionsWindowController(viewModel,
                Services.GetRequiredService<Kora.Application.Hosting.SessionWorkspaceService>(),
                Services.GetRequiredService<Kora.Application.Diagnostics.DurableEvidenceQuery>(),
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(),
                Services.GetRequiredService<ILogger<SessionsViewModel>>(),
                Services.GetRequiredService<Kora.Application.Interaction.LocalEventBroker>(), detailWindow,
                Services.GetRequiredService<Kora.Application.Memory.MemoryManagementService>(), attachmentWindow,
                Services.GetRequiredService<Kora.Application.Configuration.SessionProviderProfileConfigurationService>(),
                Services.GetRequiredService<SessionConversationService>(),
                Services.GetRequiredService<ScopedInstructionUseBroker>(),
                Services.GetRequiredService<SessionInstructionScopeCommandService>(),
                Services.GetRequiredService<SessionInstructionScopeBroker>(),
                Services.GetRequiredService<Kora.Application.Dependencies.VerifiedLocalHandoffOperation>(),
                Services.GetRequiredService<SessionRetentionApplyService>(),
                Services.GetRequiredService<Kora.Application.Configuration.DetailPresentationConfigurationService>());
            sessionsWindow.Bind();
            sessionsWindow.CreateInteraction = (state, owner) =>
                responseWindow.PrepareSessions(state, owner, sessionsWindow.Open,
                    () => sessionsWindow.OpenNewRequest?.Invoke());
            responseWindow.PrepareDetailsHost = sessionsWindow.PrepareDetailsHost;
            viewModel.BindSessionRetentionCommands(Services.GetRequiredService<SessionRetentionCommandService>(),
                sessionsWindow.OpenRetentionReviewAsync);
            newRequestWindow = new(viewModel, Services.GetRequiredService<SessionWorkspaceService>(),
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(),
                () => Services.GetRequiredService<NewRequestComposer>(), sessionsWindow,
                Services.GetRequiredService<IUiDispatcher>());
            newRequestWindow.BindDictation(Services.GetRequiredService<ILogger<Kora.Application.Voice.ComposerDictationService>>());
            newRequestWindow.Bind();
            BindSessionRouting(viewModel);
            ConversationShutdownGuard.Bind(viewModel, newRequestWindow, sessionsWindow, sessionRouting);
            BindApplicationLifecycle(viewModel, nativeQuestions);
            sessionsWindow.ConversationQuestions = questionWindow;
            sessionsWindow.OpenNewRequest = newRequestWindow.Open;
            sessionsWindow.OpenRetainedAttachments = attachmentCatalogue.Open;
            sessionsWindow.OpenPermissionGrants = (target, admitted) => exactGrants?.Open(target, admitted);
            sessionsWindow.SynchronizePermissionGrants = () => exactGrants?.SynchronizeSessionAdmission();
            sessionsWindow.ReviewOriginalMessageReuse = newRequestWindow.ReviewOriginalReuseAsync;
            allWorkWindow = new AllWorkWindowController(viewModel,
                Services.GetRequiredService<SessionWorkspaceService>(),
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(),
                Services.GetRequiredService<ILogger<AllWorkViewModel>>(), sessionsWindow);
            allWorkWindow.Bind();
            sessionsWindow.OpenAllWork = allWorkWindow.Open;
            workspaceSurfaceCommands = new(viewModel,
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(),
                allWorkWindow, sessionsWindow);
            workspaceSurfaceCommands.Bind();
            var localEvents = Services.GetRequiredService<Kora.Application.Interaction.LocalEventBroker>();
            Services.GetRequiredService<SessionWorkspaceService>().BindLocalEvents(localEvents);
            viewModel.BindLocalEvents(localEvents);
            var sessionRetention = Services.GetRequiredService<SessionRetentionService>();
            Services.GetRequiredService<SessionWorkspaceService>().BindRetention(sessionRetention);
            sessionRetention.Revoking += sessionsWindow.RevokeSession;
            sessionRetention.Revoking += newRequestWindow.RevokeSession;
            sessionRetention.Revoking += session => Avalonia.Threading.Dispatcher.UIThread.Invoke(() => detailWindow.RevokeSession(session));
            sessionRetention.Revoking += viewModel.RevokeSessionPresentation;
            sessionRetention.Revoking += session => attachmentWindow?.RevokeSession(session);
            var memories = Services.GetRequiredService<Kora.Application.Memory.MemoryManagementService>();
            sessionRetention.Revoking += memories.ExpireSession;
            Services.GetRequiredService<SessionWorkspaceService>().SessionLifecycleChanged += viewModel.RevokeSessionPresentation;
            Services.GetRequiredService<SessionWorkspaceService>().SessionRetired += viewModel.RevokeSessionPresentation;
            sessionRetention.Failed += viewModel.ReportHostInteractionFailure;
            sessionRetention.Start();
            viewModel.BindSessionCommands(Services.GetRequiredService<Kora.Application.Hosting.SessionWorkspaceService>(),
                Services.GetRequiredService<SessionRequestCommandService>(),
                Services.GetRequiredService<ConversationSearchCommandService>());
            viewModel.BindRemainingWork(Services.GetRequiredService<SessionRemainingWorkCommandService>(),
                Services.GetRequiredService<SessionConversationService>());
            viewModel.BindSpeechSummarySuggestions(Services.GetRequiredService<SessionConversationService>(),
                Services.GetRequiredService<TimeProvider>());
            viewModel.BindSessionProviderProfiles(Services.GetRequiredService<Kora.Application.Configuration.SessionProviderProfileConfigurationService>());
            viewModel.BindMemoryCommands(memories);
            clipboardWindow = new ClipboardPreviewWindowController(viewModel);
            Services.GetRequiredService<Kora.Application.Context.ClipboardPermissionSourceUsePolicy>()
                .BindPresenter((proposal, generation, current, token) =>
                    string.Equals(proposal.Binding.ActionId, Kora.Application.Context.ClipboardPermissionSourceUsePolicy.ReadActionId, StringComparison.Ordinal)
                        ? questionWindow.RequestClipboardPermissionAsync(null, proposal, generation, current, token)
                        : clipboardWindow.RequestPermissionAsync(questionWindow, proposal, generation, current, token));
            if (Services.GetService<Kora.Application.Dependencies.HostedTransmissionReviewService>() is { } hostedReviews)
            {
                copilotTransmissionWindow = new CopilotTransmissionWindowController(viewModel, hostedReviews,
                Services.GetRequiredService<ILogger<CopilotTransmissionWindowController>>());
            }
            copilotAccountWindow = new CopilotAccountWindowController(viewModel,
                Services.GetRequiredService<Kora.Windows.Dependencies.CopilotAccountBroker>(),
                Services.GetRequiredService<Kora.Application.Voice.AudioControlAdmission>(),
                Services.GetRequiredService<ILogger<CopilotAccountWindowController>>());
            viewModel.BindFilePreview(Services.GetRequiredService<Kora.Tools.Files.LocalFilePreview>(), fileWindow,
                Services.GetRequiredService<Kora.Tools.Files.LocalFileSearch>(), fileWindow,
                Services.GetRequiredService<Kora.Tools.Files.LocalFileRefresh>());
            viewModel.BindFileAnswer(Services.GetRequiredService<Kora.Tools.Files.LocalFileAnswer>());
            viewModel.BindFolderAnswer(Services.GetRequiredService<Kora.Tools.Files.FolderAnswers.LocalFolderAnswer>());
            maintenanceWindow = new MaintenanceWindowController(viewModel,
                Services.GetRequiredService<MaintenanceViewModel>(),
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsCapabilityAdmissionOpen
                    && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired);
            var maintenanceCommands = Services.GetRequiredService<MaintenanceCommands>();
            Services.GetRequiredService<MaintenanceViewModel>().BindCachedCommands(maintenanceCommands,
                () => viewModel.CanRunMaintenanceCommands);
            viewModel.BindMaintenanceCommands(maintenanceCommands);
            var host = viewModel;
            host.BindClipboardOwnershipGate(() => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsCapabilityAdmissionOpen
                && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired);
            host.BindCallOwnershipGate(() => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision);
            host.BindVoiceOwnershipGate(() => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsCapabilityAdmissionOpen
                && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired);
            var dispatcher = Services.GetRequiredService<IUiDispatcher>();
            Services.GetRequiredService<DesktopInstanceOwnershipBridge>().BindCallbacks(
                cancellationToken => dispatcher.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    host.ShowApplication();
                    return Task.CompletedTask;
                }),
                async cancellationToken =>
                {
                    var quiescent = false;
                    await dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            quiescent = await host.TryPrepareHandoffAsync();
                            if (quiescent)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                desktop.Shutdown();
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            if (quiescent)
                            {
                                host.AbandonHandoffPreparation();
                            }
                            throw;
                        }
                    });
                    return quiescent;
                },
                _ => dispatcher.InvokeAsync(() =>
                {
                    host.AbandonHandoffPreparation();
                    return Task.CompletedTask;
                }));
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
        => HostRequestRunner.Run(RequestOrigin.HostSystem, DisposeDesktopControllers,
            HostActivityLayer.Desktop, HostOperation.Recovery);

    private void DisposeDesktopControllers()
    {
        presenceRendererTimer.Stop();
        presenceRendererTimer.Tick -= OnPresenceRendererTick;
        presenceWindows?.Stop();
        applicationLifecycle?.Dispose();
        applicationLifecycle = null;
        rendererSwitchContext = null;
        DisposeHandoffPresentation();
        Services.GetRequiredService<DesktopInstanceOwnershipBridge>().UnbindCallbacks();
        if (logger is not null)
        {
            DesktopLog.Information(logger, "Shutting down the Kora desktop application");
        }
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.PrivacyClosureRequested -= OnPrivacyClosureRequested;
            viewModel = null;
        }
        systemTray?.Dispose();
        instructionEditor?.Dispose();
        customizations?.Dispose();
        customizations = null;
        instructionEditor = null;
        instructionUse?.Dispose();
        instructionUse = null;
        promptParameters?.Dispose();
        promptParameters = null;
        systemTray = null;
        settingsWindow?.Dispose();
        settingsWindow = null;
        microphoneRecovery?.Dispose();
        microphoneRecovery = null;
        documentationWindow?.Dispose();
        documentationWindow = null;
        detailWindow?.Dispose();
        detailWindow = null;
        responseWindow?.Dispose();
        speechCaptionWindow?.Dispose();
        speechCaptionWindow = null;
        responseWindow = null;
        grantListWindow?.Dispose();
        grantListWindow = null;
        exactGrants?.Dispose();
        exactGrants = null;
        questionWindow?.Dispose();
        questionWindow = null;
        evidenceWindow?.Dispose();
        evidenceWindow = null;
        bundledFixedActions?.Dispose();
        bundledFixedActions = null;
        skillPackagesWindow?.Dispose();
        sharedSkillInvocation?.Dispose();
        skillPackagesWindow = null;
        workspaceSurfaceCommands?.Dispose();
        workspaceSurfaceCommands = null;
        allWorkWindow?.Dispose();
        allWorkWindow = null;
        sessionRouting?.Dispose();
        sessionRouting = null;
        sessionsWindow?.Dispose();
        sessionsWindow = null;
        newRequestWindow?.Dispose();
        newRequestWindow = null;
        attachmentCatalogue?.Dispose();
        attachmentCatalogue = null;
        attachmentWindow?.Dispose();
        attachmentWindow = null;
        clipboardWindow?.Dispose();
        clipboardWindow = null;
        copilotTransmissionWindow?.Dispose();
        copilotTransmissionWindow = null;
        copilotAccountWindow?.Dispose();
        copilotAccountWindow = null;
        fileWindow?.Dispose();
        fileWindow = null;
        maintenanceWindow?.Dispose();
        maintenanceWindow = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.Equals(eventArgs.PropertyName, nameof(MainViewModel.IsNativePresenceRenderer), StringComparison.Ordinal)
            && viewModel is not null && presenceWindows is not null)
        {
            PresenceSwitchTiming.Mark(PresenceSwitchPhase.DesktopRequest);
            rendererTimingFirstTick = true;
            presenceWindows.Request(viewModel.IsNativePresenceRenderer);
            if (presenceWindows.HasPendingChange)
            {
                rendererSwitchContext = HostActivity.CaptureContinuation(HostActivityLayer.Desktop,
                    HostOperation.Presentation, RequestOrigin.LocalUi);
                viewModel.ReportPresenceRendererStatus(
                    "Animation mode saved. Waiting for startup, dragging and open dialogs to finish.");
                presenceRendererTimer.Start();
                PresenceSwitchTiming.Mark(PresenceSwitchPhase.TimerStarted);
            }
            else
            {
                presenceRendererTimer.Stop();
                rendererSwitchContext = null;
                viewModel.ReportPresenceRendererStatus(viewModel.IsNativePresenceRenderer
                    ? "Experimental animation mode selected." : "Standard animation mode selected.");
            }
        }
        if (string.Equals(
            eventArgs.PropertyName,
            nameof(MainViewModel.ThemeMode),
            StringComparison.Ordinal)
            && viewModel is not null)
        {
            ApplyThemeMode(viewModel.ThemeMode);
        }

    }

    private void OnPrivacyClosureRequested(object? sender, EventArgs eventArgs)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows.ToArray())
            {
                window.Hide();
            }
        }
    }

    private void ApplyThemeMode(ApplicationThemeMode mode)
    {
        RequestedThemeVariant = mode switch
        {
            ApplicationThemeMode.System => ThemeVariant.Default,
            ApplicationThemeMode.Light => ThemeVariant.Light,
            ApplicationThemeMode.Dark => ThemeVariant.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "The appearance theme is invalid."),
        };
        if (logger is not null)
        {
            DesktopLog.Information(logger, "Application appearance theme updated");
        }
    }
}