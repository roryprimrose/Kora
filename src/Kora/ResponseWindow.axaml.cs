using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Kora.Application.ViewModels;
using Kora.Application.Visuals;
using Kora.Core.Configuration;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class ResponseWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer responseTimeoutTimer;
    private readonly PresentationInactivityTimeout responseInactivity = new();
    private readonly DispatcherTimer positionSaveTimer;
    private bool positionInitialized;

    public ResponseWindow()
        : this(App.Services.GetRequiredService<MainViewModel>())
    {
    }

    public ResponseWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        responseTimeoutTimer = new DispatcherTimer();
        responseTimeoutTimer.Tick += OnResponseTimeout;
        positionSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        positionSaveTimer.Tick += OnPositionSaveTimer;
        Opened += OnOpened;
        Closed += OnClosed;
        PositionChanged += OnPositionChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        AddHandler(PointerMovedEvent, OnMouseActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnMouseActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnMouseActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnMouseActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerPressed += OnInteraction;
        KeyDown += OnInteraction;
    }

    private async void OnDismissClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (!await viewModel.RejectPendingModelActionAsync())
        {
            return;
        }

        await viewModel.RejectPendingGrantChangeAsync();
        await viewModel.CancelModelQuestionAsync();
        viewModel.HideApplication();
    }

    private async void OnCancelTaskClicked(object? sender, RoutedEventArgs eventArgs) =>
        await viewModel.CancelCurrentTaskAsync();

    private async void OnQuestionChoiceClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: ModelQuestionChoice choice })
        {
            await viewModel.SelectModelQuestionChoiceAsync(choice);
        }
    }

    private async void OnResponseActionClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: ResponseAction action })
        {
            await viewModel.ExecuteResponseActionAsync(action);
        }
    }

    private async void OnCancelQuestionClicked(object? sender, RoutedEventArgs eventArgs) =>
        await viewModel.CancelModelQuestionAsync();

    private void OnArtifactCommandClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: ArtifactCommandOption option })
        {
            viewModel.ApplyArtifactCommandOption(option);
            this.FindControl<TextBox>("CommandInput")?.Focus();
        }
    }

    private void OnCommandTextKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        var list = this.FindControl<ListBox>("ArtifactCommandList");
        if (viewModel.IsArtifactCommandDropdownVisible && eventArgs.Key is Key.Down or Key.Up)
        {
            var count = viewModel.ArtifactCommandOptions.Count;
            var current = list?.SelectedIndex ?? -1;
            var next = eventArgs.Key == Key.Down
                ? Math.Min(current + 1, count - 1)
                : Math.Max(current - 1, 0);
            if (list is not null)
            {
                list.SelectedIndex = next;
                if (list.SelectedItem is { } item)
                {
                    list.ScrollIntoView(item);
                }
            }
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.Key == Key.Escape && viewModel.IsArtifactCommandDropdownVisible)
        {
            viewModel.DismissArtifactCommandOptions();
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.Key == Key.Enter
            && list?.SelectedItem is ArtifactCommandOption selected)
        {
            viewModel.ApplyArtifactCommandOption(selected);
            list.SelectedIndex = -1;
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.Key != Key.Enter || !viewModel.RunTypedCommand.CanExecute(null))
        {
            return;
        }

        eventArgs.Handled = true;
        viewModel.NotifyPresenceInteraction();
        viewModel.RunTypedCommand.Execute(null);
    }

    private async void OnCancelTaskKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.S && eventArgs.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            viewModel.ShowSessions();
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.Key != Key.Escape || !viewModel.IsCancelTaskVisible)
        {
            return;
        }

        eventArgs.Handled = true;
        viewModel.NotifyPresenceInteraction();
        await viewModel.CancelCurrentTaskAsync();
    }

    private void OnInteraction(object? sender, PointerPressedEventArgs eventArgs)
    {
        viewModel.NotifyPresenceInteraction();
        RestartResponseTimeout();
    }

    private void OnMouseActivity(object? sender, PointerEventArgs eventArgs)
    {
        if (eventArgs.Pointer.Type == PointerType.Mouse)
        {
            RestartResponseTimeout();
        }
    }

    private void OnInteraction(object? sender, KeyEventArgs eventArgs)
    {
        viewModel.NotifyPresenceInteraction();
        RestartResponseTimeout();
    }

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(eventArgs);
        }
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        RestorePosition();
        RestartResponseTimeout();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        responseTimeoutTimer.Stop();
        responseInactivity.Stop();
        positionSaveTimer.Stop();
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.Equals(eventArgs.PropertyName, nameof(MainViewModel.IsVisualResponseVisible), StringComparison.Ordinal)
            && !viewModel.IsVisualResponseVisible)
        {
            responseTimeoutTimer.Stop();
            responseInactivity.Stop();
            Hide();
            return;
        }

        if (string.Equals(
                eventArgs.PropertyName,
                nameof(MainViewModel.IsResponseAlwaysVisible),
                StringComparison.Ordinal)
            || string.Equals(
                eventArgs.PropertyName,
                nameof(MainViewModel.ResponseTimeoutSeconds),
                StringComparison.Ordinal)
            || string.Equals(
                eventArgs.PropertyName,
                nameof(MainViewModel.IsResponseInteractionPending),
                StringComparison.Ordinal)
            || string.Equals(
                eventArgs.PropertyName,
                nameof(MainViewModel.HasResponseActions),
                StringComparison.Ordinal))
        {
            RestartResponseTimeout();
        }
    }

    private void OnResponseTimeout(object? sender, EventArgs eventArgs)
    {
        responseTimeoutTimer.Stop();
        if (!IsVisible || !responseInactivity.IsScheduled)
        {
            responseInactivity.Stop();
            return;
        }
        if (!viewModel.IsResponseAlwaysVisible && !viewModel.IsResponseInteractionPending
            && !viewModel.HasResponseActions)
        {
            if (!responseInactivity.TryExpire())
            {
                responseTimeoutTimer.Interval = responseInactivity.Remaining;
                responseTimeoutTimer.Start();
                return;
            }
            Hide();
        }
        else
        {
            responseInactivity.Stop();
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
        _ = viewModel.SetResponseWindowPosition(
            new ResponseWindowPosition(Position.X, Position.Y));
    }

    internal void ShowResponse()
    {
        if (!IsVisible)
        {
            Show();
        }

        RestartResponseTimeout();
    }

    internal void HideResponse()
    {
        responseTimeoutTimer.Stop();
        responseInactivity.Stop();
        Hide();
    }

    private void RestartResponseTimeout()
    {
        responseTimeoutTimer.Stop();
        responseInactivity.Stop();
        if (!IsVisible || viewModel.IsResponseAlwaysVisible
            || viewModel.IsResponseInteractionPending
            || viewModel.HasResponseActions)
        {
            return;
        }

        responseInactivity.Restart(viewModel.ResponseTimeoutSeconds);
        responseTimeoutTimer.Interval = responseInactivity.Remaining;
        responseTimeoutTimer.Start();
    }

    private void RestorePosition()
    {
        positionInitialized = false;
        var savedPosition = viewModel.ResponseWindowPosition;
        if (savedPosition is not null
            && IsPositionOnConnectedScreen(savedPosition))
        {
            Position = new PixelPoint(savedPosition.X, savedPosition.Y);
        }
        else
        {
            PositionNearPresence();
        }

        positionInitialized = true;
    }

    private bool IsPositionOnConnectedScreen(ResponseWindowPosition position)
    {
        var point = new PixelPoint(position.X, position.Y);
        return Screens.All.Any(screen => screen.WorkingArea.Contains(point));
    }

    private void PositionNearPresence()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        const int margin = 24;
        const int presenceHeight = 360;
        var scale = RenderScaling;
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        var scaledMargin = (int)Math.Ceiling(margin * scale);
        var scaledPresenceHeight = (int)Math.Ceiling(presenceHeight * scale);
        Position = new PixelPoint(
            screen.WorkingArea.Right - width - scaledMargin,
            Math.Max(
                screen.WorkingArea.Y + scaledMargin,
                screen.WorkingArea.Bottom - height - scaledPresenceHeight - scaledMargin));
    }
}