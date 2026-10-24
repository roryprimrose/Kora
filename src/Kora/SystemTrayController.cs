using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Platform;

using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
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
    private readonly NativeMenuItem voiceAudioItem;
    private readonly NativeMenuItem inputStatusItem;
    private readonly NativeMenu menu;
    private readonly Action? chooseMicrophone;
    private readonly Style defaultActionStyle;
    private readonly TrayIcon trayIcon;
    private readonly TrayIcons trayIcons;
    private readonly DispatcherTimer trayClickTimer;
    private readonly ILogger<SystemTrayController> logger;
    private bool disposed;

    public SystemTrayController(
        MainViewModel viewModel,
        ILogger<SystemTrayController> logger,
        Action? inspectEvidence = null,
        Action? inspectSessions = null,
        Action? chooseMicrophone = null,
        Action? newRequest = null)
    {
        this.viewModel = viewModel;
        this.logger = logger;
        this.chooseMicrophone = chooseMicrophone;

        showItem = new NativeMenuItem();
        showItem.Click += (_, _) => ShowWindow();

        settingsItem = new NativeMenuItem();
        settingsItem.Click += (_, _) => RunAfterNativeMenuCloses(viewModel.ShowSettings);

        exitItem = new NativeMenuItem();
        exitItem.Click += (_, _) => RunRecoveryAction(() => viewModel.ExitAsync());

        menu = new NativeMenu();
        inputStatusItem = new NativeMenuItem { IsEnabled = false };
        menu.Add(inputStatusItem);
        menu.Add(showItem);
        if (newRequest is not null)
        {
            var newRequestItem = new NativeMenuItem("New request...");
            newRequestItem.Click += (_, _) => RunAfterNativeMenuCloses(newRequest);
            menu.Add(newRequestItem);
        }
        if (inspectSessions is not null)
        {
            var sessionsItem = new NativeMenuItem("Sessions...");
            sessionsItem.Click += (_, _) => RunAfterNativeMenuCloses(inspectSessions);
            menu.Add(sessionsItem);
        }
        menu.Add(new NativeMenuItemSeparator());
        var disableListening = new NativeMenuItem("Disable listening");
        disableListening.Click += (_, _) => RunRecoveryAction(viewModel.DisableListeningFromTrayAsync);
        menu.Add(disableListening);
        voiceAudioItem = new NativeMenuItem("Voice & audio");
        menu.Add(voiceAudioItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(settingsItem);
        var help = new NativeMenu();
        var documentationItem = new NativeMenuItem("Documentation");
        documentationItem.Click += (_, _) => RunAfterNativeMenuCloses(viewModel.ShowDocumentation);
        help.Add(documentationItem);
        if (inspectEvidence is not null)
        {
            var evidenceItem = new NativeMenuItem("Evidence & diagnostics...");
            evidenceItem.Click += (_, _) => RunAfterNativeMenuCloses(inspectEvidence);
            help.Add(evidenceItem);
        }
        menu.Add(new NativeMenuItem("Help") { Menu = help });
        menu.Add(new NativeMenuItemSeparator());
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
        defaultActionStyle = new Style(selector => selector.Is<MenuItem>()
            .PropertyEquals(StyledElement.DataContextProperty, settingsItem))
        {
            Setters = { new Setter(MenuItem.FontWeightProperty, FontWeight.Bold) },
        };
        application.Styles.Add(defaultActionStyle);
        TrayIcon.SetIcons(application, trayIcons);
        trayClickTimer = new DispatcherTimer
        {
            Interval = GetTrayDoubleClickTime(),
        };
        trayClickTimer.Tick += OnTrayClickTimerTick;
        trayIcon.Clicked += OnTrayIconClicked;

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
        trayIcon.Clicked -= OnTrayIconClicked;
        trayClickTimer.Stop();
        trayClickTimer.Tick -= OnTrayClickTimerTick;
        clickSequence.Cancel();
        if (Avalonia.Application.Current is { } application)
        {
            TrayIcon.SetIcons(application, null);
            application.Styles.Remove(defaultActionStyle);
        }

        trayIcon.Dispose();
    }

    private void OnTrayIconClicked(object? sender, EventArgs eventArgs)
        => HandlePrimaryClick();

    internal void HandlePrimaryClick()
    {
        if (disposed) { return; }
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Presentation,
            RequestOrigin.LocalUi);
        switch (clickSequence.RegisterClick())
        {
            case ClickSequenceOutcome.Pending:
                trayClickTimer.Start();
                ShowWindow();
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
        if (clickSequence.ResolvePendingClick() is not (ClickSequenceOutcome.None or ClickSequenceOutcome.SingleClick))
        {
            throw new InvalidOperationException("The tray click sequence returned an invalid delayed outcome.");
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
        settingsItem.Header = "Settings...";
        exitItem.Header = $"Exit {viewModel.AssistantName}";
    }

    private void UpdateToolTip() =>
        trayIcon.ToolTipText = $"{viewModel.AssistantName} - {viewModel.TrayInputStatus}";

    private void UpdateVoiceControls()
    {
        inputStatusItem.Header = viewModel.TrayInputStatus;
        var revision = viewModel.MicrophoneTopologyRevision;
        var voiceAudio = new NativeMenu();
        var enable = new NativeMenuItem("Enable listening (push-to-talk readiness)");
        enable.Click += (_, _) => RunRecoveryAction(() => viewModel.EnableListeningFromTrayAsync(revision));
        voiceAudio.Add(enable);
        if (chooseMicrophone is { } choose)
        {
            var chooseItem = new NativeMenuItem("Choose microphone...");
            chooseItem.Click += (_, _) => RunAfterNativeMenuCloses(choose);
            voiceAudio.Add(chooseItem);
        }
        var voiceSettings = new NativeMenuItem("Speech & audio settings...");
        voiceSettings.Click += (_, _) => RunAfterNativeMenuCloses(() =>
        {
            viewModel.ShowSettings();
            viewModel.RequestVoiceRecovery();
        });
        voiceAudio.Add(voiceSettings);
        voiceAudioItem.Menu = voiceAudio;
    }

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
            NativeMenuDismissalDelay, DispatcherPriority.Input));

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