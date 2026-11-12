using Avalonia;
using Avalonia.Controls;

using Kora.Application.ViewModels;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Application.Presentation;
using Kora.Application.Configuration.SettingInspection;
using Kora.SettingInspection;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class SettingsWindowController : IDisposable, ISettingsSectionNavigation
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<SettingsWindowController> logger;
    private readonly Action? chooseMicrophone;
    private readonly SpeechCaptionWindowController? captionDisplay;
    private SettingsWindow? window;
    private SettingsView? view;
    private Func<Window>? getWorkbench;
    private Action<SettingsView>? presentWorkbench;
    private bool disposed;
    private int nativeVisible;
    private long nativeVisibilityRevision;
    private SettingInspectionWorkflow? settingInspection;
    private TimeProvider inspectionTime = TimeProvider.System;

    internal void BindSettingInspection(SettingInspectionWorkflow workflow, TimeProvider time)
    { settingInspection = workflow; inspectionTime = time; }

    internal void BindWorkbench(Func<Window> owner, Action<SettingsView> present)
    {
        getWorkbench = owner;
        presentWorkbench = present;
    }

    internal Action? EditOwnedInstructions { get; set; }
    internal Action? InspectSkillPackages { get; set; }
    internal Action? InspectExactGrants { get; set; }
    internal Func<Task>? ReviewLocalVersion { get; set; }
    internal Func<Task>? ReturnToVersionQuestion { get; set; }

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
        viewModel.KnowledgeSourceAnswerRequested += OnKnowledgeSourceAnswer;
        viewModel.KnowledgeSourceCaptureLimitsRequested += OnKnowledgeSourceCaptureLimits;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.BindSettingsSectionNavigation(null);
        viewModel.KnowledgeSourceAnswerRequested -= OnKnowledgeSourceAnswer;
        viewModel.KnowledgeSourceCaptureLimitsRequested -= OnKnowledgeSourceCaptureLimits;
        BindNativeLifetime(false);
        DesktopLog.Debug(logger, "Disposing the settings window controller");
        viewModel.SettingsRequested -= OnSettingsRequested;
        viewModel.ReadinessRequested -= OnReadinessRequested;
        viewModel.VoiceRecoveryRequested -= OnVoiceRecoveryRequested;
        if (view is not null)
        {
            view.PresentationChanged -= OnViewPresentationChanged;
            view.Closed -= OnViewClosed;
            if (view.Owner is ResponseWindow workbench) { workbench.RemoveHostedView(view); }
            view.Dispose();
            view = null;
        }
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

        PresentRequestedSettings();
    }

    private void PresentRequestedSettings(Action<SettingsView>? select = null)
    {
        try
        {
            var target = OpenWindow(() => !disposed && viewModel.CanRevealPrivatePresentation);
            select?.Invoke(target);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException
            or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (view is { } target) { HideSettings(target); }
            DesktopLog.Error(logger, exception, "Opening the native Settings view");
            viewModel.ReportHostInteractionFailure(
                "Settings could not be opened. No setting or permission was changed. Retry from the owning unlocked host. Failure type: "
                + exception.GetType().Name);
        }
    }

    private Task OnKnowledgeSourceAnswer(Kora.Application.KnowledgeSources.KnowledgeSourceAnswerCommand command,
        Func<bool> eligible) => OpenWindow(eligible).OpenKnowledgeSourceAnswerAsync(command, eligible);

    private Task OnKnowledgeSourceCaptureLimits(Kora.Application.Configuration.KnowledgeSourceCaptureLimitsCommand command,
        Func<bool> eligible) => OpenWindow(eligible).OpenKnowledgeSourceCaptureLimitsAsync(command, eligible);

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
            HideSettings(target);
            throw new InvalidOperationException("Settings navigation was revoked before tab selection.");
        }

        target.ShowSection(section);
        if (!admitted())
        {
            HideSettings(target);
            throw new InvalidOperationException("Settings navigation was revoked during tab selection.");
        }
    }

    public void ShowFilter(string query, Func<bool> admitted)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        if (!Kora.Core.Tools.SettingsNavigationTool.IsValidQuery(query))
        { throw new ArgumentException("Invalid settings search filter.", nameof(query)); }
        var target = OpenWindow(admitted);
        if (!admitted())
        {
            HideSettings(target);
            throw new InvalidOperationException("Settings search was revoked before filtering.");
        }
        target.ShowFilter(query);
        if (!admitted())
        {
            HideSettings(target);
            throw new InvalidOperationException("Settings search was revoked during filtering.");
        }
    }

    internal void ShowPresence(Func<bool> admitted)
    {
        var target = OpenWindow(admitted);
        target.ShowPresence(admitted);
    }

    private SettingsView OpenWindow(Func<bool> admitted)
    {
        if (disposed || !viewModel.CanRevealPrivatePresentation || !admitted())
        { throw new InvalidOperationException("The native Settings host is unavailable."); }
        DesktopLog.Information(logger, "Settings window was requested");
        if (view is null)
        {
            if (getWorkbench is not null)
            {
                view = new SettingsView(viewModel, getWorkbench(), chooseMicrophone, captionDisplay);
                ConfigureView(view);
            }
            else
            {
                window = CreateWindow();
                view = window.View;
                ConfigureView(view);
            }
        }
        var target = view;
        if (!admitted()) { throw new InvalidOperationException("Settings navigation was revoked before showing."); }
        if (presentWorkbench is not null)
        {
            presentWorkbench(target);
        }
        else
        {
            var native = window ?? throw new InvalidOperationException("The legacy Settings adapter is unavailable.");
            if (!native.IsVisible) { native.Show(); }
            native.Activate();
            native.UpdateLayout();
        }
        target.RefreshNativePresentation();
        if (disposed || !ReferenceEquals(view, target) || !target.IsNativePresented || !admitted())
        {
            HideSettings(target);
            throw new InvalidOperationException("The native Settings view is not visible or admission was revoked.");
        }
        BindNativeLifetime(target.IsNativePresented);
        return target;
    }

    private void OnReadinessRequested(object? sender, EventArgs eventArgs)
    {
        if (!disposed && viewModel.CanRevealPrivatePresentation)
        { PresentRequestedSettings(static target => target.ShowReadiness()); }
    }

    private void OnVoiceRecoveryRequested(object? sender, EventArgs eventArgs)
    {
        if (!disposed && viewModel.CanRevealPrivatePresentation)
        { PresentRequestedSettings(static target => target.ShowVoiceRecovery()); }
    }

    private SettingsWindow CreateWindow()
    {
        var settingsWindow = new SettingsWindow(viewModel, chooseMicrophone, captionDisplay);
        settingsWindow.PropertyChanged += OnWindowPropertyChanged;
        settingsWindow.Closed += OnWindowClosed;
        return settingsWindow;
    }

    private static void HideSettings(SettingsView target)
    {
        target.IsVisible = false;
        if (!target.Owner.IsVisible) { return; }
        if (target.Owner is ResponseWindow workbench) { workbench.HideResponse(); }
        else { target.Owner.Hide(); }
    }

    private void ConfigureView(SettingsView settingsView)
    {
        settingsView.BindNavigation(EditOwnedInstructions, InspectSkillPackages, InspectExactGrants,
            ReviewLocalVersion, ReturnToVersionQuestion);
        if (settingInspection is { } inspection)
        {
            settingsView.FindControl<SettingInspectionControl>("SettingInspection")!.Bind(
                viewModel, inspection, inspectionTime, settingsView.Owner, CaptureLifetime);
        }
        settingsView.PresentationChanged += OnViewPresentationChanged;
        settingsView.Closed += OnViewClosed;
    }

    private void OnViewPresentationChanged(object? sender, EventArgs args) =>
        BindNativeLifetime(view?.IsNativePresented == true);
    private void OnViewClosed(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(view, sender)) { return; }
        view!.PresentationChanged -= OnViewPresentationChanged;
        view.Closed -= OnViewClosed;
        view = null;
        BindNativeLifetime(false);
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
        viewModel.BindLogonStartupNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindStartupPresentationNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindDiagnosticRetentionNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindAuditRetentionNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindSessionRetentionNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindOrdinaryDeadlineNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindWindowsSpeechRateNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindPlaybackVolumeNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindSpeechInputNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindQuietHoursNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindForegroundPttNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindConversationalReplyNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindDetailPresentationNativeLifetime(() =>
            Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision);
        viewModel.BindMarkdownDisplayNativeLifetime(() =>
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
