using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Hosting;

namespace Kora;

internal sealed class SessionRoutingPresentationLease(
    Window source, ForegroundConversationTarget target, Action revalidate, Action release) : IDisposable
{
    private readonly HashSet<Window> owned = [];
    private bool transitioning;
    private Window? closing;
    private bool disposed;
    private long transitionRevision;

    internal bool Retains(ForegroundConversationTarget? original, Window? foreground) =>
        !disposed && ReferenceEquals(original, target) && target.IsCurrent && source.IsVisible
        && (source is not ResponseWindow response || response.IsSessionListPresented)
        && (ReferenceEquals(foreground, source) || foreground is null
            && (transitioning || owned.Any(window => window.IsVisible && window.IsActive)));

    internal void Show(Window window)
    {
        if (disposed || !target.IsCurrent || !source.IsVisible)
        { throw new OperationCanceledException("The exact routing source presentation was retired."); }
        if (!owned.Add(window)) { throw new InvalidOperationException("The routing window is already owned."); }
        window.PropertyChanged += Changed;
        window.Closing += Closing;
        window.Closed += Closed;
        BeginTransition();
        try { window.Show(); window.Activate(); }
        finally { EndTransition(); }
    }

    internal void Withdraw(Window window)
    {
        if (owned.Contains(window) && window.IsActive && target.IsCurrent)
        { closing = window; BeginTransition(); }
        if (window is SessionRoutingAdviceWindow advice) { advice.Withdraw(); }
        else if (window is SessionRoutingWindow destination) { destination.Withdraw(); }
        else { throw new InvalidOperationException("Only the exact routing windows can be withdrawn."); }
    }

    private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.IsActiveProperty && sender is Window window)
        {
            if (window.IsActive || !ReferenceEquals(window, closing))
            {
                transitioning = false;
                transitionRevision++;
            }
            revalidate();
        }
        else if (args.Property == Visual.IsVisibleProperty) { revalidate(); }
    }

    private void Closing(object? sender, WindowClosingEventArgs args)
    {
        if (sender is Window { IsActive: true } window && target.IsCurrent)
        { closing = window; BeginTransition(); }
    }

    private void Closed(object? sender, EventArgs args)
    {
        if (sender is not Window window) { return; }
        Forget(window);
        if (transitioning && target.IsCurrent && source.IsVisible) { source.Activate(); }
        closing = null;
        EndTransition();
    }

    private void BeginTransition()
    {
        transitionRevision++;
        transitioning = true;
    }

    private void EndTransition()
    {
        var revision = transitionRevision;
        // Expire behind the input-priority activation notification, not before the native focus transfer.
        Dispatcher.UIThread.Post(() =>
        {
            if (disposed || revision != transitionRevision) { return; }
            transitioning = false;
            revalidate();
        }, DispatcherPriority.Input);
    }

    private void Forget(Window window)
    {
        owned.Remove(window);
        window.PropertyChanged -= Changed;
        window.Closing -= Closing;
        window.Closed -= Closed;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        foreach (var window in owned.ToArray()) { Forget(window); }
        release();
    }
}
