using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using Kora.Application.Documentation;
using Kora.Application.ViewModels;
using Kora.Application;
using Kora.Core.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App : Avalonia.Application
{
    private SystemTrayController? systemTray;
    private SettingsWindowController? settingsWindow;
    private DocumentationWindowController? documentationWindow;
    private DetailWindowController? detailWindow;
    private ResponseWindowController? responseWindow;
    private GrantListWindowController? grantListWindow;
    private MainViewModel? viewModel;
    private ILogger<App>? logger;

    internal static ServiceProvider Services { get; set; } = null!;
    internal DetailWindowController? Details => detailWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            viewModel = Services.GetRequiredService<MainViewModel>();
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.PrivacyClosureRequested += OnPrivacyClosureRequested;
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
            MarkdownDocumentRenderer.Renderer = new NativeDetailRenderer(
                Services.GetRequiredService<ILogger<NativeDetailRenderer>>());
            detailWindow = new DetailWindowController(
                Services.GetRequiredService<IUserDocumentationProvider>(), viewModel,
                Services.GetRequiredService<ILogger<DetailWindowController>>(),
                Services.GetRequiredService<ILogger<NativeDetailRenderer>>());
            documentationWindow = new DocumentationWindowController(
                Services.GetRequiredService<IUserDocumentationProvider>(),
                viewModel,
                Services.GetRequiredService<ILogger<DocumentationWindowController>>(),
                detailWindow);
            responseWindow = new ResponseWindowController(
                viewModel,
                Services.GetRequiredService<ILogger<ResponseWindowController>>());
            grantListWindow = new GrantListWindowController(viewModel);
            systemTray = new SystemTrayController(
                viewModel,
                Services.GetRequiredService<ILogger<SystemTrayController>>());
            var host = viewModel;
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
    {
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
        systemTray = null;
        settingsWindow?.Dispose();
        settingsWindow = null;
        documentationWindow?.Dispose();
        documentationWindow = null;
        detailWindow?.Dispose();
        detailWindow = null;
        responseWindow?.Dispose();
        responseWindow = null;
        grantListWindow?.Dispose();
        grantListWindow = null;
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