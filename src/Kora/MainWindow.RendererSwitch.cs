using System.Diagnostics;

using Avalonia.Controls;

using Kora.Application.Visuals;
using Kora.Windows.Presentation;

namespace Kora;

public sealed partial class MainWindow : IPresenceWindow
{
    private readonly bool initializeHost = true;
    private bool initializationCompleted;
    private bool retiring;
    private bool resourcesDisposed;

    bool IPresenceWindow.CanReplace => initializationCompleted && !retiring && !shutdownRequested
        && !isOptionalSpeechOfferVisible && OwnedWindows.Count == 0
        && presenceInput?.IsGestureActive == false;

    PresenceWindowState IPresenceWindow.SuspendForReplacement()
    {
        var state = new PresenceWindowState(Position, WindowState,
            IsVisible && Opacity > 0 && presence.IsPresenceVisible
                && !presenceInputFailed && !nativePresenceFailed && !windowActionFailed,
            presence.IsPresenceVisible, presenceInactivity.IsScheduled ? presenceInactivity.Remaining : null,
            Stopwatch.GetTimestamp());
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
        ShowPresenceWindow();
        RefreshPresenceInput();
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
            Win32Properties.RemoveWindowStylesCallback(this, ConfigurePresenceWindowStyles);
            viewModel.WindowActionRequested -= OnWindowActionRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            presenceInput = null;
        }
        resourcesDisposed = true;
    }
}
