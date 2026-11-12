using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Layout;
using Avalonia;

using Kora.Application.ViewModels;
using Kora.Application.Infrastructure;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Application.Presentation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Auditing;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Controls;
using Kora.Core.Voice;
using Kora.Application.Configuration;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SettingsView : UserControl, IDisposable
{
    private readonly SettingsBrowser settingsTabs;
    private readonly SettingsPage readinessTab;
    private readonly SettingsPage speechAudioTab;
    private readonly SettingsPage appearanceTab;
    private readonly SettingsPage responsesTab;
    private readonly SettingsPage sessionsTab;
    private readonly SettingsPage callsTab;
    private bool isLocalModelReviewOpen;
    private bool isPowerShellReviewOpen;
    private readonly MainViewModel model;
    private int undoLifetime;
    private int undoVisible;
    private readonly Window owner;
    private readonly NativeViewLifetime nativeLifetime;
    private volatile bool disposed;
    private bool nativePresented;
    private bool? callActivationAvailable;
    private string? preparedSettingId;
    internal Window Owner => owner;
    internal bool IsNativePresented => nativePresented;
    private bool IsActive => nativeLifetime.IsActive;
    internal event EventHandler? PresentationChanged;
    internal event EventHandler? Closed;

    public SettingsView() => throw new InvalidOperationException("Settings requires an explicit host-owned view and window.");

    public SettingsView(MainViewModel viewModel, Window owner, Action? chooseMicrophone = null,
        SpeechCaptionWindowController? captionDisplay = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(owner);
        model = viewModel;
        this.owner = owner;
        AvaloniaXamlLoader.Load(this);
        settingsTabs = this.FindControl<SettingsBrowser>("SettingsTabs")
            ?? throw new InvalidOperationException("The settings tab control is unavailable.");
        readinessTab = this.FindControl<SettingsPage>("ReadinessTab")
            ?? throw new InvalidOperationException("The readiness settings tab is unavailable.");
        speechAudioTab = this.FindControl<SettingsPage>("SpeechAudioTab")
            ?? throw new InvalidOperationException("The speech and audio settings tab is unavailable.");
        appearanceTab = this.FindControl<SettingsPage>("AppearanceTab")
            ?? throw new InvalidOperationException("The appearance settings tab is unavailable.");
        responsesTab = this.FindControl<SettingsPage>("ResponsesTab")
            ?? throw new InvalidOperationException("The response settings tab is unavailable.");
        sessionsTab = this.FindControl<SettingsPage>("SessionsTab")
            ?? throw new InvalidOperationException("The session settings tab is unavailable.");
        callsTab = this.FindControl<SettingsPage>("CallsTab")
            ?? throw new InvalidOperationException("The call settings tab is unavailable.");
        DataContext = viewModel;
        nativeLifetime = new(this, owner, () => model.CanRevealPrivatePresentation);
        nativeLifetime.PresentationChanged += OnNativePresentationChanged;
        nativeLifetime.ActivationChanged += OnNativeActivationChanged;
        owner.Closing += OnHostClosing;
        owner.Closed += OnHostClosed;
        model.PrivacyClosureRequested += OnPrivacyClosed;
        settingsTabs.SelectionChanged += (_, _) => BindCallActivationReviewLifetime();
        settingsTabs.ResultsChanged += (_, _) =>
        {
            var id = settingsTabs.ExactSettingId;
            if (!string.Equals(preparedSettingId, id, StringComparison.Ordinal))
            {
                preparedSettingId = id;
                PrepareIndividualSetting(id);
            }
            BindCallActivationReviewLifetime();
        };
        BindSpeechComponents(viewModel);
        if (captionDisplay is not null)
        {
            this.FindControl<CaptionDisplaySelector>("CaptionDisplaySelector")!.Bind(captionDisplay);
        }
        var recovery = this.FindControl<Button>("ChooseMicrophone")
            ?? throw new InvalidOperationException("The microphone recovery control is unavailable.");
        recovery.IsEnabled = chooseMicrophone is not null;
        recovery.Click += (_, _) => chooseMicrophone?.Invoke();
        BindKnowledgeSources();
        BindUndo("ReviewSettingsUndo", RegisteredSettingSuggestionOperation.Review);
        BindUndo("ConfirmSettingsUndo", RegisteredSettingSuggestionOperation.Confirm);
        BindUndo("CancelSettingsUndo", RegisteredSettingSuggestionOperation.Cancel);
        settingsTabs.Initialize();
        settingsTabs.BindSettingIdCopy(id => HostRequestRunner.RunAsync(RequestOrigin.LocalUi,
            () => CopySettingIdAsync(id)), exception => model.ReportHostInteractionFailure(
                "Setting ID could not be copied. Clipboard contents are unconfirmed. Failure type: " + exception.GetType().Name));
    }

    private Task CopySettingIdAsync(string id)
    {
        nativeLifetime.Refresh();
        if (disposed || !IsNativePresented || !model.CanRevealPrivatePresentation
            || !settingsTabs.IsSettingVisible(id) || !ReferenceEquals(TopLevel.GetTopLevel(this), owner))
        { throw new InvalidOperationException("Copy Setting ID requires the original visible private settings host."); }
        var clipboard = owner.Clipboard ?? throw new InvalidOperationException("The native clipboard is unavailable.");
        return NativeDetailClipboard.WriteDataAsync(clipboard.SetDataAsync, id, null);
    }

    private void BindCallActivationReviewLifetime()
    {
        var available = IsNativePresented && IsActive && settingsTabs.IsSettingVisible("calls.voice-activation");
        if (callActivationAvailable == available) { return; }
        callActivationAvailable = available;
        model.BindCallVoiceActivationNativeLifetime(available);
    }

    private void OnNativePresentationChanged(object? sender, EventArgs args) => RefreshPresentation();
    private void OnNativeActivationChanged(object? sender, EventArgs args) => BindCallActivationReviewLifetime();
    private void OnHostClosing(object? sender, WindowClosingEventArgs args)
    {
        if (!PrepareClose()) { args.Cancel = true; }
    }
    private void OnHostClosed(object? sender, EventArgs args) => Dispose();
    private void OnPrivacyClosed(object? sender, EventArgs args)
    {
        IsVisible = false;
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        var presented = !disposed && nativeLifetime.IsPresented;
        if (nativePresented != presented)
        {
            nativePresented = presented;
            Volatile.Write(ref undoVisible, presented ? 1 : 0);
            Interlocked.Increment(ref undoLifetime);
            if (!presented)
            {
                model.CancelSpokenApprovalReview();
                speechComponentCancellation?.Cancel();
                localAdmissionReview = null;
            }
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        BindCallActivationReviewLifetime();
    }

    internal Func<bool> CapturePresentation() => nativeLifetime.Capture();

    internal void RefreshNativePresentation() => nativeLifetime.Refresh();

    internal bool PrepareClose()
    {
        knowledgeSources?.Close();
        return knowledgeSources is not { IsQuiescent: false };
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        nativeLifetime.PresentationChanged -= OnNativePresentationChanged;
        nativeLifetime.ActivationChanged -= OnNativeActivationChanged;
        nativeLifetime.Dispose();
        settingsTabs.Dispose();
        owner.Closing -= OnHostClosing;
        owner.Closed -= OnHostClosed;
        model.PrivacyClosureRequested -= OnPrivacyClosed;
        speechComponentCancellation?.Cancel();
        knowledgeSources?.Close();
        RefreshPresentation();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void BindUndo(string name, RegisteredSettingSuggestionOperation operation)
    {
        this.FindControl<Button>(name)!.Command = new AsyncCommand(() =>
        {
            var lifetime = Volatile.Read(ref undoLifetime);
            return HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () => model.ExecuteSettingsUndoAsync(operation,
                SecurityAuditInitiator.LocalUser, CancellationToken.None,
                () => Volatile.Read(ref undoVisible) != 0 && Volatile.Read(ref undoLifetime) == lifetime
                    && model.CanRevealPrivatePresentation));
        }, exception => model.ReportHostInteractionFailure("Settings undo failed. No completion claimed. Failure type: " + exception.GetType().Name));
    }

    public void ShowReadiness() => SelectPage(readinessTab);

    public void ShowVoiceRecovery() => SelectPage(speechAudioTab);

    public void ShowFilter(string query)
    {
        settingsTabs.ShowFilter(query);
        PrepareIndividualSetting(settingsTabs.ExactSettingId);
    }

    private void PrepareIndividualSetting(string? id)
    {
        if (id is null) { return; }
        if (VoiceCaptureTimingCommand.OptionIds.Contains(id, StringComparer.Ordinal))
        { model.SelectedVoiceTimingOption = id; }
        else if (id is SpeechTextCommand.PlacementId or SpeechTextCommand.DelayId or SpeechTextCommand.WordEmphasisId)
        {
            model.SelectedSpeechCaptionOption = id switch
            {
                SpeechTextCommand.PlacementId => SpeechCaptionOption.Placement,
                SpeechTextCommand.DelayId => SpeechCaptionOption.DismissalDelay,
                _ => SpeechCaptionOption.WordEmphasis,
            };
        }
    }

    private void SelectPage(SettingsPage page)
    {
        settingsTabs.Filter = string.Empty;
        settingsTabs.SelectedItem = page;
    }

    internal void BindNavigation(Action? editOwnedInstructions, Action? inspectSkillPackages,
        Action? inspectExactGrants, Func<Task>? reviewLocalVersion, Func<Task>? returnToVersionQuestion)
    {
        BindAction("EditOwnedInstructions", editOwnedInstructions);
        BindAction("InspectSkillPackages", inspectSkillPackages);
        BindAction("InspectExactGrants", inspectExactGrants);
        BindAction("InspectNetworkPermissionGrants", inspectExactGrants);
        BindAsync("ReviewLocalVersion", reviewLocalVersion);
        BindAsync("ReturnToVersionQuestion", returnToVersionQuestion);

        void BindAction(string name, Action? action) =>
            BindAsync(name, action is null ? null : () => { action(); return Task.CompletedTask; });

        void BindAsync(string name, Func<Task>? action)
        {
            var button = this.FindControl<Button>(name)
                ?? throw new InvalidOperationException("The Settings navigation control is unavailable: " + name);
            button.IsEnabled = action is not null;
            if (action is null) { return; }
            button.Command = new AsyncCommand(() => HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () =>
            {
                if (!IsNativePresented || !model.CanRevealPrivatePresentation)
                { throw new InvalidOperationException("Settings navigation requires the original visible private host."); }
                return action();
            }), exception => model.ReportHostInteractionFailure(
                "Settings navigation failed. No operation completion is claimed. Failure type: " + exception.GetType().Name));
        }
    }

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
        SelectPage(tab);
        if (!ReferenceEquals(settingsTabs.SelectedItem, tab))
        { throw new InvalidOperationException("The native Settings section was not selected."); }
    }

    internal void ShowPresence(Func<bool> admitted)
    {
        ShowSection(SettingsSection.Appearance);
        settingsTabs.ShowGroup(appearanceTab, "Presence");
        UpdateLayout();
        var heading = this.FindControl<TextBlock>("PresenceHeading")
            ?? throw new InvalidOperationException("The Presence settings heading is unavailable.");
        if (!admitted() || !IsNativePresented)
        { throw new OperationCanceledException("Presence settings navigation was retired before focus."); }
        heading.BringIntoView();
        if (!admitted() || !heading.Focus() || !admitted())
        { throw new InvalidOperationException("The existing Presence settings could not be focused; no setting was changed."); }
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
            if (await confirmation.ShowDialog<bool>(owner)
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
            if (await confirmation.ShowDialog<bool>(owner)
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
