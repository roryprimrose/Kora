using System.Runtime.ExceptionServices;

using Avalonia;

using Kora.Application;
using Kora.Application.Auditing;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Dependencies;
using Kora.Application.Documentation;
using Kora.Application.Hosting;
using Kora.Application.Maintenance;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Coordination;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core.Storage;
using Kora.Core.Platform;
using Kora.Core.Skills;
using Kora.Application.Skills;
using Kora.Windows.Skills;
using Kora.Core.Voice;
using Kora.Definitions.Artifacts;
using Kora.Windows.Audio;
using Kora.Windows.Artifacts;
using Kora.Windows.Communication;
using Kora.Windows.Coordination;
using Kora.Windows.Dependencies;
using Kora.Windows.Identity;
using Kora.Windows.Maintenance;
using Kora.Windows.Session;
using Kora.Windows.Storage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Formatting.Json;

namespace Kora;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        HostActivity.ConfigureW3C();
        var ownershipBridge = new DesktopInstanceOwnershipBridge();
        WindowsInstanceCoordinator coordinator;
        try
        {
            coordinator = new WindowsInstanceCoordinator(ownershipBridge);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WindowsInstanceCoordinator.ReportStartupFailure(
                $"Kora could not prove its Windows ownership/launch identity: {exception.Message}");
            return 1;
        }

        ExceptionDispatchInfo? failure = null;
        var exitCode = 0;
        try
        {
            using (coordinator)
            {
                var disposition = coordinator.Enter();
                if (disposition != InstanceStartupDisposition.Owner)
                {
                    if (disposition == InstanceStartupDisposition.Denied)
                    {
                        coordinator.ShowFailure();
                        exitCode = 1;
                    }
                }
                else
                {
                    var safelyDisposed = false;
                    ServiceProvider? provider = null;
                    try
                    {
                        var paths = new ApplicationDataPaths(coordinator.BuildIdentity.IsDebug);
                        var fileHealth = new FileEvidenceHealth();
                        var fileLogger = CreateFileLogger(paths, fileHealth);
                        Log.Logger = fileLogger;
                        var services = new ServiceCollection();
                        ConfigureServices(services, paths, fileLogger, fileHealth, ownershipBridge, coordinator);
                        using (var startup = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                            HostActivityLayer.Desktop, HostOperation.Startup))
                        {
                            provider = services.BuildServiceProvider();
                            App.Services = provider;
                            var startupLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Kora.Desktop");
                            var auditConfiguration = provider.GetRequiredService<AuditRetentionConfigurationService>();
                            try { auditConfiguration.Observe(); }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                            {
                                auditConfiguration.HoldUnavailable();
                                throw new AuditRetentionUnavailableException(exception);
                            }
                            var diagnosticConfiguration = provider.GetRequiredService<DiagnosticRetentionConfigurationService>();
                            try { diagnosticConfiguration.Observe(); }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                            {
                                diagnosticConfiguration.HoldUnavailable();
                                DesktopLog.Error(startupLogger, exception, "Reading SQLite diagnostic retention before startup evidence");
                            }
                            DesktopLog.Information(startupLogger, "Starting Kora desktop host");
                            Task.Run(() => provider.GetRequiredService<DurableHostRecovery>()
                                .RecoverAsync(CancellationToken.None)).GetAwaiter().GetResult();
                            Task.Run(() => provider.GetRequiredService<WindowsSqliteDiagnosticRetention>()
                                .RunAsync(CancellationToken.None).AsTask()).GetAwaiter().GetResult();
                            startup.Complete(HostOperationOutcome.Completed);
                        }
                        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                        safelyDisposed = true;
                    }
                    catch (Exception exception)
                    {
                        failure = ExceptionDispatchInfo.Capture(exception);
                        Log.Fatal("Kora terminated unexpectedly. BootstrapDiagnostic: {BootstrapDiagnostic}; ExceptionType: {ExceptionType}.",
                            true, exception.GetType().FullName);
                        WindowsInstanceCoordinator.ReportStartupFailure(
                            exception is AuditRetentionUnavailableException
                                ? "Audit retention is unconfirmed; required new audit and authority writes are held. Inspect device-local audit preference and required audit/intent receipts before explicit repair and restart; no fallback policy or automatic replay."
                                : "Kora could not complete durable host startup or execution. No automatic task replay is permitted.");
                    }
                    finally
                    {
                        try
                        {
                            if (provider is not null)
                            {
                                // Avalonia leaves its synchronization context installed after the UI loop exits.
                                Task.Run(() => DisposeServicesAsync(provider)).GetAwaiter().GetResult();
                            }
                        }
                        catch (Exception exception)
                        {
                            safelyDisposed = false;
                            failure ??= ExceptionDispatchInfo.Capture(exception);
                            Log.Error("Kora service shutdown failed; clean ownership release is not verified. BootstrapDiagnostic: {BootstrapDiagnostic}; ExceptionType: {ExceptionType}.",
                                true, exception.GetType().FullName);
                        }

                        ownershipBridge.UnbindCallbacks();
                        provider = null;
                        App.Services = null!;

                        try
                        {
                            Log.CloseAndFlush();
                        }
                        catch (Exception exception)
                        {
                            safelyDisposed = false;
                            failure ??= ExceptionDispatchInfo.Capture(exception);
                        }

                        try
                        {
                            coordinator.CompleteHostExit(safelyDisposed);
                        }
                        catch (Exception exception)
                        {
                            failure ??= ExceptionDispatchInfo.Capture(exception);
                            WindowsInstanceCoordinator.ReportStartupFailure(
                                $"Kora could not complete its safe ownership transition: {exception.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            // Coordinator disposal must not replace an earlier host/teardown failure.
            failure ??= ExceptionDispatchInfo.Capture(exception);
        }

        failure?.Throw();
        return exitCode;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static async Task DisposeServicesAsync(ServiceProvider provider)
    {
        using var shutdown = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Recovery);
        try
        {
            await provider.DisposeAsync();
            shutdown.Complete(HostOperationOutcome.Completed);
        }
        catch
        {
            shutdown.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private static void ConfigureServices(
        IServiceCollection services,
        ApplicationDataPaths paths,
        Serilog.Core.Logger fileLogger,
        FileEvidenceHealth fileHealth,
        DesktopInstanceOwnershipBridge ownershipBridge,
        WindowsInstanceCoordinator coordinator)
    {
        var diagnosticPolicy = new DiagnosticRetentionPolicy();
        var auditPolicy = new AuditRetentionPolicy();
        var evidence = new WindowsSqliteEvidenceSink(paths, diagnosticPolicy: diagnosticPolicy, auditPolicy: auditPolicy);
        evidence.Initialize();
        var tasks = new WindowsSqliteHostTaskStore(paths);
        Task.Run(() => tasks.InitializeAsync(CancellationToken.None).AsTask()).GetAwaiter().GetResult();
        var interactions = new WindowsSqliteHostInteractionStore(paths, tasks, auditPolicy: auditPolicy);
        Task.Run(() => interactions.InitializeAsync(CancellationToken.None).AsTask()).GetAwaiter().GetResult();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
            var fileSink = new FileEvidenceSink(fileLogger, fileHealth);
            builder.AddProvider(new EvidenceLoggerProvider(
                [fileSink, evidence], fileSink));
        });
        services.AddSingleton(ownershipBridge);
        services.AddSingleton<IInstanceHostCallbacks>(ownershipBridge);
        services.AddSingleton<IInstanceLifecycleController>(coordinator);
        services.AddSingleton<BuiltInCommandCatalog>();
        services.AddSingleton<BuiltInCommandRouter>();
        services.AddSingleton(_ =>
        {
            var embedded = EmbeddedArtifactCatalogue.Load();
            var disk = new WindowsDiskArtifactDiscovery(paths).Load();
            return new ArtifactCatalogue([.. embedded.Artifacts, .. disk]);
        });
        services.AddSingleton<ArtifactCommandRouter>();
        services.AddSingleton<IApplicationDataPaths>(paths);
        services.AddSingleton(evidence);
        services.AddSingleton(diagnosticPolicy);
        services.AddSingleton(auditPolicy);
        services.AddSingleton<WindowsSqliteDiagnosticRetention>();
        services.AddSingleton<IHostTaskStore>(tasks);
        services.AddSingleton(interactions);
        services.AddSingleton<IHostInteractionStore>(interactions);
        services.AddSingleton<ISessionWorkspaceStore>(interactions);
        services.AddSingleton<ISharedSkillSessionStore>(interactions);
        services.AddSingleton<ISharedSkillSourceReader, WindowsProfileSkillReader>();
        services.AddSingleton<LocalSharedSkillPreferences>();
        services.AddSingleton<SharedSkillAdmission>();
        services.AddSingleton<SharedSkillDiscoveryService>();
        services.AddSingleton<IAudioControlSessionStore>(interactions);
        services.AddSingleton<IDiagnosticRetentionSessionStore>(interactions);
        services.AddSingleton<DiagnosticRetentionAdmission>();
        services.AddSingleton<IAuditRetentionSessionStore>(interactions);
        services.AddSingleton<AuditRetentionAdmission>();
        services.AddSingleton<IManualCallControlStore>(interactions);
        services.AddSingleton<Kora.Application.Communication.ManualCallControl>();
        services.AddSingleton<IMaintenanceControlSessionStore>(interactions);
        services.AddSingleton<MaintenanceCommands>();
        services.AddSingleton<Kora.Application.Voice.AudioControlAdmission>();
        services.AddSingleton<Kora.Application.Voice.BoundedAudioOutputCatalog>();
        services.AddSingleton<OutputDeviceConfigurationService>();
        services.AddSingleton<ResponseModeConfigurationService>();
        services.AddSingleton<InCallFeedbackConfigurationService>();
        services.AddSingleton<IInCallFeedbackPreferences>(provider =>
            new LocalInCallFeedbackPreferences(provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<SpeechTextConfigurationService>();
        services.AddSingleton<ISessionWorkspaceAccess, DesktopSessionWorkspaceAccess>();
        services.AddSingleton<SessionWorkspaceService>();
        services.AddSingleton(TimeProvider.System);
        services.AddKeyedSingleton("release-metadata", (_, _) => new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        }) { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IReleaseMetadataClient>(provider => new GitHubReleaseMetadataClient(
            provider.GetRequiredKeyedService<HttpClient>("release-metadata"),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<GitHubReleaseMetadataClient>>()));
        services.AddSingleton<ICanonicalReleasePageOpener, WindowsReleasePageOpener>();
        services.AddSingleton(provider => new MaintenanceViewModel(
            provider.GetRequiredService<IReleaseMetadataClient>(), provider.GetRequiredService<IApplicationInfo>(),
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => ReleaseArchitecture.X64,
                System.Runtime.InteropServices.Architecture.X86 => ReleaseArchitecture.X86,
                _ => ReleaseArchitecture.Unsupported,
            },
            provider.GetRequiredService<ICanonicalReleasePageOpener>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IUiDispatcher>(), provider.GetRequiredService<ISecurityAuditLog>(),
            provider.GetRequiredService<ILogger<MaintenanceViewModel>>()));
        services.AddSingleton<Kora.Application.Interaction.HostQuestionService>();
        services.AddSingleton<Kora.Application.Interaction.HostAuthorizationService>();
        services.AddSingleton<HostTaskCoordinator>();
        services.AddSingleton<DurableVersionQuery>();
        services.AddSingleton<IEvidenceReader>(new WindowsEvidenceReader(
            new WindowsSqliteEvidenceReader(evidence), new WindowsDailyEvidenceReader(paths), interactions));
        services.AddSingleton<IEvidenceQueryAccess, DesktopEvidenceAccess>();
        services.AddSingleton<DurableEvidenceQuery>();
        services.AddSingleton<DurableHostRecovery>();
        services.AddSingleton<IApplicationLogReader, LocalApplicationLogReader>();
        services.AddSingleton<IUserDocumentationProvider, EmbeddedUserDocumentationProvider>();
        services.AddSingleton<ISecurityAuditLog, LoggerSecurityAuditLog>();
        services.AddSingleton<IDependencyProbe, StorageDependencyProbe>();
        services.AddSingleton<IDependencyProbe, SqliteDependencyProbe>();
        services.AddSingleton<WindowsPowerShellSetupService>();
        services.AddSingleton<IPowerShellSetup>(provider =>
            provider.GetRequiredService<WindowsPowerShellSetupService>());
        services.AddSingleton<IDependencyProbe>(provider =>
            provider.GetRequiredService<WindowsPowerShellSetupService>());
        services.AddKeyedSingleton(
            "ollama-probe",
            (_, _) => new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
            }));
        services.AddSingleton<IDependencyProbe>(provider =>
            new LocalInferenceDependencyProbe(
                provider.GetRequiredKeyedService<HttpClient>("ollama-probe")));
        services.AddSingleton<IDependencyProbe, WindowsVoiceDependencyProbe>();
        services.AddSingleton<IDependencyProbe, WindowsTextToSpeechDependencyProbe>();
        services.AddSingleton<DependencyBootstrapper>();
        services.AddSingleton<Kora.Core.Tools.ICapabilityHostAccess, DesktopCapabilityHostAccess>();
        services.AddSingleton<Kora.Tools.Capabilities.CapabilitiesList>();
        services.AddSingleton<Kora.Tools.Capabilities.CapabilitiesGet>();
        services.AddSingleton<Kora.Tools.Application.ApplicationGetVersion>();
        services.AddSingleton<Kora.Tools.Readiness.ReadinessGet>();
        services.AddSingleton<Kora.Tools.Runtime.RecordedRuntimeObservation>();
        services.AddSingleton<Kora.Tools.Runtime.RuntimeList>();
        services.AddSingleton<Kora.Tools.Runtime.RuntimeGetStatus>();
        services.AddSingleton<Kora.Application.Tools.ReadOnlyCapabilityRegistry>();
        services.AddSingleton<ILocalModelSetup, WindowsOllamaSetupService>();
        services.AddSingleton<DependencySetupWorkflow>();
        services.AddKeyedSingleton(
            "ollama-reasoner",
            (_, _) => new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
            })
            {
                Timeout = TimeSpan.FromMinutes(3),
            });
        services.AddSingleton<ILocalModelReasoner>(provider =>
            new WindowsOllamaReasoner(
                provider.GetRequiredKeyedService<HttpClient>("ollama-reasoner"),
                provider.GetRequiredService<BuiltInCommandCatalog>()));
        services.AddSingleton<IPreferenceStore, LocalPreferenceStore>();
        services.AddSingleton<IPlaybackVolumePreferences>(provider =>
            new LocalPlaybackVolumePreferences(provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<PlaybackVolumeConfigurationService>();
        services.AddSingleton<IWindowsSpeechRatePreferences>(provider =>
            new LocalWindowsSpeechRatePreferences(provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<WindowsSpeechRateConfigurationService>();
        services.AddSingleton<IDiagnosticRetentionPreferences>(provider =>
            new LocalDiagnosticRetentionPreferences(provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<DiagnosticRetentionConfigurationService>();
        services.AddSingleton<IAuditRetentionPreferences>(provider =>
            new LocalAuditRetentionPreferences(provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<AuditRetentionConfigurationService>();
        services.AddSingleton<IModelApprovalPreferences>(provider =>
            new LocalModelApprovalPreferences(
                provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<IModelExecutionPreferences>(provider =>
            new LocalModelExecutionPreferences(
                provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<IAssistantNamePreferences>(provider =>
            new LocalAssistantNamePreferences(
                provider.GetRequiredService<IPreferenceStore>(),
                provider.GetRequiredService<ILogger<LocalAssistantNamePreferences>>()));
        services.AddSingleton<IAppearancePreferences>(provider =>
            new LocalAppearancePreferences(
                provider.GetRequiredService<IPreferenceStore>(),
                provider.GetRequiredService<ILogger<LocalAppearancePreferences>>()));
        services.AddSingleton<AppearanceConfigurationService>();
        services.AddSingleton<ISpeechCatalog>(provider => provider.GetRequiredService<ITextToSpeechService>());
        services.AddSingleton<IAudioOutputDeviceCatalog>(provider => provider.GetRequiredService<ITextToSpeechService>());
        services.AddSingleton<ISpeechPlaybackService>(provider => provider.GetRequiredService<ITextToSpeechService>());
        services.AddSingleton<SpeechConfigurationService>();
        services.AddSingleton<AssistantNameConfigurationService>();
        services.AddSingleton<ITextToSpeechPreferences>(provider =>
            new LocalTextToSpeechPreferences(
                provider.GetRequiredService<IPreferenceStore>(),
                provider.GetRequiredService<ILogger<LocalTextToSpeechPreferences>>()));
        services.AddSingleton<IOptionalSpeechOfferPreferences>(provider =>
            new LocalOptionalSpeechOfferPreferences(
                provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<IAudioDevicePreferences>(provider =>
            new LocalAudioDevicePreferences(
                provider.GetRequiredService<IPreferenceStore>(),
                provider.GetRequiredService<ILogger<LocalAudioDevicePreferences>>()));
        services.AddSingleton<IResponseOutputPreferences>(provider =>
            new LocalResponseOutputPreferences(
                provider.GetRequiredService<IPreferenceStore>(),
                provider.GetRequiredService<ILogger<LocalResponseOutputPreferences>>()));
        services.AddSingleton<ISpeechTextPreferences>(provider =>
            new LocalSpeechTextPreferences(provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<ICallAwarePreferences>(provider =>
            new LocalCallAwarePreferences(
                provider.GetRequiredService<IPreferenceStore>(),
                provider.GetRequiredService<ILogger<LocalCallAwarePreferences>>()));
        services.AddSingleton<IVoiceConsentPreferences>(provider =>
            new LocalVoiceConsentPreferences(
                provider.GetRequiredService<IPreferenceStore>(),
                provider.GetRequiredService<ILogger<LocalVoiceConsentPreferences>>()));
        services.AddSingleton<ICallStateService, UnavailableCallStateService>();
        services.AddSingleton<IMicrophoneAccessService, WindowsMicrophoneAccessService>();
        services.AddSingleton<IWindowsPrivacyObservationService, WindowsPrivacyObservationService>();
        services.AddSingleton<WindowsVoiceRecognitionService>();
        services.AddSingleton<IVoiceRecognitionService>(provider =>
            provider.GetRequiredService<WindowsVoiceRecognitionService>());
        services.AddSingleton<IActivatedVoiceRecognitionService>(provider =>
            provider.GetRequiredService<WindowsVoiceRecognitionService>());
        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15),
        });
        services.AddSingleton<KokoroTextToSpeechProvider>();
        services.AddSingleton<ITextToSpeechService, WindowsTextToSpeechService>();
        services.AddSingleton<ISessionController, WindowsSessionController>();
        services.AddSingleton<IApplicationProcessController, DesktopApplicationProcessController>();
        services.AddSingleton<ICurrentUserNameProvider, WindowsCurrentUserNameProvider>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IApplicationInfo, AssemblyApplicationInfo>();
        services.AddSingleton<Kora.Core.Context.IPlainTextClipboardReader, Kora.Windows.Context.WindowsPlainTextClipboardReader>();
        services.AddSingleton<Kora.Tools.Clipboard.ClipboardSnapshotBroker>();
        services.AddSingleton<Kora.Tools.Clipboard.ClipboardRead>();
        services.AddSingleton<Kora.Tools.Clipboard.ClipboardReuse>();
        services.AddSingleton<Kora.Tools.Clipboard.ClipboardRevoke>();
        services.AddSingleton<Kora.Core.Context.ILocalFileInspector, Kora.Windows.Context.WindowsLocalFileInspector>();
        services.AddSingleton<Kora.Tools.Files.LocalFilePreview>();
        services.AddSingleton<MainViewModel>();
    }

    private static Serilog.Core.Logger CreateFileLogger(ApplicationDataPaths paths, FileEvidenceHealth health)
    {
        var logDirectory = Path.Combine(paths.LocalRoot, DailyLogFilePolicy.DirectoryName);
        Directory.CreateDirectory(logDirectory);

        // Serilog normally self-reports file errors instead of throwing. A sticky admission
        // latch makes those failures observable without recursively invoking the failed sink.
        Serilog.Debugging.SelfLog.Enable(_ => health.Fail());
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                new JsonFormatter(renderMessage: true),
                Path.Combine(logDirectory, DailyLogFilePolicy.RollingName),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                retainedFileTimeLimit: TimeSpan.FromDays(30),
                rollOnFileSizeLimit: false,
                shared: false)
            .CreateLogger();
        health.RequireHealthy();
        return logger;
    }
}