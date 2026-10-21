using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Kora.Application.ViewModels;
using Kora.Application.Visuals;
using Kora.Core.Configuration;
using Kora.Core.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Application.Hosting;
using Kora.Controls;
using Kora.Windows.Presentation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<MainWindow> logger;
    private readonly ILogger<PresenceRenderWorker> rendererLogger;
    private readonly bool useNativePresence;
    private readonly PresenceControl presence;
    private readonly DispatcherTimer presenceTimeoutTimer;
    private readonly PresentationInactivityTimeout presenceInactivity = new();
    private readonly DispatcherTimer positionSaveTimer;
    private readonly DispatcherTimer presenceInputTimer;
    private WindowsPresenceWindowInput? presenceInput;
    private bool presenceInputFailed;
    private bool windowActionFailed;
    private bool isOptionalSpeechOfferVisible;
    private CancellationTokenSource? pendingHide;
    private bool initialized;
    private bool positionInitialized;
    private bool shutdownRequested;

    public MainWindow()
        : this(
            App.Services.GetRequiredService<MainViewModel>(),
            App.Services.GetRequiredService<ILogger<MainWindow>>())
    {
    }

    public MainWindow(MainViewModel viewModel, ILogger<MainWindow> logger)
        : this(viewModel, logger, viewModel.IsNativePresenceRenderer, true,
            App.Services.GetRequiredService<ILogger<PresenceRenderWorker>>())
    {
    }

    internal MainWindow(MainViewModel viewModel, ILogger<MainWindow> logger, bool useNative, bool initializeHost,
        ILogger<PresenceRenderWorker> rendererLogger)
    {
        this.viewModel = viewModel;
        this.logger = logger;
        this.rendererLogger = rendererLogger;
        this.initializeHost = initializeHost;
        useNativePresence = useNative;
        AvaloniaXamlLoader.Load(this);
        presence = this.FindControl<PresenceControl>("Presence")
            ?? throw new InvalidOperationException("The presence control was not loaded.");
        DataContext = viewModel;
        presence.BindPresentationEligibility(() => viewModel.CanRevealPrivatePresentation);
        ShowInTaskbar = false;
        Opacity = 0;
        presenceInputTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Input,
            OnPresenceInputTick);
        presenceTimeoutTimer = new DispatcherTimer();
        presenceTimeoutTimer.Tick += OnPresenceTimeout;
        positionSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        positionSaveTimer.Tick += OnPositionSaveTimer;
        viewModel.WindowActionRequested += OnWindowActionRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        AddHandler(PointerMovedEvent, OnMouseActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnMouseActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnMouseActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnMouseActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerPressed += OnPointerPressed;
        PositionChanged += OnPositionChanged;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        try
        {
            Win32Properties.AddWindowStylesCallback(this, ConfigurePresenceWindowStyles);
            var handle = TryGetPlatformHandle()?.Handle
                ?? throw new InvalidOperationException("The presence window has no Windows handle.");
            presenceInput = useNativePresence
                ? WindowsPresenceWindowInput.CreateForNativeRenderer(handle)
                : new WindowsPresenceWindowInput(handle);
            InitializeNativePresence();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            try { ((IPresenceWindow)this).CloseForReplacement(); }
            catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
            {
                throw new AggregateException("Presence construction and cleanup failed.", failure, cleanup);
            }
            throw;
        }
    }

    private (uint Style, uint ExtendedStyle) ConfigurePresenceWindowStyles(uint style, uint extendedStyle) =>
        useNativePresence ? (style, extendedStyle | 0x08000000)
            : (style, WindowsPresenceWindowInput.GetExtendedStyle(extendedStyle, presenceInput?.InterceptsMouse == true));

    private void OnPresenceInputTick(object? sender, EventArgs eventArgs) => RefreshPresenceInput();

    private void RefreshPresenceInput()
    {
        if (presenceInputFailed || presenceInput is null)
        {
            return;
        }

        try
        {
            IsHitTestVisible = presenceInput.Refresh(
                IsVisible && viewModel.CanRevealPrivatePresentation
                && viewModel.State != Kora.Core.AssistantState.Hidden
                && (nativePresence?.CanInteract ?? presence.CanInteract));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            presenceInputFailed = true;
            presenceInputTimer.Stop();
            IsHitTestVisible = false;
            DesktopLog.Error(logger, exception, "Updating presence mouse routing");
            HidePresenceWindow();
            viewModel.ReportPresenceInputFailure(exception.Message);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (nativePresence is not null)
        {
            RefreshNativePresence();
        }
        if (change.Property == IsVisibleProperty && presenceInput is not null)
        {
            RefreshPresenceInput();
            if (IsVisible && !presenceInputFailed)
            {
                presenceInputTimer.Start();
            }
            else
            {
                presenceInputTimer.Stop();
            }
            UpdatePresenceTimeoutEligibility();
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        DisposePresenceResources();
    }

    private async void OnLoaded(object? sender, RoutedEventArgs eventArgs)
    {
        if (!initializeHost) { return; }
        try
        {
            await HostRequestRunner.RunAsync(RequestOrigin.HostSystem, InitializeWindowAsync,
                HostActivityLayer.Desktop, HostOperation.Startup);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            HostRequestRunner.Run(RequestOrigin.HostSystem, () =>
            {
                DesktopLog.Error(logger, exception, "Initializing the main window");
                windowActionFailed = true;
                viewModel.ReportHostInteractionFailure("Desktop initialization failed. " + exception.Message);
                presenceInputTimer.Stop();
                IsHitTestVisible = false;
                Hide();
            }, HostActivityLayer.Desktop, HostOperation.Presentation);
        }
        finally
        {
            initializationCompleted = true;
            if (!presenceInputFailed && !nativePresenceFailed && !windowActionFailed)
            {
                viewModel.ReportPresenceRendererStatus(useNativePresence
                    ? "Experimental animation mode is active." : "Standard animation mode is active.");
            }
        }
    }

    private async Task InitializeWindowAsync()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        presence.IsMotionEnabled = WindowsPresenceMotionPreference.IsAnimationEnabled();
        DesktopLog.Information(logger, "Main window loaded");
        await viewModel.InitializeAsync();
        RestorePosition();
        if (useNativePresence && ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
        {
            ReportNativePresenceFailure(new InvalidOperationException("A transparent non-layered native presence shell is unavailable."));
            return;
        }
        if (!viewModel.CanRevealPrivatePresentation)
        {
            HidePresenceWindow();
            return;
        }
        if (viewModel.GetOptionalSpeechProviderOffer() is { } offer)
        {
            Opacity = 1;
            ShowPresenceWindow();
            var review = await ShowOptionalSpeechOfferAsync(offer);
            viewModel.AcknowledgeOptionalSpeechProviderOffer(offer, review);
        }

        if (!viewModel.NeedsVoiceConsent && viewModel.Dependencies.Any(status =>
            string.Equals(status.Id, "local.inference", StringComparison.Ordinal)
            && status.Readiness != Kora.Core.Dependencies.DependencyReadiness.Ready))
        {
            viewModel.ShowReadiness();
        }

        if (viewModel.IsListening)
        {
            DesktopLog.Debug(logger, "Hiding the main window after successful background startup");
            viewModel.HidePresentation();
            HidePresenceWindow();
        }
        else if (!viewModel.IsPresenceDisplayEnabled)
        {
            DesktopLog.Debug(logger, "Keeping the disabled presence hidden after startup");
            HidePresenceWindow();
        }
        else
        {
            Opacity = 1;
            viewModel.ShowPresentation();
        }

        Opacity = 1;
    }

    private void OnPresenceFrameUpdating(object? sender, EventArgs eventArgs) =>
        viewModel.RefreshSpeechPlaybackFrame();

    private async Task<bool> ShowOptionalSpeechOfferAsync(OptionalSpeechProviderOffer offer)
    {
        var review = new Button { Content = "Review in Settings" };
        var decline = new Button { Content = "Not now" };
        var dialog = new Window
        {
            Title = offer.Title,
            Width = 490,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = offer.Detail,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children = { review, decline },
                    },
                },
            },
        };
        review.Click += (_, _) => dialog.Close(true);
        decline.Click += (_, _) => dialog.Close(false);
        isOptionalSpeechOfferVisible = true;
        UpdatePresenceTimeoutEligibility();
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            isOptionalSpeechOfferVisible = false;
            UpdatePresenceTimeoutEligibility();
        }
    }

    private async void OnWindowActionRequested(object? sender, WindowAction action)
    {
        if (retiring) { return; }
        _ = await RunWindowActionAsync(() => ApplyWindowActionAsync(action), ReportWindowActionFailure);
    }

    private void ReportWindowActionFailure(Exception exception)
    {
        windowActionFailed = true;
        DesktopLog.Error(logger, exception, "Applying a desktop window action");
        viewModel.ReportHostInteractionFailure(
            "Desktop presentation failed. Use the native tray controls to exit and restart Kora. " + exception.Message);
    }

    internal static async Task<HostOperationOutcome> RunWindowActionAsync(
        Func<Task> route, Action<Exception> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(reportFailure);
        using var activity = HostActivity.BeginOperation(
            HostActivityLayer.Desktop, HostOperation.Presentation, RequestOrigin.HostSystem);
        try
        {
            await route();
            activity.Complete(HostOperationOutcome.Completed);
            return HostOperationOutcome.Completed;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            return HostOperationOutcome.Cancelled;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            reportFailure(exception);
            return HostOperationOutcome.Failed;
        }
    }

    private async Task ApplyWindowActionAsync(WindowAction action)
    {
        if (action is WindowAction.Show or WindowAction.ShowPresence
            && (presenceInputFailed
                || nativePresenceFailed
                || windowActionFailed
                || !viewModel.CanRevealPrivatePresentation
                || !viewModel.IsPresenceDisplayEnabled))
        {
            return;
        }
        rendererTransition = null;
        switch (action)
        {
            case WindowAction.ShowPresence:
            case WindowAction.Show:
                DesktopLog.Debug(logger, "Showing the presence");
                CancelPendingHide();
                presence.IsMotionEnabled = WindowsPresenceMotionPreference.IsAnimationEnabled();
                EnsurePositionOnConnectedScreen();
                ShowPresenceWindow();
                presence.IsPresenceVisible = true;
                RefreshPresenceInput();
                SchedulePresenceTimeout();
                break;
            case WindowAction.Hide:
                DesktopLog.Debug(logger, "Hiding the main window after its transition");
                presenceTimeoutTimer.Stop();
                presenceInactivity.Stop();
                await HidePresenceAfterTransitionAsync();
                break;
            case WindowAction.Close:
                DesktopLog.Information(logger, "Shutting down the desktop application");
                CancelPendingHide();
                presenceTimeoutTimer.Stop();
                presenceInactivity.Stop();
                shutdownRequested = true;
                if (Avalonia.Application.Current?.ApplicationLifetime
                    is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                else
                {
                    Close();
                }
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

    private async Task HidePresenceAfterTransitionAsync()
    {
        CancelPendingHide();
        presence.IsPresenceVisible = false;
        RefreshPresenceInput();
        var hideRequest = new CancellationTokenSource();
        pendingHide = hideRequest;
        try
        {
            await Task.Delay(
                PresenceAnimation.VisibilityTransitionDuration + PresenceAnimation.FrameInterval,
                hideRequest.Token);
            HidePresenceWindow();
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
    }

    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (retiring) { return; }
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Presentation,
            RequestOrigin.LocalUi);
        positionSaveTimer.Stop();
        if (positionInitialized)
        {
            viewModel.QueuePresencePosition(
                new PresencePosition(Position.X, Position.Y));
        }

        if (shutdownRequested
            || eventArgs.CloseReason is WindowCloseReason.ApplicationShutdown
                or WindowCloseReason.OSShutdown)
        {
            activity.Complete(HostOperationOutcome.Completed);
            return;
        }

        DesktopLog.Debug(logger, "Hiding the main window instead of closing the background application");
        eventArgs.Cancel = true;
        CancelPendingHide();
        viewModel.HideApplication();
        activity.Complete(HostOperationOutcome.Completed);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        presence.IsPresentationEligible = viewModel.CanRevealPrivatePresentation;
        if (eventArgs.PropertyName is nameof(MainViewModel.IsSpeaking))
        {
            viewModel.RefreshSpeechPlaybackFrame();
        }
        else if (eventArgs.PropertyName is nameof(MainViewModel.State))
        {
            RefreshPresenceInput();
        }
        if (eventArgs.PropertyName is nameof(MainViewModel.PresenceTimeoutSeconds))
        {
            SchedulePresenceTimeout();
        }
        else if (eventArgs.PropertyName is nameof(MainViewModel.IsPresenceDisplayEnabled))
        {
            if (viewModel.IsPresenceDisplayEnabled)
            {
                _ = RunWindowActionAsync(
                    () => ApplyWindowActionAsync(WindowAction.ShowPresence),
                    ReportWindowActionFailure);
            }
            else
            {
                presenceTimeoutTimer.Stop();
                presenceInactivity.Stop();
                CancelPendingHide();
                presence.IsPresenceVisible = false;
                HidePresenceWindow();
            }
        }
        else if (eventArgs.PropertyName is nameof(MainViewModel.IsListening)
            or nameof(MainViewModel.State)
            or nameof(MainViewModel.IsBusy)
            or nameof(MainViewModel.IsSpeaking)
            or nameof(MainViewModel.IsCancelTaskVisible)
            or nameof(MainViewModel.IsLocalModelSetupActive)
            or nameof(MainViewModel.IsPowerShellSetupActive)
            or nameof(MainViewModel.IsResponseInteractionPending)
            or nameof(MainViewModel.HasResponseActions)
            or nameof(MainViewModel.IsGrantEditorVisible))
        {
            UpdatePresenceTimeoutEligibility();
        }
        else if (eventArgs.PropertyName is nameof(MainViewModel.PresenceSizePixels))
        {
            EnsurePositionOnConnectedScreen();
        }
    }

    private void ShowPresenceWindow()
    {
        presence.SetAnimationEnabled(nativePresence is null);
        Opacity = 1;
        Show();
        if (!presenceInputFailed)
        {
            presenceInputTimer.Start();
        }
    }

    private void HidePresenceWindow()
    {
        nativePresence?.Suspend();
        presenceInputTimer.Stop();
        IsHitTestVisible = false;
        presence.SetAnimationEnabled(false);
        try
        {
            if (useNativePresence && !presenceInputFailed) { presenceInput?.Refresh(false); }
        }
        finally
        {
            Hide();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (presenceInput?.InterceptsMouse == true
            && viewModel.CanRevealPrivatePresentation
            && eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control)
            && eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            eventArgs.Handled = true;
            viewModel.NotifyPresenceInteraction();
            BeginMoveDrag(eventArgs);
        }
    }

    private void OnMouseActivity(object? sender, PointerEventArgs eventArgs)
    {
        if (eventArgs.Pointer.Type == PointerType.Mouse)
        {
            SchedulePresenceTimeout();
        }
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs eventArgs)
    {
        if (!positionInitialized)
        {
            return;
        }

        positionSaveTimer.Stop();
        positionSaveTimer.Start();
        if (presenceInput?.InterceptsMouse == true)
        {
            SchedulePresenceTimeout();
        }
    }

    private void OnPositionSaveTimer(object? sender, EventArgs eventArgs)
    {
        HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
        {
            positionSaveTimer.Stop();
            viewModel.QueuePresencePosition(
                new PresencePosition(Position.X, Position.Y));
        }, HostActivityLayer.Desktop, HostOperation.Policy);
    }

    private void OnPresenceTimeout(object? sender, EventArgs eventArgs)
    {
        presenceTimeoutTimer.Stop();
        if (!CanSchedulePresenceTimeout || !presenceInactivity.IsScheduled)
        {
            presenceInactivity.Stop();
            return;
        }

        if (!presenceInactivity.TryExpire())
        {
            presenceTimeoutTimer.Interval = presenceInactivity.Remaining;
            presenceTimeoutTimer.Start();
            return;
        }

        _ = RunWindowActionAsync(async () =>
        {
            DesktopLog.Debug(logger, "Fading the presence after its inactivity timeout");
            await HidePresenceAfterTransitionAsync();
        }, ReportWindowActionFailure);
    }

    private bool CanSchedulePresenceTimeout =>
        rendererTransition is null && IsVisible && !isOptionalSpeechOfferVisible && viewModel.CanAutoHidePresence;

    private void UpdatePresenceTimeoutEligibility()
    {
        if (!CanSchedulePresenceTimeout)
        {
            presenceTimeoutTimer.Stop();
            presenceInactivity.Stop();
        }
        else if (!presenceInactivity.IsScheduled)
        {
            SchedulePresenceTimeout();
        }
    }

    private void SchedulePresenceTimeout()
    {
        presenceTimeoutTimer.Stop();
        presenceInactivity.Stop();
        if (!CanSchedulePresenceTimeout)
        {
            return;
        }

        presenceInactivity.Restart(viewModel.PresenceTimeoutSeconds);
        presenceTimeoutTimer.Interval = presenceInactivity.Remaining;
        presenceTimeoutTimer.Start();
    }

    private void PositionAtWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        const int margin = 24;
        var scale = RenderScaling;
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        var scaledMargin = (int)Math.Ceiling(margin * scale);
        Position = new PixelPoint(
            screen.WorkingArea.Right - width - scaledMargin,
            screen.WorkingArea.Bottom - height - scaledMargin);
    }

    private void RestorePosition()
    {
        positionInitialized = false;
        var savedPosition = viewModel.PresencePosition;
        if (savedPosition is not null
            && IsPositionOnConnectedScreen(savedPosition))
        {
            Position = new PixelPoint(savedPosition.X, savedPosition.Y);
        }
        else
        {
            PositionAtWorkingArea();
        }

        positionInitialized = true;
    }

    private void EnsurePositionOnConnectedScreen()
    {
        var position = new PresencePosition(Position.X, Position.Y);
        if (!IsPositionOnConnectedScreen(position))
        {
            PositionAtWorkingArea();
        }
    }

    private bool IsPositionOnConnectedScreen(PresencePosition position)
    {
        var point = new PixelPoint(position.X, position.Y);
        return Screens.All.Any(screen => screen.WorkingArea.Contains(point));
    }
}