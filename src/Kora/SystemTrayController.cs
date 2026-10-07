using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Platform;

using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;

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
    private readonly TrayIcon trayIcon;
    private readonly TrayIcons trayIcons;
    private readonly DispatcherTimer trayClickTimer;
    private readonly ILogger<SystemTrayController> logger;
    private bool disposed;

    public SystemTrayController(
        MainViewModel viewModel,
        ILogger<SystemTrayController> logger,
        Func<Task>? reviewLocalVersion = null,
        Action? inspectEvidence = null)
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
        exitItem.Click += async (_, _) => await viewModel.ExitAsync();

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(settingsItem);
        menu.Add(documentationItem);
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
        listeningItem.Click += async (_, _) =>
            await viewModel.ToggleListeningCommand.ExecuteAsync();
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
        var refreshItem = new NativeMenuItem("Refresh microphones");
        refreshItem.Click += async (_, _) => await viewModel.RefreshMicrophonesAsync();
        menu.Add(refreshItem);
        var stopSpeechItem = new NativeMenuItem("Stop speaking");
        stopSpeechItem.Click += async (_, _) => await viewModel.StopSpeechCommand.ExecuteAsync();
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
        }

        trayIcon.Dispose();
    }

    private void OnTrayIconClicked(object? sender, EventArgs eventArgs)
    {
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
    }

    private void OnTrayClickTimerTick(object? sender, EventArgs eventArgs)
    {
        trayClickTimer.Stop();
        if (clickSequence.ResolvePendingClick() == ClickSequenceOutcome.SingleClick)
        {
            ShowWindow();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
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
            or nameof(MainViewModel.IsBusy))
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
        trayIcon.ToolTipText = $"{viewModel.AssistantName} - {viewModel.ListeningStatus}";

    private void UpdateVoiceControls()
    {
        listeningItem.Header = viewModel.ListeningButtonText;
        listeningItem.IsEnabled = viewModel.ToggleListeningCommand.CanExecute(null);
        var devices = new NativeMenu();
        var revision = viewModel.MicrophoneTopologyRevision;
        foreach (var device in viewModel.Microphones)
        {
            var item = new NativeMenuItem(
                $"{(string.Equals(viewModel.SelectedMicrophone?.Id, device.Id, StringComparison.Ordinal) ? "[selected] " : string.Empty)}{device.Name}"
                + (device.IsSystemDefault ? " (Windows default)" : $" · {device.Id}"));
            item.Click += async (_, _) => await viewModel.SelectMicrophoneAsync(device, revision);
            devices.Add(item);
        }
        if (viewModel.SelectedMicrophone is { } previous && !viewModel.Microphones.Contains(previous))
        {
            devices.Add(new NativeMenuItem($"Unavailable: {previous.Name} · {previous.Id}") { IsEnabled = false });
        }
        microphonesItem.Menu = devices;
    }

    private void ShowWindow()
    {
        DesktopLog.Debug(logger, "Main window was requested from the system tray");
        viewModel.ShowApplication();
    }

    private static void RunAfterNativeMenuCloses(Action action) =>
        DispatcherTimer.RunOnce(
            action,
            NativeMenuDismissalDelay,
            DispatcherPriority.Background);

    private static TimeSpan GetTrayDoubleClickTime() =>
        Avalonia.Application.Current?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse)
        ?? DefaultDoubleClickTime;
}