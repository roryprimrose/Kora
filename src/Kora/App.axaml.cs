using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using Kora.Application.Documentation;
using Kora.Application.ViewModels;
using Kora.Core.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App : Avalonia.Application
{
    private SystemTrayController? systemTray;
    private SettingsWindowController? settingsWindow;
    private DocumentationWindowController? documentationWindow;
    private ResponseWindowController? responseWindow;
    private GrantListWindowController? grantListWindow;
    private MainViewModel? viewModel;
    private ILogger<App>? logger;

    internal static ServiceProvider Services { get; set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            viewModel = Services.GetRequiredService<MainViewModel>();
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyThemeMode(viewModel.ThemeMode);
            logger = Services.GetRequiredService<ILogger<App>>();
            DesktopLog.Information(logger, "Initializing the Kora desktop application");
            var window = new MainWindow(
                viewModel,
                Services.GetRequiredService<ILogger<MainWindow>>());
            desktop.MainWindow = window;
            settingsWindow = new SettingsWindowController(
                viewModel,
                Services.GetRequiredService<ILogger<SettingsWindowController>>());
            documentationWindow = new DocumentationWindowController(
                Services.GetRequiredService<IUserDocumentationProvider>(),
                viewModel,
                Services.GetRequiredService<ILogger<DocumentationWindowController>>());
            responseWindow = new ResponseWindowController(
                viewModel,
                Services.GetRequiredService<ILogger<ResponseWindowController>>());
            grantListWindow = new GrantListWindowController(viewModel);
            systemTray = new SystemTrayController(
                viewModel,
                Services.GetRequiredService<ILogger<SystemTrayController>>());
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        if (logger is not null)
        {
            DesktopLog.Information(logger, "Shutting down the Kora desktop application");
        }
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel = null;
        }
        systemTray?.Dispose();
        systemTray = null;
        settingsWindow?.Dispose();
        settingsWindow = null;
        documentationWindow?.Dispose();
        documentationWindow = null;
        responseWindow?.Dispose();
        responseWindow = null;
        grantListWindow?.Dispose();
        grantListWindow = null;
        Services.DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
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