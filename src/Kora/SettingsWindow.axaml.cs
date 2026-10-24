using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Layout;

using Kora.Application.ViewModels;
using Kora.Application.Infrastructure;
using Kora.Core.Commands;
using Kora.Application.Presentation;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SettingsWindow : Window
{
    private readonly TabControl settingsTabs;
    private readonly TabItem readinessTab;
    private readonly TabItem speechAudioTab;
    private readonly TabItem appearanceTab;
    private readonly TabItem responsesTab;
    private readonly TabItem sessionsTab;
    private readonly TabItem callsTab;
    private bool isLocalModelReviewOpen;
    private bool isPowerShellReviewOpen;
    private readonly MainViewModel model;

    public SettingsWindow()
        : this(App.Services.GetRequiredService<MainViewModel>())
    {
    }

    public SettingsWindow(MainViewModel viewModel, Action? chooseMicrophone = null,
        SpeechCaptionWindowController? captionDisplay = null)
    {
        model = viewModel;
        AvaloniaXamlLoader.Load(this);
        settingsTabs = this.FindControl<TabControl>("SettingsTabs")
            ?? throw new InvalidOperationException("The settings tab control is unavailable.");
        readinessTab = this.FindControl<TabItem>("ReadinessTab")
            ?? throw new InvalidOperationException("The readiness settings tab is unavailable.");
        speechAudioTab = this.FindControl<TabItem>("SpeechAudioTab")
            ?? throw new InvalidOperationException("The speech and audio settings tab is unavailable.");
        appearanceTab = this.FindControl<TabItem>("AppearanceTab")
            ?? throw new InvalidOperationException("The appearance settings tab is unavailable.");
        responsesTab = this.FindControl<TabItem>("ResponsesTab")
            ?? throw new InvalidOperationException("The response settings tab is unavailable.");
        sessionsTab = this.FindControl<TabItem>("SessionsTab")
            ?? throw new InvalidOperationException("The session settings tab is unavailable.");
        callsTab = this.FindControl<TabItem>("CallsTab")
            ?? throw new InvalidOperationException("The call settings tab is unavailable.");
        DataContext = viewModel;
        BindSpeechComponents(viewModel);
        if (captionDisplay is not null)
        {
            this.FindControl<CaptionDisplaySelector>("CaptionDisplaySelector")!.Bind(captionDisplay);
        }
        var recovery = this.FindControl<Button>("ChooseMicrophone")
            ?? throw new InvalidOperationException("The microphone recovery control is unavailable.");
        recovery.IsEnabled = chooseMicrophone is not null;
        recovery.Click += (_, _) => chooseMicrophone?.Invoke();
    }

    public void ShowReadiness() => settingsTabs.SelectedItem = readinessTab;

    public void ShowVoiceRecovery() => settingsTabs.SelectedItem = speechAudioTab;

    public void ShowSection(SettingsSection section)
    {
        var tab = section switch
        {
            SettingsSection.Appearance => appearanceTab,
            SettingsSection.SpeechAudio => speechAudioTab,
            SettingsSection.Responses => responsesTab,
            SettingsSection.Sessions => sessionsTab,
            SettingsSection.Calls => callsTab,
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown Settings section."),
        };
        settingsTabs.SelectedItem = tab;
        if (!ReferenceEquals(settingsTabs.SelectedItem, tab))
        { throw new InvalidOperationException("The native Settings section was not selected."); }
    }

    private void OnMaintenanceClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel) { viewModel.ShowMaintenance(); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Avalonia Click requires void; dialog/setup failures are explicitly reported to the supplied model.")]
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
                            Text = "Install Ollama 0.35.1 for your Windows account if needed, then download the Qwen3 1.7B AI model (about 1.36 GB) from Ollama. The model uses the Apache 2.0 license. Kora will check the download and test the model on this device. This uses internet access, disk space and processing power. Existing models will not be replaced.",
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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.ReportHostInteractionFailure("Local model setup could not be completed. " + exception.Message);
        }
        finally
        {
            isLocalModelReviewOpen = false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Avalonia Click requires void; dialog/setup failures are explicitly reported to the supplied model.")]
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
                            Text = "Kora will use an existing working PowerShell 7 installation, or install it from Microsoft's package using Windows Package Manager for your Windows account. Installation may use internet access and disk space. Kora will check that PowerShell starts correctly. This setup does not run skill scripts or give them permission to run.",
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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.ReportHostInteractionFailure("PowerShell setup could not be completed. " + exception.Message);
        }
        finally
        {
            isPowerShellReviewOpen = false;
        }
    }

    private void OnRevokeSessionApprovalClicked(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs eventArgs) => RevokeApproval(sender, ModelApprovalScope.Session);

    private void OnRevokeAlwaysApprovalClicked(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs eventArgs) => RevokeApproval(sender, ModelApprovalScope.Always);

    private void RevokeApproval(object? sender, ModelApprovalScope scope)
    {
        if (sender is Button { Tag: BuiltInAction action }
            && DataContext is MainViewModel viewModel)
        {
            new AsyncCommand(() => viewModel.RevokeModelActionApprovalAsync(action, scope),
                exception => viewModel.ReportHostInteractionFailure("Grant revocation not confirmed. " + exception.Message)).Execute(null);
        }
    }
}
