using System.ComponentModel;
using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using Kora.Application.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Kora.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
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

    private void OnWindowActionRequested(object? sender, WindowAction action)
    {
        switch (action)
        {
            case WindowAction.Show:
                Show();
                Activate();
                break;
            case WindowAction.Hide:
                Hide();
                break;
            case WindowAction.Close:
                Close();
                break;
            case WindowAction.Restart:
                Restart();
                break;
            default:
                throw new InvalidOperationException($"Unknown window action: {action}.");
        }
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