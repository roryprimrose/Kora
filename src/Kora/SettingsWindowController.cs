using Avalonia;

using Kora.Application.ViewModels;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Application.Presentation;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class SettingsWindowController : IDisposable, ISettingsSectionNavigation
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<SettingsWindowController> logger;
    private readonly Action? chooseMicrophone;
    private readonly SpeechCaptionWindowController? captionDisplay;
    private SettingsWindow? window;
    private bool disposed;
    private int nativeVisible;
    private long nativeVisibilityRevision;

    public SettingsWindowController(
        MainViewModel viewModel,
        ILogger<SettingsWindowController> logger,
        Action? chooseMicrophone = null,
        SpeechCaptionWindowController? captionDisplay = null)
    {
        this.viewModel = viewModel;
        this.logger = logger;
        this.chooseMicrophone = chooseMicrophone;
        this.captionDisplay = captionDisplay;
        viewModel.SettingsRequested += OnSettingsRequested;
        viewModel.ReadinessRequested += OnReadinessRequested;
        viewModel.VoiceRecoveryRequested += OnVoiceRecoveryRequested;
        viewModel.BindSettingsSectionNavigation(this);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.BindSettingsSectionNavigation(null);
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
        if (disposed || !viewModel.CanRevealPrivatePresentation)
        {
            return;
        }
        OpenWindow(() => !disposed && viewModel.CanRevealPrivatePresentation);
    }

    public Func<bool> CaptureLifetime()
    {
        var revision = Volatile.Read(ref nativeVisibilityRevision);
        return () => !disposed && Volatile.Read(ref nativeVisibilityRevision) == revision;
    }

    public void Navigate(SettingsSection section, Func<bool> admitted)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        if (!Enum.IsDefined(section)) { throw new ArgumentOutOfRangeException(nameof(section)); }
        var target = OpenWindow(admitted);
        if (!admitted())
        {
            target.Hide();
            throw new InvalidOperationException("Settings navigation was revoked before tab selection.");
        }
        target.ShowSection(section);
        if (!admitted())
        {
            target.Hide();
            throw new InvalidOperationException("Settings navigation was revoked during tab selection.");
        }
    }

    private SettingsWindow OpenWindow(Func<bool> admitted)
    {
        if (disposed || !viewModel.CanRevealPrivatePresentation || !admitted())
        { throw new InvalidOperationException("The native Settings host is unavailable."); }
        DesktopLog.Information(logger, "Settings window was requested");
        var target = window ??= CreateWindow();
        if (!admitted()) { throw new InvalidOperationException("Settings navigation was revoked before showing."); }
        if (!target.IsVisible)
        {
            target.Show();
            BindNativeLifetime(target.IsVisible);
        }
        if (disposed || !ReferenceEquals(window, target) || !target.IsVisible || !admitted())
        {
            target.Hide();
            throw new InvalidOperationException("The native Settings view is not visible or admission was revoked.");
        }
        target.Activate();
        return target;
    }

    private void OnReadinessRequested(object? sender, EventArgs eventArgs)
    {
        if (!disposed && viewModel.CanRevealPrivatePresentation)
        { OpenWindow(() => !disposed && viewModel.CanRevealPrivatePresentation).ShowReadiness(); }
    }

    private void OnVoiceRecoveryRequested(object? sender, EventArgs eventArgs)
    {
        if (!disposed && viewModel.CanRevealPrivatePresentation)
        { OpenWindow(() => !disposed && viewModel.CanRevealPrivatePresentation).ShowVoiceRecovery(); }
    }

    private SettingsWindow CreateWindow()
    {
        var settingsWindow = new SettingsWindow(viewModel, chooseMicrophone, captionDisplay);
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
        viewModel.BindAuditRetentionNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindSessionRetentionNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindWindowsSpeechRateNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindSpeechInputNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindSpeechComponentNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindInCallFeedbackNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindManualCallNativeLifetime(visible && !disposed);
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
        {
            if (ReferenceEquals(window, sender))
            {
                DesktopLog.Debug(logger, "Settings window closed");
                window!.PropertyChanged -= OnWindowPropertyChanged;
                window.Closed -= OnWindowClosed;
                window = null;
                BindNativeLifetime(false);
            }
        }, HostActivityLayer.Desktop, HostOperation.Presentation);
    }
}
