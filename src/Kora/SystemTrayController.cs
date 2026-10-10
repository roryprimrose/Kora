using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Platform;

using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
using Kora.Core.Voice;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class SystemTrayController : IDisposable
{
    private static readonly TimeSpan DefaultDoubleClickTime =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan NativeMenuDismissalDelay =
        TimeSpan.FromMilliseconds(50);

    private static readonly Uri IconUri = new("avares://Kora/Assets/Kora.ico");

    private readonly MainViewModel viewModel;
    private readonly ClickSequenceResolver clickSequence = new();
    private readonly NativeMenuItem showItem;
    private readonly NativeMenuItem settingsItem;
    private readonly NativeMenuItem exitItem;
    private readonly NativeMenuItem listeningItem;
    private readonly NativeMenuItem microphonesItem;
    private readonly NativeMenuItem inputStatusItem;
    private readonly NativeMenu menu;
    private readonly AsyncCommand refreshMicrophonesCommand;
    private readonly TrayIcon trayIcon;
    private readonly TrayIcons trayIcons;
    private readonly DispatcherTimer trayClickTimer;
    private readonly ILogger<SystemTrayController> logger;
    private bool disposed;

    public SystemTrayController(
        MainViewModel viewModel,
        ILogger<SystemTrayController> logger,
        Func<Task>? reviewLocalVersion = null,
        Action? inspectEvidence = null,
        Action? inspectSkillPackages = null,
        Action? inspectSessions = null,
        Action? reviewMaintenance = null,
        Action? chooseMicrophone = null,
        Action? inspectExactGrants = null)
    {
        this.viewModel = viewModel;
        this.logger = logger;

        showItem = new NativeMenuItem();
        showItem.Click += (_, _) => ShowWindow();

        settingsItem = new NativeMenuItem();
        settingsItem.Click += (_, _) => RunAfterNativeMenuCloses(viewModel.ShowSettings);

        var documentationItem = new NativeMenuItem("Documentation");
        documentationItem.Click += (_, _) => RunAfterNativeMenuCloses(viewModel.ShowDocumentation);

        exitItem = new NativeMenuItem();
        exitItem.Click += (_, _) => RunRecoveryAction(() => viewModel.ExitAsync());

        menu = new NativeMenu();
        inputStatusItem = new NativeMenuItem { IsEnabled = false };
        menu.Add(inputStatusItem);
        menu.Add(showItem);
        menu.Add(settingsItem);
        menu.Add(documentationItem);
        if (inspectExactGrants is not null)
        {
            var exactGrantsItem = new NativeMenuItem("Exact operation grants");
            exactGrantsItem.Click += (_, _) => RunAfterNativeMenuCloses(inspectExactGrants);
            menu.Add(exactGrantsItem);
        }
        if (inspectSkillPackages is not null)
        {
            var skillsItem = new NativeMenuItem("Skill packages (inspection only)");
            skillsItem.Click += (_, _) => RunAfterNativeMenuCloses(inspectSkillPackages);
            menu.Add(skillsItem);
        }
        if (inspectSessions is not null)
        {
            var sessionsItem = new NativeMenuItem("Sessions");
            sessionsItem.Click += (_, _) => RunAfterNativeMenuCloses(inspectSessions);
            menu.Add(sessionsItem);
        }
        var previewClipboard = new NativeMenuItem("Preview clipboard (local plain text)");
        var previewClipboardCommand = new AsyncCommand(viewModel.PreviewClipboardAsync,
            exception => viewModel.ReportHostInteractionFailure(
                "Local clipboard preview failed. No success is claimed. Failure type: " + exception.GetType().Name));
        previewClipboard.Click += (_, _) => RunAfterNativeMenuCloses(() => previewClipboardCommand.Execute(null));
        menu.Add(previewClipboard);
        var previewFile = new NativeMenuItem("Preview file (local inspection only)");
        var previewFileCommand = new AsyncCommand(viewModel.PreviewFileAsync,
            exception => viewModel.ReportHostInteractionFailure(
                "Local file preview failed. No success is claimed. Failure type: " + exception.GetType().Name));
        previewFile.Click += (_, _) => RunAfterNativeMenuCloses(() => previewFileCommand.Execute(null));
        menu.Add(previewFile);
        var previewFolder = new NativeMenuItem("Preview folder (immediate files, local lexical search)");
        var previewFolderCommand = new AsyncCommand(viewModel.PreviewFolderAsync,
            exception => viewModel.ReportHostInteractionFailure(
                "Local folder preview failed. No success is claimed. Failure type: " + exception.GetType().Name));
        previewFolder.Click += (_, _) => RunAfterNativeMenuCloses(() => previewFolderCommand.Execute(null));
        menu.Add(previewFolder);
        if (reviewMaintenance is not null)
        {
            var maintenanceItem = new NativeMenuItem("Release maintenance (notify-only)");
            maintenanceItem.Click += (_, _) => RunAfterNativeMenuCloses(reviewMaintenance);
            menu.Add(maintenanceItem);
        }
        if (inspectEvidence is not null)
        {
            var evidenceItem = new NativeMenuItem("Evidence (read-only)");
            evidenceItem.Click += (_, _) => RunAfterNativeMenuCloses(inspectEvidence);
            menu.Add(evidenceItem);
        }
        if (reviewLocalVersion is not null)
        {
            var reviewVersion = new NativeMenuItem("Review local version (native question)");
            var reviewCommand = new AsyncCommand(reviewLocalVersion,
                exception => viewModel.ReportHostInteractionFailure(
                    "Native question presentation failed. No success is claimed. Close and start a fresh review. Failure type: "
                    + exception.GetType().Name));
            reviewVersion.Click += (_, _) => RunAfterNativeMenuCloses(() => reviewCommand.Execute(null));
            menu.Add(reviewVersion);
        }
        listeningItem = new NativeMenuItem();
        listeningItem.Header = "Listening controls";
        menu.Add(listeningItem);
        var voiceRecoveryItem = new NativeMenuItem("Voice consent / push-to-talk");
        voiceRecoveryItem.Click += (_, _) => RunAfterNativeMenuCloses(() =>
        {
            viewModel.ShowSettings();
            viewModel.RequestVoiceRecovery();
        });
        menu.Add(voiceRecoveryItem);
        microphonesItem = new NativeMenuItem("Microphones");
        menu.Add(microphonesItem);
        if (chooseMicrophone is not null)
        {
            var chooseItem = new NativeMenuItem("Choose microphone (native recovery)");
            chooseItem.Click += (_, _) => RunAfterNativeMenuCloses(chooseMicrophone);
            menu.Add(chooseItem);
        }
        var refreshItem = new NativeMenuItem("Refresh microphones");
        refreshMicrophonesCommand = new AsyncCommand(viewModel.RefreshMicrophonesAsync,
            exception => viewModel.ReportHostInteractionFailure(
                "Microphone refresh failed. Retry Refresh devices or review Settings. Failure type: " + exception.GetType().Name));
        refreshItem.Click += (_, _) => RunAfterNativeMenuCloses(() => refreshMicrophonesCommand.Execute(null));
        menu.Add(refreshItem);
        var stopSpeechItem = new NativeMenuItem("Stop speaking");
        stopSpeechItem.Click += (_, _) => RunRecoveryAction(viewModel.StopSpeakingFromTrayAsync);
        menu.Add(stopSpeechItem);
        menu.Add(exitItem);

        using var iconStream = AssetLoader.Open(IconUri);
        trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            IsVisible = true,
            Menu = menu,
        };
        trayIcons = [trayIcon];
        var application = Avalonia.Application.Current
            ?? throw new InvalidOperationException("The Avalonia application is unavailable.");
        TrayIcon.SetIcons(application, trayIcons);
        trayClickTimer = new DispatcherTimer
        {
            Interval = GetTrayDoubleClickTime(),
        };
        trayClickTimer.Tick += OnTrayClickTimerTick;
        trayIcon.Clicked += OnTrayIconClicked;
        menu.Opening += OnMenuOpening;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateIdentityText();
        UpdateToolTip();
        UpdateVoiceControls();
        DesktopLog.Information(logger, "System tray controls initialized");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DesktopLog.Debug(logger, "Disposing system tray controls");
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        menu.Opening -= OnMenuOpening;
        trayIcon.Clicked -= OnTrayIconClicked;
        trayClickTimer.Stop();
        trayClickTimer.Tick -= OnTrayClickTimerTick;
        clickSequence.Cancel();
        if (Avalonia.Application.Current is { } application)
        {
            TrayIcon.SetIcons(application, null);
        }

        trayIcon.Dispose();
    }

    private void OnTrayIconClicked(object? sender, EventArgs eventArgs)
    {
        if (disposed) { return; }
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Presentation,
            RequestOrigin.LocalUi);
        switch (clickSequence.RegisterClick())
        {
            case ClickSequenceOutcome.Pending:
                trayClickTimer.Start();
                break;
            case ClickSequenceOutcome.DoubleClick:
                trayClickTimer.Stop();
                DesktopLog.Debug(logger, "Settings were requested by a system tray double-click");
                viewModel.ShowSettings();
                break;
            default:
                throw new InvalidOperationException("The tray click sequence returned an invalid immediate outcome.");
        }
        activity.Complete(HostOperationOutcome.Completed);
    }

    private void OnTrayClickTimerTick(object? sender, EventArgs eventArgs)
    {
        if (disposed) { return; }
        trayClickTimer.Stop();
        if (clickSequence.ResolvePendingClick() == ClickSequenceOutcome.SingleClick)
        {
            ShowWindow();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (disposed) { return; }
        if (string.Equals(
            eventArgs.PropertyName,
            nameof(MainViewModel.AssistantName),
            StringComparison.Ordinal))
        {
            UpdateIdentityText();
            UpdateToolTip();
        }
        else if (eventArgs.PropertyName is nameof(MainViewModel.ListeningStatus) or nameof(MainViewModel.IsListening)
            or nameof(MainViewModel.IsVoiceEnabled) or nameof(MainViewModel.HasVoiceConsent)
            or nameof(MainViewModel.MicrophoneTopologyRevision) or nameof(MainViewModel.SelectedMicrophone)
            or nameof(MainViewModel.IsBusy) or nameof(MainViewModel.IsRefreshingMicrophones)
            or nameof(MainViewModel.TrayInputStatus))
        {
            UpdateToolTip();
            UpdateVoiceControls();
        }
    }

    private void UpdateIdentityText()
    {
        showItem.Header = $"Show {viewModel.AssistantName}";
        settingsItem.Header = $"{viewModel.AssistantName} Settings";
        exitItem.Header = $"Exit {viewModel.AssistantName}";
    }

    private void UpdateToolTip() =>
        trayIcon.ToolTipText = $"{viewModel.AssistantName} - {viewModel.TrayInputStatus}";

    private void UpdateVoiceControls()
    {
        inputStatusItem.Header = viewModel.TrayInputStatus;
        var devices = new NativeMenu();
        var revision = viewModel.MicrophoneTopologyRevision;
        var listening = new NativeMenu();
        var enable = new NativeMenuItem("Enable listening (push-to-talk readiness)");
        enable.Click += (_, _) => RunRecoveryAction(() => viewModel.EnableListeningFromTrayAsync(revision));
        listening.Add(enable);
        var disable = new NativeMenuItem("Disable listening (close input for this run)");
        disable.Click += (_, _) => RunRecoveryAction(viewModel.DisableListeningFromTrayAsync);
        listening.Add(disable);
        listeningItem.Menu = listening;
        var canReveal = viewModel.CanUseTrayMicrophoneRecovery;
        foreach (var device in viewModel.Microphones)
        {
            if (!canReveal) { break; }
            var selected = string.Equals(viewModel.SelectedMicrophone?.Id, device.Id, StringComparison.Ordinal);
            var availability = !viewModel.IsMicrophoneCatalogCurrent ? "refresh required"
                : device.IsSystemDefault
                    ? viewModel.IsSystemMicrophoneAvailable ? "Windows default; available" : "Windows default; unavailable"
                    : "available";
            var item = CreateMicrophoneItem(device, selected, availability);
            item.Click += (_, _) => RunRecoveryAction(() => viewModel.SelectMicrophoneAsync(device, revision));
            devices.Add(item);
        }
        if (canReveal && viewModel.SelectedMicrophone is { } previous && !viewModel.Microphones.Contains(previous))
        {
            devices.Add(CreateMicrophoneItem(previous, selected: true, "unavailable; preference retained", selectable: false));
        }
        if (!canReveal)
        {
            devices.Add(new NativeMenuItem("Return to the owning unlocked host to refresh") { IsEnabled = false });
        }
        else if (viewModel.IsRefreshingMicrophones)
        {
            devices.Add(new NativeMenuItem("Refreshing devices (up to five seconds)") { IsEnabled = false });
        }
        microphonesItem.Menu = devices;
    }

    private void OnMenuOpening(object? sender, EventArgs eventArgs)
    {
        if (disposed) { return; }
        UpdateVoiceControls();
        refreshMicrophonesCommand.Execute(null);
    }

    internal static NativeMenuItem CreateMicrophoneItem(
        MicrophoneDevice device, bool selected, string availability, bool selectable = true) =>
        new(device.Name + " (" + availability + ")"
            + (selected ? " - selected preference, not capture" : string.Empty))
        {
            ToggleType = MenuItemToggleType.Radio, IsChecked = selected, IsEnabled = selectable,
        };

    private void RunRecoveryAction(Func<Task> action)
    {
        var command = new AsyncCommand(
            () => HostRequestRunner.RunAsync(RequestOrigin.LocalUi, action),
            exception => HostRequestRunner.Run(RequestOrigin.LocalUi, () => viewModel.ReportHostInteractionFailure(
                "Native tray action failed. Review Settings and retry. Failure type: " + exception.GetType().Name)));
        RunAfterNativeMenuCloses(() => command.Execute(null));
    }

    private void ShowWindow()
    {
        HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
        {
            DesktopLog.Debug(logger, "Main window was requested from the system tray");
            viewModel.ShowApplication();
        }, HostActivityLayer.Desktop, HostOperation.Presentation);
    }

    private void RunAfterNativeMenuCloses(Action action) =>
        RunAfterNativeMenuCloses(action, continuation => DispatcherTimer.RunOnce(
            () => { if (!disposed) { continuation(); } },
            NativeMenuDismissalDelay, DispatcherPriority.Background));

    internal static void RunAfterNativeMenuCloses(Action action, Action<Action> schedule)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(schedule);
        var begin = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation,
            RequestOrigin.LocalUi);
        schedule(
            () =>
            {
                using var activity = begin();
                try
                {
                    action();
                    activity.Complete(HostOperationOutcome.Completed);
                }
                catch
                {
                    activity.Complete(HostOperationOutcome.Failed);
                    throw;
                }
            });
    }

    private static TimeSpan GetTrayDoubleClickTime() =>
        Avalonia.Application.Current?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse)
        ?? DefaultDoubleClickTime;
}