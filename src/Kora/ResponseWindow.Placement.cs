using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora;

public sealed partial class ResponseWindow
{
    private IWorkbenchPlacementPreferences? placementPreferences;
    private ILogger placementLogger = NullLogger.Instance;
    private readonly CancellationTokenSource placementLifetime = new();
    private WorkbenchBounds? compactBounds;
    private WorkbenchBounds? expandedBounds;
    private WindowState expandedRestoreState;
    private WindowState stateBeforeMinimize;
    private bool placementLoaded;
    private volatile bool placementLoading;
    private bool compactPlacementChanged;
    private bool expandedPlacementChanged;
    private bool applyingPlacement;
    private long placementRevision;
    private Task placementWrites = Task.CompletedTask;
    internal Task PlacementOperation { get; private set; } = Task.CompletedTask;

    private void ConfigurePlacement(IWorkbenchPlacementPreferences? preferences, ILogger? logger)
    {
        placementPreferences = preferences;
        placementLogger = logger ?? NullLogger.Instance;
        PropertyChanged += OnPlacementChanged;
    }

    private void BeginPlacementRestore()
    {
        if (placementPreferences is not null && !placementLoaded && !placementLoading)
        {
            placementLoading = true;
            PlacementOperation = LoadPlacementAsync(placementPreferences, placementRevision);
        }
    }

    private async Task LoadPlacementAsync(IWorkbenchPlacementPreferences preferences, long revision)
    {
        var token = placementLifetime.Token;
        try
        {
            var saved = await Task.Run(preferences.Load, token).ConfigureAwait(false);
            if (sessionWindowClosed) { return; }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (sessionWindowClosed) { return; }
                if (!compactPlacementChanged) { compactBounds = saved?.Compact; }
                if (!expandedPlacementChanged)
                {
                    expandedBounds = saved?.Expanded;
                    expandedRestoreState = saved?.ExpandedMaximized == true ? WindowState.Maximized : WindowState.Normal;
                }
                placementLoaded = true;
                if (placementRevision == revision && IsVisible && viewModel.CanRevealPrivatePresentation)
                { ApplyPlacement(IsExpanded); }
                else { CaptureNormalBounds(); }
            }, DispatcherPriority.Normal, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            DesktopLog.Debug(placementLogger, "Window placement read retired with its owning presentation");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ReportPlacementFailure(exception, "Window placement is unavailable. Saved geometry was not reinterpreted or overwritten.");
        }
        finally { placementLoading = false; }
    }

    private void CaptureNormalBounds()
    {
        if (applyingPlacement || !positionInitialized || WindowState != WindowState.Normal
            || Width <= 0 || Height <= 0 || !double.IsFinite(Width) || !double.IsFinite(Height))
        { return; }
        var bounds = new WorkbenchBounds(Position.X, Position.Y, Width, Height, RenderScaling);
        if (IsExpanded)
        {
            expandedBounds = bounds;
            if (placementLoading) { expandedPlacementChanged = true; }
        }
        else
        {
            compactBounds = bounds;
            if (placementLoading) { compactPlacementChanged = true; }
        }
    }

    private void CaptureModePlacement()
    {
        CaptureNormalBounds();
        if (IsExpanded && WindowState != WindowState.Minimized) { expandedRestoreState = WindowState; }
    }

    private void ApplyPlacement(bool expanded)
    {
        var bounds = expanded ? expandedBounds : compactBounds;
        applyingPlacement = true;
        try
        {
            WindowState = WindowState.Normal;
            if (bounds is not null)
            {
                Position = new PixelPoint(bounds.X, bounds.Y);
                Width = bounds.Width;
                Height = bounds.Height;
            }
            KeepOnScreen();
            if (expanded && expandedRestoreState == WindowState.Maximized)
            { WindowState = WindowState.Maximized; }
        }
        finally { applyingPlacement = false; }
    }

    private void OnPlacementChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (applyingPlacement) { return; }
        if (args.Property == WindowStateProperty)
        {
            if (args.OldValue is WindowState previous && previous == WindowState.Normal)
            {
                // State transitions can resize the client before notifications; retain the last normal bounds.
                stateBeforeMinimize = previous;
            }
            if (WindowState == WindowState.Minimized)
            {
                if (args.OldValue is WindowState oldState) { stateBeforeMinimize = oldState; }
            }
            else if (IsExpanded)
            {
                expandedRestoreState = WindowState;
                if (placementLoading) { expandedPlacementChanged = true; }
            }
            UpdateCaptionState();
            if (WindowState == WindowState.Minimized) { RetireMinimizedPresentation(); }
            FlushPlacement();
        }
        else if (args.Property == BoundsProperty)
        {
            placementRevision++;
            CaptureNormalBounds();
            if (positionInitialized && WindowState == WindowState.Normal)
            {
                positionSaveTimer.Stop();
                positionSaveTimer.Start();
            }
        }
    }

    private void RestoreMinimizedState()
    {
        if (WindowState == WindowState.Minimized)
        { WindowState = stateBeforeMinimize == WindowState.Maximized && IsExpanded ? WindowState.Maximized : WindowState.Normal; }
    }

    private void FlushPlacement()
    {
        if (placementPreferences is null || !placementLoaded || applyingPlacement) { return; }
        CaptureModePlacement();
        var snapshot = new WorkbenchPlacement(compactBounds, expandedBounds,
            expandedRestoreState == WindowState.Maximized);
        var previous = placementWrites;
        placementWrites = SavePlacementAsync(previous, placementPreferences, snapshot);
        PlacementOperation = placementWrites;
    }

    private async Task SavePlacementAsync(Task previous, IWorkbenchPlacementPreferences preferences, WorkbenchPlacement snapshot)
    {
#pragma warning disable VSTHRD003 // Joins only this window's previously enqueued atomic placement write.
        await previous.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        try { await Task.Run(() => preferences.Save(snapshot)).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ReportPlacementFailure(exception, "Window placement was not confirmed saved. No successful restart restoration is claimed.");
        }
    }

    private void ReportPlacementFailure(Exception exception, string message)
    {
        DesktopLog.Error(placementLogger, exception, "Loading or saving device-local window placement");
        if (sessionWindowClosed) { return; }
        Dispatcher.UIThread.Post(() =>
        {
            if (!sessionWindowClosed)
            { viewModel.ReportHostInteractionFailure(message + " Failure type: " + exception.GetType().Name); }
        });
    }
}
