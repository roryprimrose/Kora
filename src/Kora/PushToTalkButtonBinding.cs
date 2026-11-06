using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Kora.Application.ViewModels;
using Kora.Core.Voice;

namespace Kora;

internal sealed class PushToTalkButtonBinding : IDisposable
{
    private readonly Window window;
    private readonly Button button;
    private readonly MainViewModel main;
    private bool available = true;
    private bool held;
    private bool disposed;
    private bool releasing;
    private Key? heldKey;
    private bool shortcutHeld;
    private IPointer? heldPointer;
    private readonly HashSet<Key> pressedKeys = [];
    private Task opening = Task.CompletedTask;
    private Task release = Task.CompletedTask;
#pragma warning disable VSTHRD003 // Exposes only this UI binding's exact owned release operation for lifecycle joining.
    internal Task Quiescence => release;
#pragma warning restore VSTHRD003

    internal PushToTalkButtonBinding(Window window, Button button, MainViewModel main)
    {
        this.window = window;
        this.button = button;
        this.main = main;
        button.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.GotFocusEvent, OnFocusChanged, RoutingStrategies.Bubble, handledEventsToo: true);
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
        if (!value) { Release(); }
        Refresh();
    }

    private void Refresh()
    {
        if (disposed) { return; }
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Refresh); return; }
        // The held control must remain enabled to receive release while capture makes CanPushToTalk false.
        button.IsEnabled = available && (held || main.CanPushToTalk);
    }

    private bool CanBegin() => !disposed && available && !held && !releasing
        && main.EndPushToTalkCommand.CanExecute(null) && main.BeginPushToTalkCommand.CanExecute(null);

    private void Begin()
    {
        held = true;
        Refresh();
        opening = BeginAsync();
    }

    internal void Release()
    {
        if (!held) { return; }
        held = false;
        heldKey = null;
        shortcutHeld = false;
        heldPointer = null;
        releasing = true;
        release = ReleaseAsync();
        Refresh();
    }

    private async Task BeginAsync()
    {
        try { await main.BeginPushToTalkCommand.ExecuteAsync(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { main.ReportHostInteractionFailure("Push-to-talk could not start. " + exception.Message); }
    }

    private async Task ReleaseAsync()
    {
        try
        {
            await main.EndPushToTalkCommand.ExecuteAsync();
#pragma warning disable VSTHRD003 // Key-up joins this binding's exact opening after requesting cancellation through the shared End command.
            await opening;
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { main.ReportHostInteractionFailure("Push-to-talk release could not be confirmed. " + exception.Message); }
        finally { releasing = false; Refresh(); }
    }

    private void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(button).Properties.IsLeftButtonPressed || !CanBegin()) { return; }
        args.Handled = true;
        args.Pointer.Capture(button);
        heldPointer = args.Pointer;
        Begin();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (!held || !ReferenceEquals(heldPointer, args.Pointer)) { return; }
        args.Handled = true;
        Release();
        args.Pointer.Capture(null);
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        var firstPress = pressedKeys.Add(args.Key);
        if (heldKey == args.Key) { args.Handled = true; return; }
        if (!firstPress || args.Handled || !CanBegin()) { return; }
        var buttonKey = ReferenceEquals(window.FocusManager?.GetFocusedElement(), button)
            && args.Key is Key.Space or Key.Enter && args.KeyModifiers == KeyModifiers.None;
        var shortcutKey = IsShortcutEligible() && main.EffectiveForegroundPttShortcut is { } shortcut
            && shortcut != ForegroundPttShortcut.Unassigned
            && args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
            && args.Key == (shortcut == ForegroundPttShortcut.ControlShiftSpace ? Key.Space : Key.F9);
        if (!buttonKey && !shortcutKey) { return; }
        args.Handled = true;
        heldKey = args.Key;
        shortcutHeld = !buttonKey;
        Begin();
    }

    private void OnKeyUp(object? sender, KeyEventArgs args)
    {
        pressedKeys.Remove(args.Key);
        if (!held || heldKey != args.Key) { return; }
        args.Handled = true;
        Release();
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (heldPointer is not null) { Release(); }
    }
    private void OnFocusLost(object? sender, FocusChangedEventArgs args) => Release();
    private void OnFocusChanged(object? sender, FocusChangedEventArgs args) => Release();
    private void OnUnavailable(object? sender, EventArgs args) => Release();
    private void OnClosed(object? sender, EventArgs args) => Dispose();
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty && !window.IsVisible) { Release(); }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { Dispatcher.UIThread.Post(() => OnMainChanged(sender, args)); return; }
        if (string.Equals(args.PropertyName, nameof(MainViewModel.EffectiveForegroundPttShortcut), StringComparison.Ordinal)) { Release(); }
        if (shortcutHeld && !IsShortcutEligible()) { Release(); }
        if (args.PropertyName is nameof(MainViewModel.CanPushToTalk))
        { Refresh(); }
    }

    private bool IsShortcutEligible()
    {
        if (!window.IsActive || !window.IsVisible || !button.IsVisible
            || main.IsResponseInteractionPending || !main.IsPushToTalkVisible) { return false; }
        var focused = window.FocusManager?.GetFocusedElement() as Control;
        return focused is null || !focused.GetVisualAncestors().Prepend(focused).Any(control =>
            control is TextBox or SelectingItemsControl or NumericUpDown or RangeBase or AutoCompleteBox);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        Release();
        disposed = true;
        button.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        button.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        window.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        window.RemoveHandler(InputElement.GotFocusEvent, OnFocusChanged);
        button.PointerCaptureLost -= OnCaptureLost;
        button.LostFocus -= OnFocusLost;
        window.Deactivated -= OnUnavailable;
        window.Closed -= OnClosed;
        window.PropertyChanged -= OnWindowChanged;
        main.PropertyChanged -= OnMainChanged;
    }
}
