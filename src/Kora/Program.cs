using Avalonia;

using Kora.Application;
using Kora.Application.ViewModels;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.Dependencies;
using Kora.Windows.Session;

using Microsoft.Extensions.DependencyInjection;

namespace Kora.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        App.Services = services.BuildServiceProvider();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<BuiltInCommandCatalog>();
        services.AddSingleton<BuiltInCommandRouter>();
        services.AddSingleton<IApplicationDataPaths, ApplicationDataPaths>();
        services.AddSingleton<IDependencyProbe, StorageDependencyProbe>();
        services.AddSingleton<IDependencyProbe, WindowsVoiceDependencyProbe>();
        services.AddSingleton<DependencyBootstrapper>();
        services.AddSingleton<IVoiceRecognitionService, WindowsVoiceRecognitionService>();
        services.AddSingleton<ISessionController, WindowsSessionController>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IApplicationInfo, AssemblyApplicationInfo>();
        services.AddSingleton<MainViewModel>();
    }
}