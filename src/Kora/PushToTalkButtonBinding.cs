using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Kora.Application.ViewModels;

namespace Kora;

internal sealed class PushToTalkButtonBinding : IDisposable
{
    private readonly Window window;
    private readonly Button button;
    private readonly MainViewModel main;
    private bool available = true;
    private bool held;
    private bool disposed;

    internal PushToTalkButtonBinding(Window window, Button button, MainViewModel main)
    {
        this.window = window;
        this.button = button;
        this.main = main;
        button.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.PointerCaptureLost += OnCaptureLost;
        button.LostFocus += OnFocusLost;
        window.Deactivated += OnUnavailable;
        window.Closed += OnClosed;
        window.PropertyChanged += OnWindowChanged;
        main.PropertyChanged += OnMainChanged;
        Refresh();
    }

    internal void SetAvailable(bool value)
    {
        available = value;
        Refresh();
    }

    private void Refresh()
    {
        if (disposed) { return; }
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Refresh); return; }
        // The held control must remain enabled to receive release while capture makes CanPushToTalk false.
        button.IsEnabled = available && (held || main.CanPushToTalk);
    }

    private bool CanBegin() => !disposed && available && !held && main.BeginPushToTalkCommand.CanExecute(null);

    private void Begin()
    {
        held = true;
        Refresh();
        main.BeginPushToTalkCommand.Execute(null);
    }

    internal void Release()
    {
        if (!held) { return; }
        held = false;
        main.EndPushToTalkCommand.Execute(null);
        Refresh();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(button).Properties.IsLeftButtonPressed || !CanBegin()) { return; }
        args.Handled = true;
        args.Pointer.Capture(button);
        Begin();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (!held) { return; }
        args.Handled = true;
        Release();
        args.Pointer.Capture(null);
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Space or Key.Enter)) { return; }
        if (held) { args.Handled = true; return; }
        if (!CanBegin()) { return; }
        args.Handled = true;
        Begin();
    }

    private void OnKeyUp(object? sender, KeyEventArgs args)
    {
        if (!held || args.Key is not (Key.Space or Key.Enter)) { return; }
        args.Handled = true;
        Release();
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs args) => Release();
    private void OnFocusLost(object? sender, FocusChangedEventArgs args) => Release();
    private void OnUnavailable(object? sender, EventArgs args) => Release();
    private void OnClosed(object? sender, EventArgs args) => Dispose();
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty && !window.IsVisible) { Release(); }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.CanPushToTalk))
        { Refresh(); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        Release();
        disposed = true;
        button.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        button.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        button.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        button.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        button.PointerCaptureLost -= OnCaptureLost;
        button.LostFocus -= OnFocusLost;
        window.Deactivated -= OnUnavailable;
        window.Closed -= OnClosed;
        window.PropertyChanged -= OnWindowChanged;
        main.PropertyChanged -= OnMainChanged;
    }
}
