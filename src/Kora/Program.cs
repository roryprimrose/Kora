using Avalonia;

using Kora.Application;
using Kora.Application.Auditing;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Documentation;
using Kora.Application.ViewModels;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.Communication;
using Kora.Windows.Dependencies;
using Kora.Windows.Session;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Formatting.Json;

namespace Kora;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var paths = new ApplicationDataPaths();
        var fileLogger = CreateFileLogger(paths);
        Log.Logger = fileLogger;

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services, paths, fileLogger);
            App.Services = services.BuildServiceProvider();
            Log.Information("Starting Kora desktop host.");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Kora terminated unexpectedly.");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureServices(
        IServiceCollection services,
        ApplicationDataPaths paths,
        Serilog.Core.Logger fileLogger)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddSerilog(fileLogger, dispose: true);
        });
        services.AddSingleton<BuiltInCommandCatalog>();
        services.AddSingleton<BuiltInCommandRouter>();
        services.AddSingleton<IApplicationDataPaths>(paths);
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
        services.AddSingleton<IModelApprovalPreferences, LocalModelApprovalPreferences>();
        services.AddSingleton<IAssistantNamePreferences, LocalAssistantNamePreferences>();
        services.AddSingleton<IAppearancePreferences, LocalAppearancePreferences>();
        services.AddSingleton<ITextToSpeechPreferences, LocalTextToSpeechPreferences>();
        services.AddSingleton<IOptionalSpeechOfferPreferences, LocalOptionalSpeechOfferPreferences>();
        services.AddSingleton<IAudioDevicePreferences, LocalAudioDevicePreferences>();
        services.AddSingleton<IResponseOutputPreferences, LocalResponseOutputPreferences>();
        services.AddSingleton<ICallAwarePreferences, LocalCallAwarePreferences>();
        services.AddSingleton<ICallStateService, UnavailableCallStateService>();
        services.AddSingleton<IMicrophoneAccessService, WindowsMicrophoneAccessService>();
        services.AddSingleton<IVoiceRecognitionService, WindowsVoiceRecognitionService>();
        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15),
        });
        services.AddSingleton<KokoroTextToSpeechProvider>();
        services.AddSingleton<ITextToSpeechService, WindowsTextToSpeechService>();
        services.AddSingleton<ISessionController, WindowsSessionController>();
        services.AddSingleton<IApplicationProcessController, DesktopApplicationProcessController>();
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