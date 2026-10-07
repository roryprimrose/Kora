using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Layout;

using Kora.Application.ViewModels;
using Kora.Core.Commands;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SettingsWindow : Window
{
    private readonly TabControl settingsTabs;
    private readonly Button pushToTalkButton;
    private readonly TabItem readinessTab;
    private readonly TabItem speechAudioTab;
    private bool isLocalModelReviewOpen;
    private bool isPowerShellReviewOpen;

    public SettingsWindow()
        : this(App.Services.GetRequiredService<MainViewModel>())
    {
    }

    public SettingsWindow(MainViewModel viewModel, Action? chooseMicrophone = null)
    {
        AvaloniaXamlLoader.Load(this);
        settingsTabs = this.FindControl<TabControl>("SettingsTabs")
            ?? throw new InvalidOperationException("The settings tab control is unavailable.");
        pushToTalkButton = this.FindControl<Button>("PushToTalkButton")
            ?? throw new InvalidOperationException("The push-to-talk control is unavailable.");
        readinessTab = this.FindControl<TabItem>("ReadinessTab")
            ?? throw new InvalidOperationException("The readiness settings tab is unavailable.");
        speechAudioTab = this.FindControl<TabItem>("SpeechAudioTab")
            ?? throw new InvalidOperationException("The speech and audio settings tab is unavailable.");
#pragma warning disable MA0147 // Avalonia routed events require void event handlers.
        pushToTalkButton.AddHandler(
            PointerPressedEvent,
            OnPushToTalkPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        pushToTalkButton.AddHandler(
            PointerReleasedEvent,
            OnPushToTalkReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        pushToTalkButton.AddHandler(
            KeyDownEvent,
            OnPushToTalkKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        pushToTalkButton.AddHandler(
            KeyUpEvent,
            OnPushToTalkKeyUp,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
#pragma warning restore MA0147
        DataContext = viewModel;
        var recovery = this.FindControl<Button>("ChooseMicrophone")
            ?? throw new InvalidOperationException("The microphone recovery control is unavailable.");
        recovery.IsEnabled = chooseMicrophone is not null;
        recovery.Click += (_, _) => chooseMicrophone?.Invoke();
        Deactivated += OnCaptureSurfaceUnavailable;
        Closed += OnCaptureSurfaceUnavailable;
    }

    private async void OnCaptureSurfaceUnavailable(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.EndPushToTalkAsync();
        }
    }

    public void ShowReadiness() => settingsTabs.SelectedItem = readinessTab;

    public void ShowVoiceRecovery() => settingsTabs.SelectedItem = speechAudioTab;

    private void OnMaintenanceClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel) { viewModel.ShowMaintenance(); }
    }

    private void OnPushToTalkFocusLost(object? sender, Avalonia.Input.FocusChangedEventArgs eventArgs) =>
        OnCaptureSurfaceUnavailable(sender, eventArgs);

    private async void OnPushToTalkPressed(object? sender, Avalonia.Input.PointerPressedEventArgs eventArgs)
    {
        if (sender is Control control && eventArgs.GetCurrentPoint(control).Properties.IsLeftButtonPressed
            && DataContext is MainViewModel viewModel)
        {
            eventArgs.Handled = true;
            eventArgs.Pointer.Capture(control);
            await viewModel.BeginPushToTalkAsync();
        }
    }

    private async void OnPushToTalkReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.EndPushToTalkAsync();
        }
        eventArgs.Pointer.Capture(null);
    }

    private async void OnPushToTalkCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.EndPushToTalkAsync();
        }
    }

    private async void OnPushToTalkKeyDown(object? sender, Avalonia.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is Avalonia.Input.Key.Space or Avalonia.Input.Key.Enter
            && DataContext is MainViewModel viewModel)
        {
            eventArgs.Handled = true;
            await viewModel.BeginPushToTalkAsync();
        }
    }

    private async void OnPushToTalkKeyUp(object? sender, Avalonia.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is Avalonia.Input.Key.Space or Avalonia.Input.Key.Enter
            && DataContext is MainViewModel viewModel)
        {
            eventArgs.Handled = true;
            await viewModel.EndPushToTalkAsync();
        }
    }

    private async void OnInstallLocalModelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (isLocalModelReviewOpen || isPowerShellReviewOpen)
        {
            return;
        }

        isLocalModelReviewOpen = true;
        try
        {
            var confirmation = new Window
            {
                Title = "Approve local model setup",
                Width = 510,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 18,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Install Ollama 0.35.1 for this Windows user via the winget Ollama.Ollama package if needed, then download qwen3:1.7b (about 1.36 GB) from the Ollama registry. The Qwen3 model is Apache-2.0 licensed. Kora will verify the model digest and run a local inference check. This uses network, disk space and CPU. Existing models will not be replaced.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            Children =
                            {
                                new Button { Content = "Install and verify" },
                                new Button { Content = "Cancel" },
                            },
                        },
                    },
                },
            };
            var buttons = (StackPanel)confirmation.Content;
            var actions = (StackPanel)buttons.Children[1];
            ((Button)actions.Children[0]).Click += (_, _) => confirmation.Close(true);
            ((Button)actions.Children[1]).Click += (_, _) => confirmation.Close(false);
            if (await confirmation.ShowDialog<bool>(this)
                && DataContext is MainViewModel viewModel)
            {
                await viewModel.InstallLocalModelAsync();
            }
        }
        finally
        {
            isLocalModelReviewOpen = false;
        }
    }

    private async void OnInstallPowerShellClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (isPowerShellReviewOpen || isLocalModelReviewOpen)
        {
            return;
        }

        isPowerShellReviewOpen = true;
        try
        {
            var install = new Button { Content = "Install and verify" };
            var cancel = new Button { Content = "Cancel" };
            var confirmation = new Window
            {
                Title = "Approve PowerShell 7 setup",
                Width = 510,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 18,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Kora will reuse a healthy PowerShell 7 (pwsh.exe) installation, or install Microsoft.PowerShell for this Windows user through winget. Installation may use the network and disk space. Kora verifies the runtime in a no-profile process afterward. No skill script is run or granted by this setup.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            Children = { install, cancel },
                        },
                    },
                },
            };
            install.Click += (_, _) => confirmation.Close(true);
            cancel.Click += (_, _) => confirmation.Close(false);
            if (await confirmation.ShowDialog<bool>(this)
                && DataContext is MainViewModel viewModel)
            {
                await viewModel.InstallPowerShellAsync();
            }
        }
        finally
        {
            isPowerShellReviewOpen = false;
        }
    }

    private void OnRevokeSessionApprovalClicked(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: BuiltInAction action }
            && DataContext is MainViewModel viewModel)
        {
            viewModel.RevokeModelActionApproval(action, ModelApprovalScope.Session);
        }
    }

    private void OnRevokeAlwaysApprovalClicked(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: BuiltInAction action }
            && DataContext is MainViewModel viewModel)
        {
            viewModel.RevokeModelActionApproval(action, ModelApprovalScope.Always);
        }
    }
}
