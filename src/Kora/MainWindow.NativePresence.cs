using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

using Kora.Application.ViewModels;
using Kora.Application.Visuals;
using Kora.Controls;
using Kora.Windows.Presentation;


namespace Kora;

public sealed partial class MainWindow
{
    private NativePresenceHost? nativePresence;
    private bool nativePresenceFailed;

    private void InitializeNativePresence()
    {
        if (!useNativePresence)
        {
            return;
        }
        nativePresence = new NativePresenceHost(CaptureNativePresence,
            () => viewModel.RefreshSpeechPlaybackFrame(), ReportNativePresenceFailure,
            rendererLogger);
        presence.SetAnimationEnabled(false);
        presence.IsVisible = false;
        var panel = new Grid();
        Content = null;
        panel.Children.Add(presence);
        panel.Children.Add(nativePresence);
        Content = panel;
        presence.PropertyChanged += OnNativeVisualInputChanged;
        viewModel.PropertyChanged += OnNativeViewModelChanged;
        viewModel.PrivacyClosureRequested += OnNativePrivacyClosure;
        DesktopLog.Information(logger, "Native presence presenter selected; the Avalonia presenter remains the default");
    }

    private PresenceRenderInputs? CaptureNativePresence()
    {
        if (nativePresenceFailed || !IsVisible || WindowState == WindowState.Minimized || Opacity <= 0
            || !viewModel.CanRevealPrivatePresentation || !presence.IsPresentationEligible
            || !viewModel.IsPresenceDisplayEnabled || nativePresence is null
            || nativePresence.Bounds.Width <= 0 || nativePresence.Bounds.Height <= 0)
        {
            return null;
        }
        return new PresenceRenderInputs(presence.State, presence.IsSpeaking, presence.SpeechOutputLevel,
            presence.IsSpeechScalingEnabled, presence.SpeechScaleAmountPercent, presence.DotDensityPercent,
            presence.DotSizePercent, presence.MovementSpeedPercent, ActualThemeVariant == ThemeVariant.Light,
            !presence.IsMotionEnabled, presence.IsPresenceVisible, nativePresence.Bounds.Width, nativePresence.Bounds.Height, RenderScaling);
    }

    private void RefreshNativePresence() => nativePresence?.Refresh(viewModel.IsSpeaking);

    private void OnNativeVisualInputChanged(object? sender, AvaloniaPropertyChangedEventArgs args) => RefreshNativePresence();

    private void OnNativeViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.IsSpeaking)
            or nameof(MainViewModel.CanRevealPrivatePresentation) or nameof(MainViewModel.IsPresenceDisplayEnabled))
        {
            RefreshNativePresence();
        }
    }

    private void OnNativePrivacyClosure(object? sender, EventArgs args)
    {
        nativePresence?.Suspend();
        RefreshPresenceInput();
    }

    private void ReportNativePresenceFailure(Exception exception)
    {
        nativePresenceFailed = true;
        nativePresence?.Suspend();
        DesktopLog.Error(logger, exception, "Presenting the opt-in native presence");
        viewModel.ReportPresenceRendererStatus("Native renderer unavailable. Select Avalonia in Settings; no automatic fallback was performed.");
        HidePresenceWindow();
        viewModel.ReportHostInteractionFailure(
            "The native presence renderer is unavailable. Other presentation and audio services remain independent. "
            + "Select Avalonia in Settings to replace the presence window, or exit normally.");
    }

    private void CloseNativePresence()
    {
        presence.PropertyChanged -= OnNativeVisualInputChanged;
        viewModel.PropertyChanged -= OnNativeViewModelChanged;
        viewModel.PrivacyClosureRequested -= OnNativePrivacyClosure;
        nativePresence?.Dispose();
    }
}