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
using Kora.Windows.Presentation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<MainWindow> logger;
    private readonly DispatcherTimer presenceTimeoutTimer;
    private readonly PresentationInactivityTimeout presenceInactivity = new();
    private readonly DispatcherTimer positionSaveTimer;
    private readonly DispatcherTimer presenceInputTimer;
    private WindowsPresenceWindowInput? presenceInput;
    private bool presenceInputFailed;
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
    {
        this.viewModel = viewModel;
        this.logger = logger;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        ShowInTaskbar = false;
        Opacity = 0;
        Win32Properties.AddWindowStylesCallback(this, ConfigurePresenceWindowStyles);
        presenceInput = new WindowsPresenceWindowInput(
            TryGetPlatformHandle()?.Handle
            ?? throw new InvalidOperationException("The presence window has no Windows handle."));
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
        PointerPressed += OnPointerPressed;
        PositionChanged += OnPositionChanged;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private (uint Style, uint ExtendedStyle) ConfigurePresenceWindowStyles(uint style, uint extendedStyle) =>
        (style, WindowsPresenceWindowInput.GetExtendedStyle(extendedStyle, presenceInput?.InterceptsMouse == true));

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
                IsVisible && viewModel.CanRevealPrivatePresentation);
        }
        catch (Win32Exception exception)
        {
            presenceInputFailed = true;
            presenceInputTimer.Stop();
            IsHitTestVisible = false;
            DesktopLog.Error(logger, exception, "Updating presence mouse routing");
            Hide();
            viewModel.ReportPresenceInputFailure(exception.Message);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
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

    private async void OnLoaded(object? sender, RoutedEventArgs eventArgs)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        DesktopLog.Information(logger, "Main window loaded");
        await viewModel.InitializeAsync();
        RestorePosition();
        if (!viewModel.CanRevealPrivatePresentation)
        {
            Hide();
            return;
        }
        if (viewModel.GetOptionalSpeechProviderOffer() is { } offer)
        {
            Opacity = 1;
            Show();
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
            Hide();
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
        if (action is WindowAction.Show or WindowAction.ShowPresence
            && (presenceInputFailed || !viewModel.CanRevealPrivatePresentation))
        {
            return;
        }
        switch (action)
        {
            case WindowAction.ShowPresence:
            case WindowAction.Show:
                DesktopLog.Debug(logger, "Showing the presence");
                CancelPendingHide();
                EnsurePositionOnConnectedScreen();
                Show();
                SchedulePresenceTimeout();
                break;
            case WindowAction.Hide:
                DesktopLog.Debug(logger, "Hiding the main window after its transition");
                CancelPendingHide();
                presenceTimeoutTimer.Stop();
                presenceInactivity.Stop();
                var hideRequest = new CancellationTokenSource();
                pendingHide = hideRequest;
                try
                {
                    await Task.Delay(
                        PresenceAnimation.VisibilityTransitionDuration + PresenceAnimation.FrameInterval,
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

    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        positionSaveTimer.Stop();
        if (positionInitialized)
        {
            _ = viewModel.SetPresencePosition(
                new PresencePosition(Position.X, Position.Y));
        }

        if (shutdownRequested
            || eventArgs.CloseReason is WindowCloseReason.ApplicationShutdown
                or WindowCloseReason.OSShutdown)
        {
            return;
        }

        DesktopLog.Debug(logger, "Hiding the main window instead of closing the background application");
        eventArgs.Cancel = true;
        CancelPendingHide();
        viewModel.HideApplication();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainViewModel.PresenceTimeoutSeconds))
        {
            SchedulePresenceTimeout();
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
        positionSaveTimer.Stop();
        _ = viewModel.SetPresencePosition(
            new PresencePosition(Position.X, Position.Y));
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

        DesktopLog.Debug(logger, "Hiding the presence after its inactivity timeout");
        Hide();
    }

    private bool CanSchedulePresenceTimeout =>
        IsVisible && !isOptionalSpeechOfferVisible && viewModel.CanAutoHidePresence;

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