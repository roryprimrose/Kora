using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Kora.Application.ViewModels;
using Kora.Core.Voice;

using Microsoft.Extensions.DependencyInjection;

namespace Kora.Desktop;

public sealed partial class App : Avalonia.Application
{
    private SystemTrayController? systemTray;

    internal static ServiceProvider Services { get; set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = Services.GetRequiredService<MainViewModel>();
            var window = new MainWindow(viewModel);
            desktop.MainWindow = window;
            systemTray = new SystemTrayController(viewModel);
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        systemTray?.Dispose();
        systemTray = null;
        Services.GetRequiredService<IVoiceRecognitionService>()
            .DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
        Services.Dispose();
    }
}