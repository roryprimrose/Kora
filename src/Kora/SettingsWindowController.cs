using Avalonia;

using Kora.Application.ViewModels;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class SettingsWindowController : IDisposable
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<SettingsWindowController> logger;
    private readonly Action? chooseMicrophone;
    private SettingsWindow? window;
    private bool disposed;
    private int nativeVisible;
    private long nativeVisibilityRevision;

    public SettingsWindowController(
        MainViewModel viewModel,
        ILogger<SettingsWindowController> logger,
        Action? chooseMicrophone = null)
    {
        this.viewModel = viewModel;
        this.logger = logger;
        this.chooseMicrophone = chooseMicrophone;
        viewModel.SettingsRequested += OnSettingsRequested;
        viewModel.ReadinessRequested += OnReadinessRequested;
        viewModel.VoiceRecoveryRequested += OnVoiceRecoveryRequested;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        BindNativeLifetime(false);
        DesktopLog.Debug(logger, "Disposing the settings window controller");
        viewModel.SettingsRequested -= OnSettingsRequested;
        viewModel.ReadinessRequested -= OnReadinessRequested;
        viewModel.VoiceRecoveryRequested -= OnVoiceRecoveryRequested;
        if (window is not null)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
            window.Closed -= OnWindowClosed;
            window.Close();
            window = null;
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs eventArgs)
    {
        if (!viewModel.CanRevealPrivatePresentation)
        {
            return;
        }
        DesktopLog.Information(logger, "Settings window was requested");
        window ??= CreateWindow();
        if (!window.IsVisible)
        {
            window.Show();
            BindNativeLifetime(window.IsVisible);
        }
        window.Activate();
    }

    private void OnReadinessRequested(object? sender, EventArgs eventArgs)
    {
        OnSettingsRequested(sender, eventArgs);
        window?.ShowReadiness();
    }

    private void OnVoiceRecoveryRequested(object? sender, EventArgs eventArgs)
    {
        OnSettingsRequested(sender, eventArgs);
        window?.ShowVoiceRecovery();
    }

    private SettingsWindow CreateWindow()
    {
        var settingsWindow = new SettingsWindow(viewModel, chooseMicrophone);
        settingsWindow.PropertyChanged += OnWindowPropertyChanged;
        settingsWindow.Closed += OnWindowClosed;
        return settingsWindow;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (ReferenceEquals(window, sender) && eventArgs.Property == Visual.IsVisibleProperty)
        {
            BindNativeLifetime(window!.IsVisible);
        }
    }

    private void BindNativeLifetime(bool visible)
    {
        // Storage-thread admission must never read Avalonia properties, and older bindings must not revive on reopen.
        var revision = Interlocked.Increment(ref nativeVisibilityRevision);
        Volatile.Write(ref nativeVisible, visible && !disposed ? 1 : 0);
        viewModel.BindDiagnosticRetentionNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindManualCallNativeLifetime(visible && !disposed);
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (ReferenceEquals(window, sender))
        {
            DesktopLog.Debug(logger, "Settings window closed");
            window!.PropertyChanged -= OnWindowPropertyChanged;
            window.Closed -= OnWindowClosed;
            window = null;
            BindNativeLifetime(false);
        }
    }
}
