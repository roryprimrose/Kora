using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Kora;

internal sealed class NativeViewLifetime : IDisposable
{
    private readonly Control view;
    private readonly Window owner;
    private readonly Func<bool> admitted;
    private volatile bool disposed;
    private int presented;
    private int active;
    private long revision;
    internal bool IsPresented => Volatile.Read(ref presented) == 1;
    internal bool IsActive => Volatile.Read(ref active) == 1;
    internal event EventHandler? PresentationChanged;
    internal event EventHandler? ActivationChanged;

    internal NativeViewLifetime(Control view, Window owner, Func<bool> admitted)
    {
        this.view = view;
        this.owner = owner;
        this.admitted = admitted;
        view.PropertyChanged += OnPropertyChanged;
        view.AttachedToVisualTree += OnTreeChanged;
        view.DetachedFromVisualTree += OnTreeChanged;
        owner.PropertyChanged += OnPropertyChanged;
        owner.Activated += OnActivationChanged;
        owner.Deactivated += OnActivationChanged;
        owner.Closed += OnClosed;
        Refresh();
    }

    internal Func<bool> Capture()
    {
        var captured = Interlocked.Read(ref revision);
        return () => !disposed && IsPresented && captured == Interlocked.Read(ref revision) && admitted();
    }

    internal void Refresh()
    {
        var visible = !disposed && view.IsEffectivelyVisible && ReferenceEquals(TopLevel.GetTopLevel(view), owner)
            && owner.IsVisible && owner.WindowState != WindowState.Minimized && admitted();
        var foreground = visible && owner.IsActive;
        if (visible != IsPresented)
        {
            Volatile.Write(ref presented, visible ? 1 : 0);
            Interlocked.Increment(ref revision);
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        if (foreground != IsActive)
        {
            Volatile.Write(ref active, foreground ? 1 : 0);
            ActivationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty || args.Property == Window.WindowStateProperty
            || args.Property == Window.IsActiveProperty)
        { Refresh(); }
    }
    private void OnTreeChanged(object? sender, VisualTreeAttachmentEventArgs args) => Refresh();
    private void OnActivationChanged(object? sender, EventArgs args) => Refresh();
    private void OnClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        view.PropertyChanged -= OnPropertyChanged;
        view.AttachedToVisualTree -= OnTreeChanged;
        view.DetachedFromVisualTree -= OnTreeChanged;
        owner.PropertyChanged -= OnPropertyChanged;
        owner.Activated -= OnActivationChanged;
        owner.Deactivated -= OnActivationChanged;
        owner.Closed -= OnClosed;
        Refresh();
    }
}
