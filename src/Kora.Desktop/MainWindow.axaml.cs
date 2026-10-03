using System.ComponentModel;
using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using Kora.Application.ViewModels;
using Kora.Application.Visuals;

using Microsoft.Extensions.DependencyInjection;

namespace Kora.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private CancellationTokenSource? pendingHide;
    private bool initialized;

    public MainWindow()
        : this(App.Services.GetRequiredService<MainViewModel>())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        viewModel.WindowActionRequested += OnWindowActionRequested;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs eventArgs)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        await viewModel.InitializeAsync();
    }

    private async void OnWindowActionRequested(object? sender, WindowAction action)
    {
        switch (action)
        {
            case WindowAction.Show:
                CancelPendingHide();
                Show();
                Activate();
                break;
            case WindowAction.Hide:
                CancelPendingHide();
                var hideRequest = new CancellationTokenSource();
                pendingHide = hideRequest;
                try
                {
                    await Task.Delay(
                        ConstellationAnimation.VisibilityTransitionDuration + ConstellationAnimation.FrameInterval,
                        hideRequest.Token);
                    Hide();
                }
                catch (OperationCanceledException) when (hideRequest.IsCancellationRequested)
                {
                }
                finally
                {
                    if (ReferenceEquals(pendingHide, hideRequest))
                    {
                        pendingHide = null;
                    }

                    hideRequest.Dispose();
                }
                break;
            case WindowAction.Close:
                CancelPendingHide();
                Close();
                break;
            case WindowAction.Restart:
                CancelPendingHide();
                Restart();
                break;
            default:
                throw new InvalidOperationException($"Unknown window action: {action}.");
        }
    }

    private void CancelPendingHide()
    {
        pendingHide?.Cancel();
        pendingHide = null;
    }

    private void Restart()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("The Kora executable path is unavailable.");
        }

        try
        {
            Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
            });
            Close();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Windows could not restart Kora.", exception);
        }
    }
}