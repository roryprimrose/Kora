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
    private readonly TrayIcon trayIcon;
    private readonly TrayIcons trayIcons;
    private readonly DispatcherTimer trayClickTimer;
    private readonly ILogger<SystemTrayController> logger;
    private bool disposed;

    public SystemTrayController(
        MainViewModel viewModel,
        ILogger<SystemTrayController> logger)
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
        else if (eventArgs.PropertyName is nameof(MainViewModel.ListeningStatus) or nameof(MainViewModel.IsListening))
        {
            UpdateToolTip();
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