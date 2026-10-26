using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Kora;

internal sealed class ReplyPresentationFence : IDisposable
{
    private readonly Control presenter;
    private Window? root;
    private long revision;

    internal ReplyPresentationFence(Control presenter)
    {
        this.presenter = presenter;
        presenter.PropertyChanged += Changed;
        presenter.AttachedToVisualTree += Attached;
        presenter.DetachedFromVisualTree += Detached;
        ObserveRoot();
    }

    internal Func<bool> Capture()
    {
        if (!presenter.IsEffectivelyVisible)
        { throw new InvalidOperationException("The original reply presenter is not visible."); }
        var version = Interlocked.Read(ref revision);
        // Provider/source validators run off-dispatcher; only the native events read controls.
        return () => Interlocked.Read(ref revision) == version;
    }

    internal void Retire() => Interlocked.Increment(ref revision);

    private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty || args.Property == StyledElement.DataContextProperty) { Retire(); }
    }

    private void Attached(object? sender, VisualTreeAttachmentEventArgs args)
    { Retire(); ObserveRoot(); }

    private void Detached(object? sender, VisualTreeAttachmentEventArgs args)
    { Retire(); ReleaseRoot(); }

    private void ObserveRoot()
    {
        ReleaseRoot();
        root = presenter as Window ?? TopLevel.GetTopLevel(presenter) as Window;
        if (root is not null && !ReferenceEquals(root, presenter)) { root.PropertyChanged += Changed; }
    }

    private void ReleaseRoot()
    {
        if (root is not null && !ReferenceEquals(root, presenter)) { root.PropertyChanged -= Changed; }
        root = null;
    }

    public void Dispose()
    {
        Retire();
        ReleaseRoot();
        presenter.PropertyChanged -= Changed;
        presenter.AttachedToVisualTree -= Attached;
        presenter.DetachedFromVisualTree -= Detached;
    }
}
