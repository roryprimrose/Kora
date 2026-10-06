using System.Runtime.ExceptionServices;

using Avalonia;

using Kora.Application;
using Kora.Application.Auditing;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Dependencies;
using Kora.Application.Documentation;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Coordination;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.Communication;
using Kora.Windows.Coordination;
using Kora.Windows.Dependencies;
using Kora.Windows.Identity;
using Kora.Windows.Session;

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
                        var fileLogger = CreateFileLogger(paths);
                        Log.Logger = fileLogger;
                        var services = new ServiceCollection();
                        ConfigureServices(services, paths, fileLogger, ownershipBridge, coordinator);
                        using (var startup = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                            HostActivityLayer.Desktop, HostOperation.Startup))
                        {
                            provider = services.BuildServiceProvider();
                            App.Services = provider;
                            var startupLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Kora.Desktop");
                            DesktopLog.Information(startupLogger, "Starting Kora desktop host");
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
                    }
                    finally
                    {
                        try
                        {
                            if (provider is not null)
                            {
                                // Avalonia leaves its synchronization context installed after the UI loop exits.
                                Task.Run(() => provider.DisposeAsync().AsTask()).GetAwaiter().GetResult();
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

    private static void ConfigureServices(
        IServiceCollection services,
        ApplicationDataPaths paths,
        Serilog.Core.Logger fileLogger,
        DesktopInstanceOwnershipBridge ownershipBridge,
        WindowsInstanceCoordinator coordinator)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
            var fileSink = new FileEvidenceSink(fileLogger);
            builder.AddProvider(new EvidenceLoggerProvider(
                [fileSink, new UnavailableEvidenceSink()], fileSink));
        });
        services.AddSingleton(ownershipBridge);
        services.AddSingleton<IInstanceHostCallbacks>(ownershipBridge);
        services.AddSingleton<IInstanceLifecycleController>(coordinator);
        services.AddSingleton<BuiltInCommandCatalog>();
        services.AddSingleton<BuiltInCommandRouter>();
        services.AddSingleton<IApplicationDataPaths>(paths);
        services.AddSingleton<IHostTaskStore, UnavailableHostTaskStore>();
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
        services.AddSingleton<MainViewModel>();
    }

    private static Serilog.Core.Logger CreateFileLogger(ApplicationDataPaths paths)
    {
        var logDirectory = Path.Combine(paths.LocalRoot, "Logs");
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                new JsonFormatter(renderMessage: true),
                Path.Combine(logDirectory, "kora-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                retainedFileTimeLimit: TimeSpan.FromDays(30),
                rollOnFileSizeLimit: false,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();
    }
}