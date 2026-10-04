using Kora.Application.ViewModels;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class SettingsWindowController : IDisposable
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<SettingsWindowController> logger;
    private SettingsWindow? window;
    private bool disposed;

    public SettingsWindowController(
        MainViewModel viewModel,
        ILogger<SettingsWindowController> logger)
    {
        this.viewModel = viewModel;
        this.logger = logger;
        viewModel.SettingsRequested += OnSettingsRequested;
        viewModel.ReadinessRequested += OnReadinessRequested;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DesktopLog.Debug(logger, "Disposing the settings window controller");
        viewModel.SettingsRequested -= OnSettingsRequested;
        viewModel.ReadinessRequested -= OnReadinessRequested;
        if (window is not null)
        {
            window.Closed -= OnWindowClosed;
            window.Close();
            window = null;
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs eventArgs)
    {
        DesktopLog.Information(logger, "Settings window was requested");
        window ??= CreateWindow();
        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    private void OnReadinessRequested(object? sender, EventArgs eventArgs)
    {
        OnSettingsRequested(sender, eventArgs);
        window!.ShowReadiness();
    }

    private SettingsWindow CreateWindow()
    {
        var settingsWindow = new SettingsWindow(viewModel);
        settingsWindow.Closed += OnWindowClosed;
        return settingsWindow;
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (ReferenceEquals(window, sender))
        {
            DesktopLog.Debug(logger, "Settings window closed");
            window = null;
        }
    }
}
