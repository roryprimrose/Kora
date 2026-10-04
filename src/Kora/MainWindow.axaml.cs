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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<MainWindow> logger;
    private readonly DispatcherTimer presenceTimeoutTimer;
    private readonly DispatcherTimer positionSaveTimer;
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
        if (viewModel.GetOptionalSpeechProviderOffer() is { } offer)
        {
            Opacity = 1;
            Show();
            var review = await ShowOptionalSpeechOfferAsync(offer);
            viewModel.AcknowledgeOptionalSpeechProviderOffer(offer, review);
        }

        if (viewModel.Dependencies.Any(status =>
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
        return await dialog.ShowDialog<bool>(this);
    }

    private async void OnWindowActionRequested(object? sender, WindowAction action)
    {
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
        if (eventArgs.PropertyName is nameof(MainViewModel.IsListening)
            or nameof(MainViewModel.PresenceTimeoutSeconds))
        {
            SchedulePresenceTimeout();
        }
        else if (eventArgs.PropertyName is nameof(MainViewModel.PresenceSizePixels))
        {
            EnsurePositionOnConnectedScreen();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
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
        if (!viewModel.IsListening || !IsVisible)
        {
            return;
        }

        DesktopLog.Debug(logger, "Hiding the inactive presence after its listening timeout");
        Hide();
    }

    private void SchedulePresenceTimeout()
    {
        presenceTimeoutTimer.Stop();
        if (!viewModel.IsListening || !IsVisible)
        {
            return;
        }

        presenceTimeoutTimer.Interval =
            TimeSpan.FromSeconds(viewModel.PresenceTimeoutSeconds);
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