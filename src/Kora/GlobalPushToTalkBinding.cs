using System.ComponentModel;

using Avalonia.Threading;

using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Voice;
using Kora.Windows.Input;

namespace Kora;

internal sealed class GlobalPushToTalkBinding : IDisposable
{
    private readonly MainViewModel main;
    private readonly GlobalPushToTalkHotkey hotkey;
    private long held;
    private bool disposed;
    private bool releasing;
    private Task opening = Task.CompletedTask;
    private Task release = Task.CompletedTask;
    private Task shutdown = Task.CompletedTask;

#pragma warning disable VSTHRD003 // Exposes this binding's exact input/capture teardown for the desktop shutdown join.
    internal Task Quiescence => shutdown;
#pragma warning restore VSTHRD003

    internal GlobalPushToTalkBinding(MainViewModel main, GlobalPttConfigurationService configuration,
        GlobalPushToTalkHotkey hotkey)
    {
        this.main = main;
        this.hotkey = hotkey;
        hotkey.HoldChanged += OnHoldChanged;
        hotkey.Failed += OnNativeFailure;
        main.PropertyChanged += OnMainChanged;
        main.PrivacyClosureRequested += OnPrivacyClosure;
        main.BindGlobalPtt(configuration, ConfigureAsync);
    }

    internal async Task InitializeAsync()
    {
        try { await ConfigureAsync(main.EffectiveGlobalPttShortcut); }
        catch (InvalidOperationException exception) { main.ReportHostInteractionFailure(exception.Message); }
    }

    private async Task ConfigureAsync(GlobalPttShortcut? shortcut)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        RequestRelease();
#pragma warning disable VSTHRD003 // Changing the registration joins this binding's old hold before admitting a new chord.
        await release;
#pragma warning restore VSTHRD003
        try
        {
            await hotkey.ConfigureAsync(shortcut);
            main.ReportGlobalPttRegistration(shortcut is null || shortcut == GlobalPttShortcut.Unassigned
                ? "Global shortcut is not registered."
                : "Registered. Hold while any application has focus; release any chord key to finish.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ObjectDisposedException)
        {
            main.ReportGlobalPttRegistration("Registration unavailable. " + exception.Message + ". No fallback shortcut.");
            throw new InvalidOperationException("Global push-to-talk registration unavailable. " + exception.Message, exception);
        }
    }

    private void OnHoldChanged(long generation, bool pressed) => Dispatcher.UIThread.Post(() =>
    {
        if (!pressed)
        {
            if (held == generation) { RequestRelease(); }
            return;
        }
        if (disposed || held != 0 || releasing || !hotkey.IsHeld(generation)
            || !main.IsGlobalPttHostEligible || !main.BeginPushToTalkCommand.CanExecute(null)
            || !main.EndPushToTalkCommand.CanExecute(null)) { return; }
        held = generation;
        opening = BeginAsync();
    });

    private async Task BeginAsync()
    {
        try { await main.BeginPushToTalkCommand.ExecuteAsync(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { main.ReportHostInteractionFailure("Global push-to-talk could not start. " + exception.Message); }
    }

    private void RequestRelease()
    {
        if (held == 0) { return; }
        held = 0;
        releasing = true;
        release = ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        try
        {
            await main.EndPushToTalkCommand.ExecuteAsync();
#pragma warning disable VSTHRD003 // End requests cancellation first, then joins the exact opening owned by this hold.
            await opening;
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { main.ReportHostInteractionFailure("Global push-to-talk release could not be confirmed. " + exception.Message); }
        finally { releasing = false; }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { Dispatcher.UIThread.Post(() => OnMainChanged(sender, args)); return; }
        if (!main.IsGlobalPttHostEligible) { RequestRelease(); }
    }

    private void OnPrivacyClosure(object? sender, EventArgs args) => RequestRelease();
    private void OnNativeFailure(Exception exception) => Dispatcher.UIThread.Post(() =>
    {
        RequestRelease();
        main.ReportGlobalPttRegistration("Native input unavailable. No fallback shortcut.");
        main.ReportHostInteractionFailure("Global push-to-talk input failed. " + exception.Message);
    });

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PropertyChanged -= OnMainChanged;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        hotkey.HoldChanged -= OnHoldChanged;
        hotkey.Failed -= OnNativeFailure;
        RequestRelease();
        shutdown = ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        try { await hotkey.DisposeAsync().ConfigureAwait(false); }
        finally
        {
#pragma warning disable VSTHRD003 // Native thread is stopped; join the exact capture release before service disposal.
            await release.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
    }
}
