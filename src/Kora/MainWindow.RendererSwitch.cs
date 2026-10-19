using System.Diagnostics;

using Avalonia.Controls;

using Kora.Application.Visuals;
using Kora.Core.Diagnostics;
using Kora.Windows.Presentation;

namespace Kora;

public sealed partial class MainWindow : IPresenceWindow
{
    private static readonly TimeSpan RendererFadeTimeout = TimeSpan.FromSeconds(1);
    private readonly bool initializeHost = true;
    private bool initializationCompleted;
    private bool retiring;
    private bool resourcesDisposed;
    private PresenceWindowState? rendererTransition;

    bool IPresenceWindow.CanReplace => initializationCompleted && !retiring && !shutdownRequested
        && !isOptionalSpeechOfferVisible && OwnedWindows.Count == 0
        && presenceInput?.IsGestureActive == false;

    private double PresenceFrameOpacity => nativePresence?.PresentedOpacity ?? presence.CurrentFrame.Opacity;

    private bool CanFadePresence => IsVisible && Opacity > 0 && WindowState != WindowState.Minimized
        && !shutdownRequested && !resourcesDisposed && !presenceInputFailed && !nativePresenceFailed
        && !windowActionFailed && viewModel.CanRevealPrivatePresentation && viewModel.IsPresenceDisplayEnabled
        && viewModel.State != Kora.Core.AssistantState.Hidden;

    bool IPresenceWindow.IsReplacementReady => rendererTransition is null
        || !CanFadePresence || PresenceFrameOpacity <= 0
        || Stopwatch.GetElapsedTime(rendererTransition.CapturedAt) >= RendererFadeTimeout;

    bool IPresenceWindow.PrepareForReplacement()
    {
        if (!CanFadePresence || PresenceFrameOpacity <= 0) { return true; }
        if (rendererTransition is null)
        {
            PresenceSwitchTiming.Mark(PresenceSwitchPhase.FadeOutStarted);
            rendererTransition = CaptureReplacementState();
            CancelPendingHide();
            presenceTimeoutTimer.Stop();
            presence.IsPresenceVisible = false;
            RefreshPresenceInput();
        }
        if (Stopwatch.GetElapsedTime(rendererTransition.CapturedAt) >= RendererFadeTimeout)
        {
            throw new TimeoutException("The outgoing presence renderer did not complete its visibility fade.");
        }
        return false;
    }

    void IPresenceWindow.CancelReplacement()
    {
        var state = rendererTransition;
        rendererTransition = null;
        if (state is null) { return; }
        presence.IsPresenceVisible = state.PresenceVisible && state.WasVisible && CanFadePresence;
        RefreshPresenceInput();
        if (presence.IsPresenceVisible) { RestorePresenceTimeout(state); }
    }

    private PresenceWindowState CaptureReplacementState() => new(Position, WindowState,
        IsVisible && Opacity > 0 && presence.IsPresenceVisible
            && !presenceInputFailed && !nativePresenceFailed && !windowActionFailed,
        presence.IsPresenceVisible, presenceInactivity.IsScheduled ? presenceInactivity.Remaining : null,
        Stopwatch.GetTimestamp());

    PresenceWindowState IPresenceWindow.SuspendForReplacement()
    {
        var state = rendererTransition ?? CaptureReplacementState();
        state = state with
        {
            Position = Position,
            WindowState = WindowState,
            WasVisible = state.WasVisible && IsVisible && Opacity > 0,
        };
        rendererTransition = null;
        retiring = true;
        CancelPendingHide();
        presenceTimeoutTimer.Stop();
        positionSaveTimer.Stop();
        HidePresenceWindow();
        return state;
    }

    void IPresenceWindow.CloseForReplacement()
    {
        retiring = true;
        HidePresenceWindow();
        DisposePresenceResources();
        Close();
    }

    void IPresenceWindow.RestoreAfterReplacement(PresenceWindowState state)
    {
        if (initializeHost)
        {
            throw new InvalidOperationException("A startup window cannot be restored as a renderer replacement.");
        }
        Position = state.Position;
        positionInitialized = true;
        presence.IsMotionEnabled = WindowsPresenceMotionPreference.IsAnimationEnabled();
        presence.IsPresenceVisible = state.PresenceVisible;
        EnsurePositionOnConnectedScreen();
        WindowState = state.WindowState;
        // Realize and validate the backend without revealing private presentation.
        Show();
        if (presenceInputFailed || nativePresenceFailed
            || useNativePresence && ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
        {
            throw new InvalidOperationException("The selected presence renderer or input backend is unavailable.");
        }
        initializationCompleted = true;
        if (!state.WasVisible || !viewModel.CanRevealPrivatePresentation || !viewModel.IsPresenceDisplayEnabled
            || viewModel.State == Kora.Core.AssistantState.Hidden)
        {
            HidePresenceWindow();
            return;
        }
        Opacity = 1;
        PresenceSwitchTiming.Mark(PresenceSwitchPhase.FadeInRequested);
        ShowPresenceWindow();
        RefreshPresenceInput();
        RestorePresenceTimeout(state);
    }

    private void RestorePresenceTimeout(PresenceWindowState state)
    {
        presenceTimeoutTimer.Stop();
        presenceInactivity.Stop();
        if (state.TimeoutRemaining is { } remaining && CanSchedulePresenceTimeout)
        {
            remaining -= Stopwatch.GetElapsedTime(state.CapturedAt);
            if (remaining <= TimeSpan.Zero)
            {
                presence.IsPresenceVisible = false;
                HidePresenceWindow();
                return;
            }
            presenceInactivity.Resume(remaining);
            presenceTimeoutTimer.Interval = presenceInactivity.Remaining;
            presenceTimeoutTimer.Start();
        }
        else
        {
            UpdatePresenceTimeoutEligibility();
        }
    }

    private void DisposePresenceResources()
    {
        if (resourcesDisposed) { return; }
        try
        {
            CloseNativePresence();
        }
        finally
        {
            presence.Dispose();
            presenceInputTimer.Stop();
            presenceTimeoutTimer.Stop();
            presenceInactivity.Stop();
            positionSaveTimer.Stop();
            CancelPendingHide();
            rendererTransition = null;
            Win32Properties.RemoveWindowStylesCallback(this, ConfigurePresenceWindowStyles);
            viewModel.WindowActionRequested -= OnWindowActionRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            presenceInput = null;
        }
        resourcesDisposed = true;
    }
}
