using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using Kora.Application.Documentation;
using Kora.Application.ViewModels;
using Kora.Application;
using Kora.Application.Maintenance;
using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Application.Hosting;

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
    private PromptParameterController? promptParameters;
    private SessionsWindowController? sessionsWindow;
    private NewRequestWindowController? newRequestWindow;
    private AllWorkWindowController? allWorkWindow;
    private ClipboardPreviewWindowController? clipboardWindow;
    private LocalFilePreviewWindowController? fileWindow;
    private SessionAttachmentWindowController? attachmentWindow;
    private AttachmentCatalogueWindowController? attachmentCatalogue;
    private SavedDraftCatalogueWindowController? savedDraftCatalogue;
    private MaintenanceWindowController? maintenanceWindow;
    private MainViewModel? viewModel;
    private ILogger<App>? logger;

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
            viewModel = Services.GetRequiredService<MainViewModel>();
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.PrivacyClosureRequested += OnPrivacyClosureRequested;
            ApplyThemeMode(viewModel.ThemeMode);
            logger = Services.GetRequiredService<ILogger<App>>();
            BindHandoffPresentation(viewModel);
            DesktopLog.Information(logger, "Initializing the Kora desktop application");
            var window = new MainWindow(
                viewModel,
                Services.GetRequiredService<ILogger<MainWindow>>());
            desktop.MainWindow = window;
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
                Services.GetRequiredService<ILogger<NativeDetailRenderer>>());
            detailWindow.BindHistoryAccess(() => Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>().CanInspect);
            documentationWindow = new DocumentationWindowController(
                Services.GetRequiredService<IUserDocumentationProvider>(),
                viewModel,
                Services.GetRequiredService<ILogger<DocumentationWindowController>>(),
                detailWindow);
            responseWindow = new ResponseWindowController(
                viewModel,
                Services.GetRequiredService<ILogger<ResponseWindowController>>(), detailWindow);
            grantListWindow = new GrantListWindowController(viewModel);
            BindExactGrants(viewModel);
            var nativeQuestions = new NativeQuestionHost(Services.GetRequiredService<Kora.Windows.Storage.WindowsSqliteHostInteractionStore>(),
                Services.GetRequiredService<TimeProvider>(), Services.GetRequiredService<ILogger<NativeQuestionViewModel>>());
            nativeQuestions.BindWorkspace(Services.GetRequiredService<Kora.Application.Hosting.SessionWorkspaceService>());
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
                Services.GetRequiredKeyedService<Kora.Application.Definitions.InstructionSaveWorkflow>(Kora.Core.Artifacts.ArtifactKind.Prompt));
            instructionEditor.Bind();
            promptParameters = new(viewModel, Services.GetRequiredService<Kora.Core.Artifacts.ArtifactCatalogue>(),
                Services.GetRequiredService<Kora.Application.Definitions.PromptRequestConsumer>(),
                Services.GetRequiredService<Kora.Application.Definitions.PromptConversationSubmission>(),
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                    && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
                () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision,
                Services.GetRequiredService<ILogger<PromptParameterController>>());
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
            systemTray = new SystemTrayController(
                viewModel,
                Services.GetRequiredService<ILogger<SystemTrayController>>(),
                () => questionWindow.ReviewVersionAsync(window),
                () => evidenceWindow?.Open(),
                () => skillPackagesWindow.Open(),
                () => sessionsWindow?.Open(),
                () => maintenanceWindow?.Open(),
                microphoneRecovery.Open,
                () => exactGrants?.Open(),
                () => questionWindow.ReturnToCurrentQuestionAsync(window),
                () => attachmentCatalogue?.Open(),
                () => instructionEditor.Open(),
                () => savedDraftCatalogue?.Open(),
                () => newRequestWindow?.Open(),
                () => sessionsWindow?.OpenCompact());
            evidenceWindow = new EvidenceWindowController(viewModel,
                Services.GetRequiredService<Kora.Application.Diagnostics.DurableEvidenceQuery>(),
                Services.GetRequiredService<Kora.Core.Diagnostics.IEvidenceQueryAccess>(),
                Services.GetRequiredService<ILogger<EvidenceViewModel>>());
            evidenceWindow.Bind();
            fileWindow = new LocalFilePreviewWindowController(viewModel, window);
            var sessionFiles = Services.GetRequiredService<SessionFileAttachmentService>();
            Services.GetRequiredService<SessionWorkspaceService>().BindAttachmentLifecycle(sessionFiles);
            viewModel.BindSessionAttachments(sessionFiles);
            attachmentWindow = new SessionAttachmentWindowController(viewModel.ReportHostInteractionFailure, sessionFiles,
                Services.GetRequiredService<SessionWorkspaceService>(),
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(), fileWindow);
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
                Services.GetRequiredService<SessionConversationService>());
            sessionsWindow.Bind();
            newRequestWindow = new(viewModel, Services.GetRequiredService<SessionWorkspaceService>(),
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(),
                () => Services.GetRequiredService<NewRequestComposer>(), sessionsWindow);
            newRequestWindow.Bind();
            sessionsWindow.OpenNewRequest = newRequestWindow.Open;
            savedDraftCatalogue = new(viewModel, Services.GetRequiredService<SessionWorkspaceService>(), sessionsWindow,
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(),
                Services.GetRequiredService<ILogger<SavedDraftCatalogueViewModel>>());
            allWorkWindow = new AllWorkWindowController(viewModel,
                Services.GetRequiredService<SessionWorkspaceService>(),
                Services.GetRequiredService<Kora.Core.Storage.ISessionWorkspaceAccess>(),
                Services.GetRequiredService<ILogger<AllWorkViewModel>>(), sessionsWindow);
            allWorkWindow.Bind();
            sessionsWindow.OpenAllWork = allWorkWindow.Open;
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
            viewModel.BindSessionCommands(Services.GetRequiredService<Kora.Application.Hosting.SessionWorkspaceService>());
            viewModel.BindSessionProviderProfiles(Services.GetRequiredService<Kora.Application.Configuration.SessionProviderProfileConfigurationService>());
            viewModel.BindMemoryCommands(memories);
            clipboardWindow = new ClipboardPreviewWindowController(viewModel);
            viewModel.BindFilePreview(Services.GetRequiredService<Kora.Tools.Files.LocalFilePreview>(), fileWindow,
                Services.GetRequiredService<Kora.Tools.Files.LocalFileSearch>(), fileWindow,
                Services.GetRequiredService<Kora.Tools.Files.LocalFileRefresh>());
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
                && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired);
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
        instructionEditor = null;
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
        skillPackagesWindow?.Dispose();
        skillPackagesWindow = null;
        allWorkWindow?.Dispose();
        allWorkWindow = null;
        sessionsWindow?.Dispose();
        sessionsWindow = null;
        newRequestWindow?.Dispose();
        newRequestWindow = null;
        attachmentCatalogue?.Dispose();
        attachmentCatalogue = null;
        savedDraftCatalogue?.Dispose();
        savedDraftCatalogue = null;
        attachmentWindow?.Dispose();
        attachmentWindow = null;
        clipboardWindow?.Dispose();
        clipboardWindow = null;
        fileWindow?.Dispose();
        fileWindow = null;
        maintenanceWindow?.Dispose();
        maintenanceWindow = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
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