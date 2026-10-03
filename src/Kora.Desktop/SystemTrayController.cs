using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

using Kora.Application.ViewModels;

namespace Kora.Desktop;

public sealed class SystemTrayController : IDisposable
{
    private static readonly Uri IconUri = new("avares://Kora.Desktop/Assets/Kora.ico");

    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly MainViewModel viewModel;
    private readonly TrayIcon trayIcon;
    private bool disposed;

    public SystemTrayController(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainViewModel viewModel)
    {
        this.desktop = desktop;
        this.viewModel = viewModel;

        var showItem = new NativeMenuItem("Show Kora");
        showItem.Click += (_, _) => ShowWindow();

        var detectMicrophoneItem = new NativeMenuItem("Detect microphone");
        detectMicrophoneItem.Click += async (_, _) =>
        {
            await viewModel.DetectMicrophonesAsync();
            ShowWindow();
        };

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += async (_, _) => await viewModel.ExitAsync();

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(detectMicrophoneItem);
        menu.Add(exitItem);

        using var iconStream = AssetLoader.Open(IconUri);
        trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            IsVisible = true,
            Menu = menu,
        };
        trayIcon.Clicked += (_, _) => ShowWindow();

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateToolTip();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        trayIcon.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainViewModel.ListeningStatus) or nameof(MainViewModel.IsListening))
        {
            UpdateToolTip();
        }
    }

    private void UpdateToolTip() => trayIcon.ToolTipText = $"Kora - {viewModel.ListeningStatus}";

    private void ShowWindow()
    {
        if (desktop.MainWindow is not { } window)
        {
            return;
        }

        window.Show();
        window.Activate();
    }
}